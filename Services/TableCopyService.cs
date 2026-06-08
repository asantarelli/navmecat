using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using NavMeCat.Models;

namespace NavMeCat.Services;

/// <summary>Duplicates a table/collection within the same connection (structure, optionally with data).</summary>
public static class TableCopyService
{
    /// <summary>Existing table/collection names at the target location (for picking a free copy name).</summary>
    public static Task<List<string>> ListObjectsAsync(ConnectionProfile p, string database, string schema)
    {
        var cs = p.BuildConnectionString();
        return p.Engine switch
        {
            DatabaseEngine.Sqlite => SqliteService.GetTablesAsync(cs),
            DatabaseEngine.Firebird => FirebirdService.GetTablesAsync(cs),
            DatabaseEngine.MongoDb => MongoService.ListCollectionsAsync(cs, database),
            _ => SqlServerService.GetTablesAsync(cs, database, schema)
        };
    }

    /// <summary>Picks "name", else "name_copy", "name_copy2", … that isn't already taken.</summary>
    public static string FreeName(string name, ICollection<string> existing)
    {
        bool Taken(string n) => existing.Contains(n) ||
            existing.Any(e => string.Equals(e, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name)) return name;
        var candidate = name + "_copy";
        var i = 2;
        while (Taken(candidate)) candidate = $"{name}_copy{i++}";
        return candidate;
    }

    public static Task CopyAsync(ConnectionProfile p,
        string srcDatabase, string srcSchema, string srcName,
        string tgtDatabase, string tgtSchema, string newName, bool includeData)
        => p.Engine switch
        {
            DatabaseEngine.Sqlite => CopySqliteAsync(p, srcName, newName, includeData),
            DatabaseEngine.Firebird => CopyFirebirdAsync(p, srcName, newName, includeData),
            DatabaseEngine.MongoDb => CopyMongoAsync(p, tgtDatabase, srcName, newName, includeData),
            _ => CopySqlServerAsync(p, tgtDatabase, srcSchema, srcName, tgtSchema, newName, includeData)
        };

    private static string B(string id) => "[" + id.Replace("]", "]]") + "]";

    // ---- SQL Server (SELECT INTO copies columns + identity) --------------
    private static async Task CopySqlServerAsync(ConnectionProfile p, string db,
        string srcSchema, string srcName, string tgtSchema, string newName, bool includeData)
    {
        var src = $"{B(srcSchema)}.{B(srcName)}";
        var dst = $"{B(tgtSchema)}.{B(newName)}";
        var where = includeData ? "" : " WHERE 1 = 0";
        await SqlServerService.ExecuteAsync(p.BuildConnectionString(), db,
            $"SELECT * INTO {dst} FROM {src}{where}");
    }

    // ---- SQLite (clone the original CREATE statement) --------------------
    private static async Task CopySqliteAsync(ConnectionProfile p, string srcName, string newName, bool includeData)
    {
        var cs = p.BuildConnectionString();
        var createSql = await SqliteScalarAsync(cs,
            "SELECT sql FROM sqlite_master WHERE type='table' AND name=$n", srcName);
        if (string.IsNullOrWhiteSpace(createSql))
            throw new InvalidOperationException($"Could not read the definition of '{srcName}'.");

        // Replace the table identifier right after CREATE TABLE with the new name.
        var newCreate = Regex.Replace(createSql,
            @"^\s*CREATE\s+TABLE\s+(""[^""]*""|\[[^\]]*\]|`[^`]*`|\w+)",
            "CREATE TABLE " + B(newName),
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        await SqliteService.ExecuteScriptAsync(cs, newCreate);
        if (includeData)
            await SqliteService.ExecuteScriptAsync(cs, $"INSERT INTO {B(newName)} SELECT * FROM {B(srcName)}");
    }

    // ---- Firebird (rebuild CREATE from catalog) -------------------------
    private static async Task CopyFirebirdAsync(ConnectionProfile p, string srcName, string newName, bool includeData)
    {
        var cs = p.BuildConnectionString();
        var cols = await FirebirdService.GetColumnsAsync(cs, srcName);
        if (cols.Count == 0)
            throw new InvalidOperationException($"Could not read the columns of '{srcName}'.");

        var lines = cols.Select(c => $"  {FirebirdService.Quote(c.Name)} {c.TypeName}{(c.Nullable ? "" : " NOT NULL")}").ToList();
        var pk = cols.Where(c => c.IsPrimaryKey).Select(c => FirebirdService.Quote(c.Name)).ToList();
        if (pk.Count > 0) lines.Add($"  PRIMARY KEY ({string.Join(", ", pk)})");
        await FirebirdService.ExecuteAsync(cs,
            $"CREATE TABLE {FirebirdService.Quote(newName)} (\n{string.Join(",\n", lines)}\n)");

        if (includeData)
        {
            var colList = string.Join(", ", cols.Select(c => FirebirdService.Quote(c.Name)));
            await FirebirdService.ExecuteAsync(cs,
                $"INSERT INTO {FirebirdService.Quote(newName)} ({colList}) SELECT {colList} FROM {FirebirdService.Quote(srcName)}");
        }
    }

    // ---- MongoDB --------------------------------------------------------
    private static async Task CopyMongoAsync(ConnectionProfile p, string db, string srcName, string newName, bool includeData)
    {
        var database = new MongoClient(p.BuildConnectionString()).GetDatabase(db);
        if (includeData)
        {
            var src = database.GetCollection<BsonDocument>(srcName);
            await src.AggregateAsync<BsonDocument>(new BsonDocument[] { new("$out", newName) });
            // $out drops/creates the target; if the source is empty it still leaves no collection,
            // so ensure it exists.
            var names = await (await database.ListCollectionNamesAsync()).ToListAsync();
            if (!names.Contains(newName)) await database.CreateCollectionAsync(newName);
        }
        else
        {
            await database.CreateCollectionAsync(newName);
        }
    }

    private static async Task<string?> SqliteScalarAsync(string cs, string sql, string param)
    {
        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$n", param);
        return (await cmd.ExecuteScalarAsync()) as string;
    }
}
