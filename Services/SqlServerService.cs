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

    /// <summary>All user tables in the database as (schema, table).</summary>
    public static async Task<List<(string Schema, string Table)>> GetAllTablesAsync(string connectionString, string database)
    {
        var result = new List<(string, string)>();
        await using var conn = new SqlConnection(WithDatabase(connectionString, database));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT s.name, t.name
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
              ORDER BY s.name, t.name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) result.Add((r.GetString(0), r.GetString(1)));
        return result;
    }

    public static async Task<List<string>> GetColumnNamesAsync(string connectionString, string database, string schema, string table)
    {
        var result = new List<string>();
        await using var conn = new SqlConnection(WithDatabase(connectionString, database));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT c.name
              FROM sys.columns c JOIN sys.objects o ON o.object_id = c.object_id
              JOIN sys.schemas s ON o.schema_id = s.schema_id
              WHERE s.name = @s AND o.name = @t
              ORDER BY c.column_id", conn);
        cmd.Parameters.AddWithValue("@s", schema);
        cmd.Parameters.AddWithValue("@t", table);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) result.Add(r.GetString(0));
        return result;
    }

    /// <summary>All foreign keys in the database as (parentSchema,parentTable,parentCol,refSchema,refTable,refCol).</summary>
    public static async Task<List<(string PS, string PT, string PC, string RS, string RT, string RC)>>
        GetAllForeignKeysAsync(string connectionString, string database)
    {
        var result = new List<(string, string, string, string, string, string)>();
        await using var conn = new SqlConnection(WithDatabase(connectionString, database));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            @"SELECT OBJECT_SCHEMA_NAME(fk.parent_object_id), OBJECT_NAME(fk.parent_object_id), pc.name,
                     OBJECT_SCHEMA_NAME(fk.referenced_object_id), OBJECT_NAME(fk.referenced_object_id), rc.name
              FROM sys.foreign_keys fk
              JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
              JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
              JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
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
