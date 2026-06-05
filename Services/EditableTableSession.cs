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
/// affected). UPDATEs only set the columns that actually changed.
/// </summary>
public sealed class EditableTableSession : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDataAdapter _adapter;
    private readonly DataColumn[] _keyColumns;
    private readonly bool _useTopOne;

    public DataTable Data { get; }
    public string Database { get; }
    public string Schema { get; }
    public string Table { get; }
    public int RowLimit { get; }

    /// <summary>How rows are identified for save: "primary key", "unique index", or "all columns".</summary>
    public string KeyDescription { get; }
    public bool HasReliableKey { get; }

    public string Identifier => $"{Database}.{Schema}.{Table}";

    private EditableTableSession(SqlConnection connection, SqlDataAdapter adapter, DataTable data,
        string database, string schema, string table, int rowLimit,
        DataColumn[] keyColumns, bool useTopOne, string keyDescription, bool hasReliableKey)
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
        KeyDescription = keyDescription;
        HasReliableKey = hasReliableKey;
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    public static async Task<EditableTableSession> OpenAsync(
        string connectionString, string database, string schema, string table, int rowLimit)
    {
        var connection = new SqlConnection(SqlServerService.WithDatabase(connectionString, database));
        await connection.OpenAsync();

        var sql = $"SELECT TOP ({rowLimit}) * FROM {Quote(schema)}.{Quote(table)}";
        var adapter = new SqlDataAdapter(sql, connection)
        {
            MissingSchemaAction = MissingSchemaAction.AddWithKey
        };

        var data = new DataTable(table);
        await Task.Run(() => adapter.Fill(data));

        var (keys, topOne, description, reliable) = await ResolveKeyAsync(connection, schema, table, data);

        return new EditableTableSession(connection, adapter, data, database, schema, table, rowLimit,
            keys, topOne, description, reliable);
    }

    public bool HasChanges => Data.GetChanges() is not null;

    // ---- key resolution --------------------------------------------------

    private static async Task<(DataColumn[] Keys, bool TopOne, string Description, bool Reliable)>
        ResolveKeyAsync(SqlConnection conn, string schema, string table, DataTable data)
    {
        if (data.PrimaryKey.Length > 0)
            return (data.PrimaryKey, false, "primary key", true);

        var fq = $"{Quote(schema)}.{Quote(table)}";

        var unique = await GetFirstUniqueIndexAsync(conn, fq, data);
        if (unique.Length > 0)
            return (unique, false, "unique index", true);

        // Keyless: match on every comparable column, limiting to one affected row.
        var nonComparable = await GetNonComparableColumnsAsync(conn, fq);
        var keys = data.Columns.Cast<DataColumn>()
            .Where(c => c.DataType != typeof(byte[]) && !nonComparable.Contains(c.ColumnName))
            .ToArray();
        return (keys, keys.Length > 0, "all columns", false);
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

        // Prefer the unique index with the fewest columns that are all present in the loaded data.
        foreach (var cols in byIndex.Values.OrderBy(c => c.Count))
        {
            if (cols.All(data.Columns.Contains))
                return cols.Select(n => data.Columns[n]!).ToArray();
        }
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
                "This table has no primary key, unique index, or comparable columns, so rows can't be " +
                "identified for update/delete.");
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
