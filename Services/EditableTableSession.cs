using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace NavMeCat.Services;

/// <summary>
/// Holds an open, editable view of a single table. Changes made to <see cref="Data"/>
/// (in-place edits, new rows, deleted rows) are pushed back to SQL Server on <see cref="SaveAsync"/>.
/// Requires the table to have a primary key for updates/deletes.
/// </summary>
public sealed class EditableTableSession : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDataAdapter _adapter;
    // The command builder hooks the adapter's row-update events, so it must be kept alive.
    private readonly SqlCommandBuilder _builder;

    public DataTable Data { get; }
    public string Database { get; }
    public string Schema { get; }
    public string Table { get; }
    public int RowLimit { get; }

    public string Identifier => $"{Database}.{Schema}.{Table}";

    private EditableTableSession(SqlConnection connection, SqlDataAdapter adapter,
        SqlCommandBuilder builder, DataTable data,
        string database, string schema, string table, int rowLimit)
    {
        _connection = connection;
        _adapter = adapter;
        _builder = builder;
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
            // Pull key + schema info so the command builder can generate UPDATE/DELETE.
            MissingSchemaAction = MissingSchemaAction.AddWithKey
        };
        var builder = new SqlCommandBuilder(adapter)
        {
            // Key-only WHERE clause: cleaner generated SQL and the user's edits win.
            ConflictOption = ConflictOption.OverwriteChanges
        };

        var data = new DataTable(table);
        await Task.Run(() => adapter.Fill(data));

        return new EditableTableSession(connection, adapter, builder, data,
            database, schema, table, rowLimit);
    }

    /// <summary>Pushes pending changes to the server. Returns the number of affected rows.</summary>
    public async Task<int> SaveAsync()
    {
        var changes = Data.GetChanges();
        if (changes is null) return 0;

        var affected = await Task.Run(() => _adapter.Update(Data));
        Data.AcceptChanges();
        return affected;
    }

    public bool HasChanges => Data.GetChanges() is not null;

    /// <summary>
    /// Builds a readable preview of the INSERT/UPDATE/DELETE statements that <see cref="SaveAsync"/>
    /// will run for the current pending changes. Values are inlined for readability — the real
    /// save sends them as parameters.
    /// </summary>
    public List<string> BuildChangePreview()
    {
        var statements = new List<string>();
        if (Data.GetChanges() is null) return statements;

        SqlCommand? insert = null, update = null, delete = null;

        foreach (DataRow row in Data.Rows)
        {
            try
            {
                switch (row.RowState)
                {
                    case DataRowState.Added:
                        insert ??= _builder.GetInsertCommand();
                        statements.Add(RenderStatement(insert, row));
                        break;
                    case DataRowState.Modified:
                        update ??= _builder.GetUpdateCommand();
                        statements.Add(RenderStatement(update, row));
                        break;
                    case DataRowState.Deleted:
                        delete ??= _builder.GetDeleteCommand();
                        statements.Add(RenderStatement(delete, row));
                        break;
                }
            }
            catch (Exception ex)
            {
                statements.Add($"-- Could not generate statement: {ex.Message}");
            }
        }

        return statements;
    }

    private static string RenderStatement(SqlCommand command, DataRow row)
    {
        var text = command.CommandText;
        foreach (SqlParameter p in command.Parameters
                     .Cast<SqlParameter>()
                     .OrderByDescending(p => p.ParameterName.Length))
        {
            object value;
            try
            {
                var version = p.SourceVersion == DataRowVersion.Default ? DataRowVersion.Current : p.SourceVersion;
                value = string.IsNullOrEmpty(p.SourceColumn) ? DBNull.Value : row[p.SourceColumn, version];
            }
            catch
            {
                value = DBNull.Value;
            }
            text = text.Replace(p.ParameterName, FormatLiteral(value));
        }
        return text.Trim();
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
        _builder.Dispose();
        _adapter.Dispose();
        _connection.Dispose();
        Data.Dispose();
    }
}
