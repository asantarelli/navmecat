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

    public record SqliteColumn(string Name, string Type, bool NotNull, int Pk, string? Default);

    /// <summary>Column details for the table designer (PRAGMA table_info order).</summary>
    public static async Task<List<SqliteColumn>> GetColumnDetailsAsync(string connectionString, string table)
    {
        var result = new List<SqliteColumn>();
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteLiteral(table)})";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add(new SqliteColumn(
                r.GetString(1),                              // name
                r.IsDBNull(2) ? "" : r.GetString(2),         // type
                r.GetInt32(3) != 0,                          // notnull
                r.GetInt32(5),                               // pk position (0 = no)
                r.IsDBNull(4) ? null : r.GetValue(4)?.ToString())); // dflt_value
        return result;
    }

    /// <summary>Explicit (CREATE INDEX) secondary indexes — not the implicit PK/unique-constraint ones.</summary>
    public static async Task<List<(string Name, bool Unique, List<string> Columns)>> GetIndexesAsync(
        string connectionString, string table)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        var indexes = new List<(string Name, bool Unique)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA index_list({QuoteLiteral(table)})";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var name = r.GetString(1);
                var unique = r.GetInt32(2) != 0;
                var origin = r.IsDBNull(3) ? "c" : r.GetString(3); // c = CREATE INDEX, u = unique constr, pk
                if (origin == "c") indexes.Add((name, unique));
            }
        }

        var result = new List<(string, bool, List<string>)>();
        foreach (var (name, unique) in indexes)
        {
            var cols = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA index_info({QuoteLiteral(name)})";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (!r.IsDBNull(2)) cols.Add(r.GetString(2)); // column name
            result.Add((name, unique, cols));
        }
        return result;
    }

    /// <summary>Runs a (possibly multi-statement) script. Returns rows affected by the last statement.</summary>
    public static async Task<int> ExecuteScriptAsync(string connectionString, string sql)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Bracket-quote an identifier for use inside SQL text.</summary>
    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>Quote an identifier as a string literal (for PRAGMA(...) arguments).</summary>
    private static string QuoteLiteral(string identifier) => "'" + identifier.Replace("'", "''") + "'";
}
