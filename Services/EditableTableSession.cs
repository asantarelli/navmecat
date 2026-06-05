using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace NavMeCat.Services;

/// <summary>
/// Holds an open, editable view of a single table. Changes made to <see cref="Data"/>
/// (in-place edits, new rows, deleted rows) are pushed back to SQL Server on <see cref="SaveAsync"/>.
///
/// Rows are matched for UPDATE/DELETE by, in order of preference: the primary key, a unique
/// index, or — for keyless tables — every comparable column (with TOP (1) so only one row is
/// affected). For keyless tables the caller can override which columns identify a row via
/// <see cref="SetRowIdentity"/>. UPDATEs only set the columns that actually changed.
/// </summary>
public sealed class EditableTableSession : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDataAdapter _adapter;
    private readonly HashSet<string> _nonComparable;

    // Current key (may be a custom row identity); plus the auto-resolved default to revert to.
    private DataColumn[] _keyColumns;
    private bool _useTopOne;
    private readonly DataColumn[] _defaultKeys;
    private readonly bool _defaultTopOne;
    private readonly string _defaultDescription;

    public DataTable Data { get; }
    public string Database { get; }
    public string Schema { get; }
    public string Table { get; }
    public int RowLimit { get; }

    public string KeyDescription { get; private set; }
    public bool HasReliableKey { get; private set; }
    /// <summary>True when the table has a real primary key or unique index (no identity picker needed).</summary>
    public bool HasNaturalKey { get; }
    public bool IsCustomIdentity { get; private set; }

    public string Identifier => $"{Database}.{Schema}.{Table}";
    public IReadOnlyList<string> AllColumnNames => Data.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
    public IReadOnlyList<string> KeyColumnNames => _keyColumns.Select(c => c.ColumnName).ToList();
    public IReadOnlyCollection<string> NonComparableColumns => _nonComparable;

    private EditableTableSession(SqlConnection connection, SqlDataAdapter adapter, DataTable data,
        string database, string schema, string table, int rowLimit,
        DataColumn[] keyColumns, bool useTopOne, string keyDescription, bool hasNaturalKey,
        HashSet<string> nonComparable)
    {
        _connection = connection;
        _adapter = adapter;
        Data = data;
        Database = database;
        Schema = schema;
        Table = table;
        RowLimit = rowLimit;
        _keyColumns = keyColumns;
        _useTopOne = useTopOne;
        _defaultKeys = keyColumns;
        _defaultTopOne = useTopOne;
        _defaultDescription = keyDescription;
        KeyDescription = keyDescription;
        HasNaturalKey = hasNaturalKey;
        HasReliableKey = hasNaturalKey;
        _nonComparable = nonComparable;
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    public static async Task<EditableTableSession> OpenAsync(
        string connectionString, string database, string schema, string table, int rowLimit)
    {
        var connection = new SqlConnection(SqlServerService.WithDatabase(connectionString, database));
        await connection.OpenAsync();

        var fq = $"{Quote(schema)}.{Quote(table)}";
        var sql = $"SELECT TOP ({rowLimit}) * FROM {fq}";
        var adapter = new SqlDataAdapter(sql, connection)
        {
            MissingSchemaAction = MissingSchemaAction.AddWithKey
        };

        var data = new DataTable(table);
        await Task.Run(() => adapter.Fill(data));

        var nonComparable = await GetNonComparableColumnsAsync(connection, fq);

        DataColumn[] keys;
        bool topOne;
        string description;
        bool naturalKey;

        if (data.PrimaryKey.Length > 0)
        {
            keys = data.PrimaryKey; topOne = false; description = "primary key"; naturalKey = true;
        }
        else
        {
            var unique = await GetFirstUniqueIndexAsync(connection, fq, data);
            if (unique.Length > 0)
            {
                keys = unique; topOne = false; description = "unique index"; naturalKey = true;
            }
            else
            {
                keys = data.Columns.Cast<DataColumn>()
                    .Where(c => c.DataType != typeof(byte[]) && !nonComparable.Contains(c.ColumnName))
                    .ToArray();
                topOne = keys.Length > 0; description = "all columns"; naturalKey = false;
            }
        }

        return new EditableTableSession(connection, adapter, data, database, schema, table, rowLimit,
            keys, topOne, description, naturalKey, nonComparable);
    }

    public bool HasChanges => Data.GetChanges() is not null;

    // ---- row identity (keyless tables) ----------------------------------

    /// <summary>Overrides which columns identify a row. Null/empty reverts to the default.</summary>
    public void SetRowIdentity(IReadOnlyList<string>? columns)
    {
        if (columns is null || columns.Count == 0)
        {
            _keyColumns = _defaultKeys;
            _useTopOne = _defaultTopOne;
            KeyDescription = _defaultDescription;
            HasReliableKey = HasNaturalKey;
            IsCustomIdentity = false;
            return;
        }

        var cols = columns.Where(Data.Columns.Contains).Select(n => Data.Columns[n]!).ToArray();
        if (cols.Length == 0) return;

        _keyColumns = cols;
        _useTopOne = true; // user-chosen identity — keep one-row safety
        KeyDescription = "custom (" + string.Join(", ", cols.Select(c => c.ColumnName)) + ")";
        HasReliableKey = true;
        IsCustomIdentity = true;
    }

    /// <summary>Suggests the smallest set of comparable columns whose values are unique across loaded rows.</summary>
    public List<string> DetectIdentityColumns()
    {
        var candidates = Data.Columns.Cast<DataColumn>()
            .Where(c => c.DataType != typeof(byte[]) && !_nonComparable.Contains(c.ColumnName))
            .ToList();
        var rows = Data.Rows.Cast<DataRow>().Where(r => r.RowState != DataRowState.Deleted).ToList();
        if (candidates.Count == 0 || rows.Count == 0) return new();

        int Distinct(IEnumerable<DataColumn> cols) =>
            rows.Select(r => string.Join("¦", cols.Select(c => r[c]?.ToString() ?? "∅")))
                .Distinct().Count();

        // Any single column that's already unique wins.
        var single = candidates.OrderByDescending(c => Distinct(new[] { c })).First();
        if (Distinct(new[] { single }) == rows.Count) return new() { single.ColumnName };

        // Greedy: keep adding the column that increases distinct combinations the most.
        var chosen = new List<DataColumn> { single };
        while (Distinct(chosen) < rows.Count && chosen.Count < candidates.Count)
        {
            DataColumn? best = null;
            var bestCount = Distinct(chosen);
            foreach (var c in candidates)
            {
                if (chosen.Contains(c)) continue;
                var d = Distinct(chosen.Append(c));
                if (d > bestCount) { bestCount = d; best = c; }
            }
            if (best is null) break;
            chosen.Add(best);
        }

        return chosen.Select(c => c.ColumnName).ToList();
    }

    private static async Task<DataColumn[]> GetFirstUniqueIndexAsync(SqlConnection conn, string fq, DataTable data)
    {
        const string sql = @"
            SELECT i.index_id, c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@fq) AND i.is_unique = 1 AND i.is_disabled = 0 AND i.has_filter = 0
            ORDER BY i.index_id, ic.key_ordinal";

        var byIndex = new Dictionary<int, List<string>>();
        await using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@fq", fq);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                if (!byIndex.TryGetValue(id, out var list)) byIndex[id] = list = new List<string>();
                list.Add(reader.GetString(1));
            }
        }

        foreach (var cols in byIndex.Values.OrderBy(c => c.Count))
            if (cols.All(data.Columns.Contains))
                return cols.Select(n => data.Columns[n]!).ToArray();
        return Array.Empty<DataColumn>();
    }

    private static async Task<HashSet<string>> GetNonComparableColumnsAsync(SqlConnection conn, string fq)
    {
        const string sql = @"
            SELECT c.name
            FROM sys.columns c
            JOIN sys.types t ON c.user_type_id = t.user_type_id
            WHERE c.object_id = OBJECT_ID(@fq)
              AND (t.name IN ('text','ntext','image','xml','geography','geometry','hierarchyid','sql_variant')
                   OR c.max_length = -1)";

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fq", fq);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    // ---- change generation ----------------------------------------------

    private sealed record ChangeStatement(string Sql, List<object> Values, string Preview);

    private List<ChangeStatement> BuildChanges()
    {
        var statements = new List<ChangeStatement>();
        var fqTable = $"{Quote(Schema)}.{Quote(Table)}";

        foreach (DataRow row in Data.Rows)
        {
            switch (row.RowState)
            {
                case DataRowState.Added:
                    statements.Add(BuildInsert(row, fqTable));
                    break;
                case DataRowState.Modified:
                    var update = BuildUpdate(row, fqTable);
                    if (update is not null) statements.Add(update);
                    break;
                case DataRowState.Deleted:
                    statements.Add(BuildDelete(row, fqTable));
                    break;
            }
        }

        return statements;
    }

    private void RequireKey()
    {
        if (_keyColumns.Length == 0)
            throw new InvalidOperationException(
                "No columns are available to identify a row for update/delete. Set a row identity, " +
                "or add a primary key / unique index to the table.");
    }

    private ChangeStatement BuildInsert(DataRow row, string fqTable)
    {
        var values = new List<object>();
        var names = new List<string>();
        var placeholders = new List<string>();
        var previewValues = new List<string>();

        foreach (DataColumn c in Data.Columns)
        {
            if (c.AutoIncrement) continue;
            var v = row[c, DataRowVersion.Current];
            names.Add(Quote(c.ColumnName));
            placeholders.Add("@p" + values.Count);
            previewValues.Add(FormatLiteral(v));
            values.Add(v);
        }

        var cols = string.Join(", ", names);
        var sql = $"INSERT INTO {fqTable} ({cols}) VALUES ({string.Join(", ", placeholders)})";
        var preview = $"INSERT INTO {fqTable} ({cols}) VALUES ({string.Join(", ", previewValues)})";
        return new ChangeStatement(sql, values, preview);
    }

    private ChangeStatement? BuildUpdate(DataRow row, string fqTable)
    {
        RequireKey();

        var changed = new List<DataColumn>();
        foreach (DataColumn c in Data.Columns)
        {
            if (c.AutoIncrement) continue;
            if (!ValuesEqual(row[c, DataRowVersion.Original], row[c, DataRowVersion.Current]))
                changed.Add(c);
        }
        if (changed.Count == 0) return null;

        var top = _useTopOne ? "TOP (1) " : "";
        var values = new List<object>();
        var sql = new StringBuilder($"UPDATE {top}{fqTable} SET ");
        var preview = new StringBuilder($"UPDATE {top}{fqTable} SET ");

        for (var i = 0; i < changed.Count; i++)
        {
            var c = changed[i];
            var v = row[c, DataRowVersion.Current];
            var sep = i > 0 ? ", " : "";
            sql.Append(sep).Append($"{Quote(c.ColumnName)} = @p{values.Count}");
            preview.Append(sep).Append($"{Quote(c.ColumnName)} = {FormatLiteral(v)}");
            values.Add(v);
        }

        AppendKeyWhere(sql, preview, row, values);
        return new ChangeStatement(sql.ToString(), values, preview.ToString());
    }

    private ChangeStatement BuildDelete(DataRow row, string fqTable)
    {
        RequireKey();

        var top = _useTopOne ? "TOP (1) " : "";
        var values = new List<object>();
        var sql = new StringBuilder($"DELETE {top}FROM {fqTable}");
        var preview = new StringBuilder($"DELETE {top}FROM {fqTable}");
        AppendKeyWhere(sql, preview, row, values);
        return new ChangeStatement(sql.ToString(), values, preview.ToString());
    }

    private void AppendKeyWhere(StringBuilder sql, StringBuilder preview, DataRow row, List<object> values)
    {
        sql.Append(" WHERE ");
        preview.Append(" WHERE ");
        var first = true;
        foreach (var c in _keyColumns)
        {
            var sep = first ? "" : " AND ";
            first = false;
            var v = row[c, DataRowVersion.Original];
            if (v is DBNull)
            {
                sql.Append(sep).Append($"{Quote(c.ColumnName)} IS NULL");
                preview.Append(sep).Append($"{Quote(c.ColumnName)} IS NULL");
            }
            else
            {
                sql.Append(sep).Append($"{Quote(c.ColumnName)} = @p{values.Count}");
                preview.Append(sep).Append($"{Quote(c.ColumnName)} = {FormatLiteral(v)}");
                values.Add(v);
            }
        }
    }

    // ---- save / preview --------------------------------------------------

    public async Task<int> SaveAsync()
    {
        var changes = BuildChanges();
        if (changes.Count == 0) return 0;

        var affected = 0;
        foreach (var change in changes)
        {
            await using var cmd = new SqlCommand(change.Sql, _connection);
            for (var i = 0; i < change.Values.Count; i++)
                cmd.Parameters.AddWithValue("@p" + i, change.Values[i] ?? DBNull.Value);
            affected += await cmd.ExecuteNonQueryAsync();
        }

        Data.AcceptChanges();
        return affected;
    }

    public List<string> BuildChangePreview()
    {
        try
        {
            return BuildChanges().Select(c => c.Preview).ToList();
        }
        catch (Exception ex)
        {
            return new List<string> { "-- Could not generate preview: " + ex.Message };
        }
    }

    private static bool ValuesEqual(object a, object b)
    {
        if (a is DBNull && b is DBNull) return true;
        if (a is DBNull || b is DBNull) return false;
        if (a is byte[] ba && b is byte[] bb) return ba.AsSpan().SequenceEqual(bb);
        return a.Equals(b);
    }

    private static string FormatLiteral(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => "'" + s.Replace("'", "''") + "'",
        bool b => b ? "1" : "0",
        DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'",
        Guid g => "'" + g + "'",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => "'" + value + "'"
    };

    public void Dispose()
    {
        _adapter.Dispose();
        _connection.Dispose();
        Data.Dispose();
    }
}
