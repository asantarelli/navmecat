using Microsoft.Data.Sqlite;

namespace NavMeCat.Services;

/// <summary>Reads SQLite metadata: tables and views from a database file.</summary>
public static class SqliteService
{
    public static async Task TestConnectionAsync(string connectionString)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
    }

    public static Task<List<string>> GetTablesAsync(string connectionString) =>
        GetObjectsAsync(connectionString, "table");

    public static Task<List<string>> GetViewsAsync(string connectionString) =>
        GetObjectsAsync(connectionString, "view");

    private static async Task<List<string>> GetObjectsAsync(string connectionString, string type)
    {
        var result = new List<string>();
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type = $type AND name NOT LIKE 'sqlite_%' ORDER BY name";
        cmd.Parameters.AddWithValue("$type", type);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    public static async Task<List<string>> GetColumnNamesAsync(string connectionString, string table)
    {
        var result = new List<string>();
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteLiteral(table)})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(1)); // 1 = name
        return result;
    }

    /// <summary>Bracket-quote an identifier for use inside SQL text.</summary>
    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>Quote an identifier as a string literal (for PRAGMA(...) arguments).</summary>
    private static string QuoteLiteral(string identifier) => "'" + identifier.Replace("'", "''") + "'";
}
