using System.Data;
using Microsoft.Data.SqlClient;

namespace NavMeCat.Services;

/// <summary>Reads SQL Server metadata: databases, schemas, tables.</summary>
public static class SqlServerService
{
    /// <summary>Returns a copy of the connection string pointed at a specific database.</summary>
    public static string WithDatabase(string connectionString, string database)
        => new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;

    public static async Task TestConnectionAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
    }

    public static async Task<List<string>> GetDatabasesAsync(string connectionString)
    {
        var result = new List<string>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>Schemas in the given database that own any user object.</summary>
    public static async Task<List<string>> GetSchemasAsync(string connectionString, string database)
    {
        var result = new List<string>();
        await using var conn = new SqlConnection(WithDatabase(connectionString, database));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT DISTINCT s.name
              FROM sys.schemas s
              JOIN sys.objects o ON o.schema_id = s.schema_id
              WHERE o.type IN ('U','V','P','FN','IF','TF','FS','FT')
              ORDER BY s.name", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    public static Task<List<string>> GetTablesAsync(string connectionString, string database, string schema) =>
        GetObjectsAsync(connectionString, database, schema, "sys.tables");

    public static Task<List<string>> GetViewsAsync(string connectionString, string database, string schema) =>
        GetObjectsAsync(connectionString, database, schema, "sys.views");

    public static Task<List<string>> GetProceduresAsync(string connectionString, string database, string schema) =>
        GetObjectsAsync(connectionString, database, schema, "sys.procedures");

    public static async Task<List<string>> GetFunctionsAsync(string connectionString, string database, string schema)
    {
        var result = new List<string>();
        await using var conn = new SqlConnection(WithDatabase(connectionString, database));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT o.name
              FROM sys.objects o
              JOIN sys.schemas s ON o.schema_id = s.schema_id
              WHERE s.name = @schema AND o.type IN ('FN','IF','TF','FS','FT')
              ORDER BY o.name", conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<List<string>> GetObjectsAsync(
        string connectionString, string database, string schema, string sysView)
    {
        var result = new List<string>();
        await using var conn = new SqlConnection(WithDatabase(connectionString, database));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            $@"SELECT o.name
               FROM {sysView} o
               JOIN sys.schemas s ON o.schema_id = s.schema_id
               WHERE s.name = @schema
               ORDER BY o.name", conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }
}
