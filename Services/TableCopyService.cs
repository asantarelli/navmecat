using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MongoDB.Bson;
using MongoDB.Driver;
using NavMeCat.Models;

namespace NavMeCat.Services;

/// <summary>Duplicates a table/collection — within one connection, or across two of the same engine.</summary>
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
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$n", param);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    // =====================================================================
    //  Cross-connection copy (two connections of the same engine)
    // =====================================================================

    public static Task CopyCrossAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName,
        ConnectionProfile tgt, string tgtDb, string tgtSchema, string newName, bool includeData)
        => src.Engine == DatabaseEngine.MongoDb
            ? CopyMongoCrossAsync(src, srcDb, srcName, tgt, tgtDb, newName, includeData)
            : CopyRelationalCrossAsync(src, srcDb, srcSchema, srcName, tgt, tgtDb, tgtSchema, newName, includeData);

    private static string Q(DatabaseEngine e, string id) => e == DatabaseEngine.Firebird
        ? "\"" + id.Replace("\"", "\"\"") + "\""
        : "[" + id.Replace("]", "]]") + "]";

    private static string Fq(DatabaseEngine e, string schema, string name) =>
        e == DatabaseEngine.SqlServer ? $"{Q(e, schema)}.{Q(e, name)}" : Q(e, name);

    private static async Task<DbConnection> OpenAsync(ConnectionProfile p, string db)
    {
        DbConnection conn = p.Engine switch
        {
            DatabaseEngine.SqlServer => new SqlConnection(SqlServerService.WithDatabase(p.BuildConnectionString(), db)),
            DatabaseEngine.Sqlite => new SqliteConnection(p.BuildConnectionString()),
            DatabaseEngine.Firebird => new FbConnection(p.BuildConnectionString()),
            _ => throw new NotSupportedException($"{p.Engine.DisplayName()} cross-copy is not supported.")
        };
        await conn.OpenAsync();
        return conn;
    }

    private static async Task CopyRelationalCrossAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName,
        ConnectionProfile tgt, string tgtDb, string tgtSchema, string newName, bool includeData)
    {
        // 1. Create the structure at the target.
        var ddl = await BuildCreateDdlAsync(src, srcDb, srcSchema, srcName, tgtSchema, newName);
        await ExecuteAsync(tgt, tgtDb, ddl);

        if (!includeData) return;

        // 2. Stream rows source → target with one prepared, parameterized insert.
        await using var srcConn = await OpenAsync(src, srcDb);
        await using var readCmd = srcConn.CreateCommand();
        readCmd.CommandText = $"SELECT * FROM {Fq(src.Engine, srcSchema, srcName)}";
        await using var reader = await readCmd.ExecuteReaderAsync();

        var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        if (cols.Count == 0) return;

        await using var tgtConn = await OpenAsync(tgt, tgtDb);
        await using var tx = await tgtConn.BeginTransactionAsync();

        var colList = string.Join(", ", cols.Select(c => Q(tgt.Engine, c)));
        var paramList = string.Join(", ", cols.Select((_, i) => "@p" + i));
        await using var insert = tgtConn.CreateCommand();
        insert.Transaction = (DbTransaction)tx;
        insert.CommandText = $"INSERT INTO {Fq(tgt.Engine, tgtSchema, newName)} ({colList}) VALUES ({paramList})";

        var ps = new DbParameter[cols.Count];
        for (var i = 0; i < cols.Count; i++)
        {
            var p = insert.CreateParameter();
            p.ParameterName = "@p" + i;
            insert.Parameters.Add(p);
            ps[i] = p;
        }

        while (await reader.ReadAsync())
        {
            for (var i = 0; i < cols.Count; i++)
                ps[i].Value = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            await insert.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static async Task<string> BuildCreateDdlAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName, string tgtSchema, string newName)
    {
        var cs = src.BuildConnectionString();
        switch (src.Engine)
        {
            case DatabaseEngine.Sqlite:
            {
                var create = await SqliteScalarAsync(cs,
                    "SELECT sql FROM sqlite_master WHERE type='table' AND name=$n", srcName)
                    ?? throw new InvalidOperationException($"Could not read the definition of '{srcName}'.");
                return Regex.Replace(create,
                    @"^\s*CREATE\s+TABLE\s+(""[^""]*""|\[[^\]]*\]|`[^`]*`|\w+)",
                    "CREATE TABLE " + Q(DatabaseEngine.Sqlite, newName),
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }
            case DatabaseEngine.Firebird:
            {
                var cols = await FirebirdService.GetColumnsAsync(cs, srcName);
                var lines = cols.Select(c => $"  {Q(src.Engine, c.Name)} {c.TypeName}{(c.Nullable ? "" : " NOT NULL")}").ToList();
                var pk = cols.Where(c => c.IsPrimaryKey).Select(c => Q(src.Engine, c.Name)).ToList();
                if (pk.Count > 0) lines.Add($"  PRIMARY KEY ({string.Join(", ", pk)})");
                return $"CREATE TABLE {Q(src.Engine, newName)} (\n{string.Join(",\n", lines)}\n)";
            }
            default: // SQL Server
            {
                var cols = await SqlServerService.GetColumnDetailsAsync(cs, srcDb, srcSchema, srcName);
                var lines = new List<string>();
                foreach (var c in cols)
                {
                    // No IDENTITY on the copy so explicit values can be inserted.
                    var line = $"  {Q(src.Engine, c.Name)} {SqlServerType(c)}{(c.Nullable ? " NULL" : " NOT NULL")}";
                    lines.Add(line);
                }
                var pk = cols.Where(c => c.IsPrimaryKey).Select(c => Q(src.Engine, c.Name)).ToList();
                if (pk.Count > 0)
                    lines.Add($"  CONSTRAINT {Q(src.Engine, "PK_" + newName)} PRIMARY KEY ({string.Join(", ", pk)})");
                return $"CREATE TABLE {Q(src.Engine, tgtSchema)}.{Q(src.Engine, newName)} (\n{string.Join(",\n", lines)}\n)";
            }
        }
    }

    private static string SqlServerType(SqlServerService.ColumnDetail c)
    {
        var t = c.TypeName.ToLowerInvariant();
        return t switch
        {
            "char" or "varchar" or "binary" or "varbinary" => $"{t}({(c.MaxLength == -1 ? "max" : c.MaxLength.ToString())})",
            "nchar" or "nvarchar" => $"{t}({(c.MaxLength == -1 ? "max" : (c.MaxLength / 2).ToString())})",
            "decimal" or "numeric" => $"{t}({c.Precision},{c.Scale})",
            _ => t
        };
    }

    private static async Task ExecuteAsync(ConnectionProfile p, string db, string sql)
    {
        switch (p.Engine)
        {
            case DatabaseEngine.Sqlite: await SqliteService.ExecuteScriptAsync(p.BuildConnectionString(), sql); break;
            case DatabaseEngine.Firebird: await FirebirdService.ExecuteAsync(p.BuildConnectionString(), sql); break;
            default: await SqlServerService.ExecuteAsync(p.BuildConnectionString(), db, sql); break;
        }
    }

    private static async Task CopyMongoCrossAsync(
        ConnectionProfile src, string srcDb, string srcName,
        ConnectionProfile tgt, string tgtDb, string newName, bool includeData)
    {
        var tgtDatabase = new MongoClient(tgt.BuildConnectionString()).GetDatabase(tgtDb);
        if (!includeData)
        {
            await tgtDatabase.CreateCollectionAsync(newName);
            return;
        }

        var source = new MongoClient(src.BuildConnectionString()).GetDatabase(srcDb).GetCollection<BsonDocument>(srcName);
        var target = tgtDatabase.GetCollection<BsonDocument>(newName);

        using var cursor = await source.FindAsync(new BsonDocument());
        var any = false;
        while (await cursor.MoveNextAsync())
        {
            var batch = cursor.Current.ToList();
            if (batch.Count == 0) continue;
            any = true;
            await target.InsertManyAsync(batch);
        }
        if (!any)
        {
            var names = await (await tgtDatabase.ListCollectionNamesAsync()).ToListAsync();
            if (!names.Contains(newName)) await tgtDatabase.CreateCollectionAsync(newName);
        }
    }
}
