using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace NavMeCat.Services;

/// <summary>
/// Holds an open, editable view of a single table. Changes made to <see cref="Data"/>
/// (in-place edits, new rows, deleted rows) are pushed back to SQL Server on <see cref="SaveAsync"/>.
/// UPDATEs only touch the columns that actually changed; UPDATE/DELETE need a primary key.
/// </summary>
public sealed class EditableTableSession : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDataAdapter _adapter;

    public DataTable Data { get; }
    public string Database { get; }
    public string Schema { get; }
    public string Table { get; }
    public int RowLimit { get; }

    public string Identifier => $"{Database}.{Schema}.{Table}";

    private EditableTableSession(SqlConnection connection, SqlDataAdapter adapter, DataTable data,
        string database, string schema, string table, int rowLimit)
    {
        _connection = connection;
        _adapter = adapter;
        Data = data;
        Database = database;
        Schema = schema;
        Table = table;
        RowLimit = rowLimit;
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
            // Pull key + schema info so we know the primary key for UPDATE/DELETE.
            MissingSchemaAction = MissingSchemaAction.AddWithKey
        };

        var data = new DataTable(table);
        await Task.Run(() => adapter.Fill(data));

        return new EditableTableSession(connection, adapter, data, database, schema, table, rowLimit);
    }

    public bool HasChanges => Data.GetChanges() is not null;

    // ---- change generation ----------------------------------------------

    private sealed record ChangeStatement(string Sql, List<object> Values, string Preview);

    private List<ChangeStatement> BuildChanges()
    {
        var statements = new List<ChangeStatement>();
        var fqTable = $"{Quote(Schema)}.{Quote(Table)}";
        var pk = Data.PrimaryKey;

        foreach (DataRow row in Data.Rows)
        {
            switch (row.RowState)
            {
                case DataRowState.Added:
                    statements.Add(BuildInsert(row, fqTable));
                    break;
                case DataRowState.Modified:
                    var update = BuildUpdate(row, fqTable, pk);
                    if (update is not null) statements.Add(update);
                    break;
                case DataRowState.Deleted:
                    statements.Add(BuildDelete(row, fqTable, pk));
                    break;
            }
        }

        return statements;
    }

    private ChangeStatement BuildInsert(DataRow row, string fqTable)
    {
        var values = new List<object>();
        var names = new List<string>();
        var paramPlaceholders = new List<string>();
        var previewValues = new List<string>();

        foreach (DataColumn c in Data.Columns)
        {
            if (c.AutoIncrement) continue;
            var v = row[c, DataRowVersion.Current];
            names.Add(Quote(c.ColumnName));
            paramPlaceholders.Add("@p" + values.Count);
            previewValues.Add(FormatLiteral(v));
            values.Add(v);
        }

        var cols = string.Join(", ", names);
        var sql = $"INSERT INTO {fqTable} ({cols}) VALUES ({string.Join(", ", paramPlaceholders)})";
        var preview = $"INSERT INTO {fqTable} ({cols}) VALUES ({string.Join(", ", previewValues)})";
        return new ChangeStatement(sql, values, preview);
    }

    private ChangeStatement? BuildUpdate(DataRow row, string fqTable, DataColumn[] pk)
    {
        if (pk.Length == 0)
            throw new InvalidOperationException("Table has no primary key; cannot generate UPDATE.");

        var changed = new List<DataColumn>();
        foreach (DataColumn c in Data.Columns)
        {
            if (c.AutoIncrement) continue;
            if (!ValuesEqual(row[c, DataRowVersion.Original], row[c, DataRowVersion.Current]))
                changed.Add(c);
        }
        if (changed.Count == 0) return null; // nothing really changed

        var values = new List<object>();
        var sql = new StringBuilder($"UPDATE {fqTable} SET ");
        var preview = new StringBuilder($"UPDATE {fqTable} SET ");

        for (var i = 0; i < changed.Count; i++)
        {
            var c = changed[i];
            var v = row[c, DataRowVersion.Current];
            var sep = i > 0 ? ", " : "";
            sql.Append(sep).Append($"{Quote(c.ColumnName)} = @p{values.Count}");
            preview.Append(sep).Append($"{Quote(c.ColumnName)} = {FormatLiteral(v)}");
            values.Add(v);
        }

        AppendKeyWhere(sql, preview, row, pk, values);
        return new ChangeStatement(sql.ToString(), values, preview.ToString());
    }

    private ChangeStatement BuildDelete(DataRow row, string fqTable, DataColumn[] pk)
    {
        if (pk.Length == 0)
            throw new InvalidOperationException("Table has no primary key; cannot generate DELETE.");

        var values = new List<object>();
        var sql = new StringBuilder($"DELETE FROM {fqTable}");
        var preview = new StringBuilder($"DELETE FROM {fqTable}");
        AppendKeyWhere(sql, preview, row, pk, values);
        return new ChangeStatement(sql.ToString(), values, preview.ToString());
    }

    private void AppendKeyWhere(StringBuilder sql, StringBuilder preview, DataRow row, DataColumn[] pk, List<object> values)
    {
        sql.Append(" WHERE ");
        preview.Append(" WHERE ");
        for (var i = 0; i < pk.Length; i++)
        {
            var c = pk[i];
            var v = row[c, DataRowVersion.Original];
            var sep = i > 0 ? " AND " : "";
            sql.Append(sep).Append($"{Quote(c.ColumnName)} = @p{values.Count}");
            preview.Append(sep).Append($"{Quote(c.ColumnName)} = {FormatLiteral(v)}");
            values.Add(v);
        }
    }

    // ---- save / preview --------------------------------------------------

    /// <summary>Pushes pending changes to the server. Returns the number of affected rows.</summary>
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

    /// <summary>Readable preview of the statements Save will run (values inlined for readability).</summary>
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
