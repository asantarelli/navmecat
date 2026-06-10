using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MongoDB.Bson;
using MongoDB.Driver;
using MySqlConnector;
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
            DatabaseEngine.MySql or DatabaseEngine.MariaDb => MySqlService.GetTablesAsync(cs, database),
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
            DatabaseEngine.MySql or DatabaseEngine.MariaDb => CopyMySqlAsync(p, srcDatabase, srcName, newName, includeData),
            _ => CopySqlServerAsync(p, tgtDatabase, srcSchema, srcName, tgtSchema, newName, includeData)
        };

    // ---- MySQL / MariaDB (CREATE TABLE … LIKE + INSERT SELECT) ----------
    private static async Task CopyMySqlAsync(ConnectionProfile p, string db, string srcName, string newName, bool includeData)
    {
        var cs = p.BuildConnectionString();
        var src = MySqlService.Quote(srcName);
        var dst = MySqlService.Quote(newName);
        await MySqlService.ExecuteAsync(cs, db, $"CREATE TABLE {dst} LIKE {src}");
        if (includeData)
            await MySqlService.ExecuteAsync(cs, db, $"INSERT INTO {dst} SELECT * FROM {src}");
    }

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

    public static bool IsRelational(DatabaseEngine e) =>
        e is DatabaseEngine.SqlServer or DatabaseEngine.Sqlite or DatabaseEngine.Firebird
          or DatabaseEngine.MySql or DatabaseEngine.MariaDb;

    /// <summary>True if a table can be copied from one engine to the other.</summary>
    public static bool CanCopyBetween(DatabaseEngine a, DatabaseEngine b) =>
        a == b || (IsRelational(a) && IsRelational(b));

    public static Task CopyCrossAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName,
        ConnectionProfile tgt, string tgtDb, string tgtSchema, string newName, bool includeData)
    {
        if (src.Engine == DatabaseEngine.MongoDb || tgt.Engine == DatabaseEngine.MongoDb)
        {
            if (src.Engine == DatabaseEngine.MongoDb && tgt.Engine == DatabaseEngine.MongoDb)
                return CopyMongoCrossAsync(src, srcDb, srcName, tgt, tgtDb, newName, includeData);
            throw new NotSupportedException("Copying between MongoDB and a relational database isn't supported.");
        }
        return CopyRelationalCrossAsync(src, srcDb, srcSchema, srcName, tgt, tgtDb, tgtSchema, newName, includeData);
    }

    private static string Q(DatabaseEngine e, string id) => e == DatabaseEngine.Firebird
        ? "\"" + id.Replace("\"", "\"\"") + "\""
        : e.IsMySql()
            ? "`" + id.Replace("`", "``") + "`"
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
            DatabaseEngine.MySql or DatabaseEngine.MariaDb =>
                new MySqlConnection(string.IsNullOrEmpty(db) ? p.BuildConnectionString() : MySqlService.WithDatabase(p.BuildConnectionString(), db)),
            _ => throw new NotSupportedException($"{p.Engine.DisplayName()} cross-copy is not supported.")
        };
        await conn.OpenAsync();
        return conn;
    }

    private static async Task CopyRelationalCrossAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName,
        ConnectionProfile tgt, string tgtDb, string tgtSchema, string newName, bool includeData)
    {
        // 1. Create the structure at the target (clone when same engine; map types when different).
        var ddl = src.Engine == tgt.Engine
            ? await BuildCreateDdlAsync(src, srcDb, srcSchema, srcName, tgtSchema, newName)
            : await BuildCrossEngineDdlAsync(src, srcDb, srcSchema, srcName, tgt, tgtSchema, newName);
        await ExecuteAsync(tgt, tgtDb, ddl);

        if (!includeData) return;

        // 2. Move the rows. SQL Server uses fast streaming bulk copy; others a prepared INSERT.
        if (tgt.Engine == DatabaseEngine.SqlServer)
            await BulkCopySqlServerAsync(src, srcDb, srcSchema, srcName, tgt, tgtDb, tgtSchema, newName);
        else
            await GenericPumpAsync(src, srcDb, srcSchema, srcName, tgt, tgtDb, tgtSchema, newName);
    }

    /// <summary>Streams source rows into a SQL Server target with SqlBulkCopy (fast, set-based).</summary>
    private static async Task BulkCopySqlServerAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName,
        ConnectionProfile tgt, string tgtDb, string tgtSchema, string newName)
    {
        await using var srcConn = await OpenAsync(src, srcDb);
        await using var readCmd = srcConn.CreateCommand();
        readCmd.CommandText = $"SELECT * FROM {Fq(src.Engine, srcSchema, srcName)}";
        readCmd.CommandTimeout = 0;
        await using var reader = await readCmd.ExecuteReaderAsync();

        await using var tgtConn = (SqlConnection)await OpenAsync(tgt, tgtDb);
        using var bulk = new SqlBulkCopy(tgtConn)
        {
            DestinationTableName = $"{Q(DatabaseEngine.SqlServer, tgtSchema)}.{Q(DatabaseEngine.SqlServer, newName)}",
            BulkCopyTimeout = 0,
            BatchSize = 10_000,
            EnableStreaming = true
        };
        for (var i = 0; i < reader.FieldCount; i++)
            bulk.ColumnMappings.Add(reader.GetName(i), reader.GetName(i));
        await bulk.WriteToServerAsync(reader);
    }

    /// <summary>Streams source rows into a non-SQL-Server target via one prepared, parameterized INSERT.</summary>
    private static async Task GenericPumpAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName,
        ConnectionProfile tgt, string tgtDb, string tgtSchema, string newName)
    {
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
            case DatabaseEngine.MySql or DatabaseEngine.MariaDb:
            {
                var cols = await MySqlService.GetColumnsAsync(cs, srcDb, srcName);
                if (cols.Count == 0)
                    throw new InvalidOperationException($"Could not read the columns of '{srcName}'.");
                // Clone column types verbatim; drop AUTO_INCREMENT so explicit values can be inserted.
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

    // ---- cross-engine structure (map source column CLR types to target) --

    private sealed record CrossColumn(string Name, Type Type, int Size, int Precision, int Scale, bool Nullable);

    private static async Task<string> BuildCrossEngineDdlAsync(
        ConnectionProfile src, string srcDb, string srcSchema, string srcName,
        ConnectionProfile tgt, string tgtSchema, string newName)
    {
        var cols = await DescribeAsync(src, srcDb, srcSchema, srcName);
        if (cols.Count == 0)
            throw new InvalidOperationException($"Could not read the columns of '{srcName}'.");

        var pk = new HashSet<string>(
            await GetPrimaryKeyNamesAsync(src, srcDb, srcSchema, srcName), StringComparer.OrdinalIgnoreCase);

        var lines = cols.Select(c =>
        {
            var notNull = !c.Nullable || pk.Contains(c.Name);
            return $"  {Q(tgt.Engine, c.Name)} {MapType(tgt.Engine, c)}{(notNull ? " NOT NULL" : "")}";
        }).ToList();

        var pkCols = cols.Where(c => pk.Contains(c.Name)).Select(c => Q(tgt.Engine, c.Name)).ToList();
        if (pkCols.Count > 0)
            lines.Add(tgt.Engine == DatabaseEngine.SqlServer
                ? $"  CONSTRAINT {Q(tgt.Engine, "PK_" + newName)} PRIMARY KEY ({string.Join(", ", pkCols)})"
                : $"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        var fq = Fq(tgt.Engine, tgtSchema, newName);
        return $"CREATE TABLE {fq} (\n{string.Join(",\n", lines)}\n)";
    }

    private static async Task<List<CrossColumn>> DescribeAsync(
        ConnectionProfile p, string db, string schema, string name)
    {
        await using var conn = await OpenAsync(p, db);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Fq(p.Engine, schema, name)}";
        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SchemaOnly);
        var schemaTable = await reader.GetSchemaTableAsync();

        var result = new List<CrossColumn>();
        if (schemaTable is null) return result;

        int? AsInt(System.Data.DataRow r, string col)
            => schemaTable.Columns.Contains(col) && r[col] is not (null or DBNull) ? Convert.ToInt32(r[col]) : null;
        bool AsBool(System.Data.DataRow r, string col, bool dflt)
            => schemaTable.Columns.Contains(col) && r[col] is bool b ? b : dflt;

        foreach (System.Data.DataRow r in schemaTable.Rows)
        {
            var cname = r["ColumnName"]?.ToString() ?? "";
            var ctype = r["DataType"] as Type ?? typeof(string);
            result.Add(new CrossColumn(cname, ctype,
                AsInt(r, "ColumnSize") ?? 0, AsInt(r, "NumericPrecision") ?? 0,
                AsInt(r, "NumericScale") ?? 0, AsBool(r, "AllowDBNull", true)));
        }
        return result;
    }

    private static Task<List<string>> GetPrimaryKeyNamesAsync(
        ConnectionProfile p, string db, string schema, string name)
    {
        var cs = p.BuildConnectionString();
        return p.Engine switch
        {
            DatabaseEngine.SqlServer => SqlServerService.GetPrimaryKeyAsync(cs, db, schema, name)
                .ContinueWith(t => t.Result.Columns),
            DatabaseEngine.Firebird => FirebirdService.GetPrimaryKeyAsync(cs, name),
            DatabaseEngine.Sqlite => SqliteService.GetColumnDetailsAsync(cs, name)
                .ContinueWith(t => t.Result.Where(c => c.Pk > 0).OrderBy(c => c.Pk).Select(c => c.Name).ToList()),
            DatabaseEngine.MySql or DatabaseEngine.MariaDb => MySqlService.GetPrimaryKeyAsync(cs, db, name),
            _ => Task.FromResult(new List<string>())
        };
    }

    private static bool IsLarge(int size, int threshold) => size <= 0 || size > threshold || size == int.MaxValue;

    private static string MapType(DatabaseEngine target, CrossColumn c)
    {
        var t = Nullable.GetUnderlyingType(c.Type) ?? c.Type;
        var tn = t.Name;

        return target switch
        {
            DatabaseEngine.Sqlite => tn switch
            {
                "Int16" or "Int32" or "Int64" or "Byte" or "SByte" or "Boolean" => "INTEGER",
                "Decimal" => "NUMERIC",
                "Double" or "Single" => "REAL",
                "Byte[]" => "BLOB",
                _ => "TEXT"
            },
            DatabaseEngine.Firebird => tn switch
            {
                "Int64" => "BIGINT",
                "Int32" => "INTEGER",
                "Int16" or "Byte" or "SByte" => "SMALLINT",
                "Boolean" => "BOOLEAN",
                "Decimal" => $"DECIMAL({ClampPrec(c.Precision, 18)},{ClampScale(c.Scale, c.Precision, 18)})",
                "Double" => "DOUBLE PRECISION",
                "Single" => "FLOAT",
                "Guid" => "CHAR(38)",
                "DateTime" or "DateTimeOffset" => "TIMESTAMP",
                "DateOnly" => "DATE",
                "TimeSpan" or "TimeOnly" => "TIME",
                "Byte[]" => "BLOB",
                "String" or "Char" => IsLarge(c.Size, 8191) ? "BLOB SUB_TYPE TEXT" : $"VARCHAR({c.Size})",
                _ => "BLOB SUB_TYPE TEXT"
            },
            DatabaseEngine.MySql or DatabaseEngine.MariaDb => tn switch
            {
                "Int64" => "BIGINT",
                "Int32" => "INT",
                "Int16" => "SMALLINT",
                "Byte" or "SByte" => "TINYINT",
                "Boolean" => "TINYINT(1)",
                "Decimal" => $"DECIMAL({ClampPrec(c.Precision, 65)},{ClampScale(c.Scale, c.Precision, 65)})",
                "Double" => "DOUBLE",
                "Single" => "FLOAT",
                "Guid" => "CHAR(36)",
                "DateTime" or "DateTimeOffset" => "DATETIME",
                "DateOnly" => "DATE",
                "TimeSpan" or "TimeOnly" => "TIME",
                "Byte[]" => "LONGBLOB",
                "String" or "Char" => IsLarge(c.Size, 4000) ? "LONGTEXT" : $"VARCHAR({c.Size})",
                _ => "LONGTEXT"
            },
            _ => tn switch // SQL Server
            {
                "Int64" => "bigint",
                "Int32" => "int",
                "Int16" => "smallint",
                "Byte" or "SByte" => "tinyint",
                "Boolean" => "bit",
                "Decimal" => $"decimal({ClampPrec(c.Precision, 38)},{ClampScale(c.Scale, c.Precision, 38)})",
                "Double" => "float",
                "Single" => "real",
                "Guid" => "uniqueidentifier",
                "DateTime" => "datetime2",
                "DateTimeOffset" => "datetimeoffset",
                "DateOnly" => "date",
                "TimeSpan" or "TimeOnly" => "time",
                "Byte[]" => "varbinary(max)",
                "String" or "Char" => IsLarge(c.Size, 4000) ? "nvarchar(max)" : $"nvarchar({c.Size})",
                _ => "nvarchar(max)"
            }
        };
    }

    private static int ClampPrec(int prec, int max) => prec is > 0 and <= 100 ? Math.Min(prec, max) : max == 38 ? 38 : 18;
    private static int ClampScale(int scale, int prec, int maxPrec)
    {
        var p = ClampPrec(prec, maxPrec);
        if (scale < 0) scale = maxPrec == 38 ? 6 : 4;
        return Math.Min(scale, p);
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
            case DatabaseEngine.MySql or DatabaseEngine.MariaDb: await MySqlService.ExecuteAsync(p.BuildConnectionString(), db, sql); break;
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
