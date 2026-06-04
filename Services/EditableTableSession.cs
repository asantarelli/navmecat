using System.Data;
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
        var builder = new SqlCommandBuilder(adapter);

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

    public void Dispose()
    {
        _builder.Dispose();
        _adapter.Dispose();
        _connection.Dispose();
        Data.Dispose();
    }
}
