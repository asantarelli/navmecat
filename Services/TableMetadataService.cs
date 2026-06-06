using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;

namespace NavMeCat.Services;

public record TableStructure(string Ddl, string Info, string Relationships);

/// <summary>Builds a CREATE TABLE script, summary info, and foreign-key relationships for a table.</summary>
public static class TableMetadataService
{
    private static string Quote(string id) => "[" + id.Replace("]", "]]") + "]";

    public static async Task<TableStructure> GetAsync(string connectionString, string database, string schema, string table)
    {
        await using var conn = new SqlConnection(SqlServerService.WithDatabase(connectionString, database));
        await conn.OpenAsync();
        var fq = $"{Quote(schema)}.{Quote(table)}";

        var columns = await GetColumnsAsync(conn, fq);
        var identity = await GetIdentityAsync(conn, fq);
        var indexes = await GetIndexesAsync(conn, fq);
        var fks = await GetForeignKeysAsync(conn, fq);

        var ddl = BuildDdl(schema, table, columns, identity, indexes, fks);
        var info = await BuildInfoAsync(conn, fq, schema, table, columns, indexes);
        var relationships = BuildRelationships(schema, table, fks);

        return new TableStructure(ddl, info, relationships);
    }

    // ---- queries ---------------------------------------------------------

    private sealed record ColumnDef(string Name, string TypeName, int MaxLength, byte Precision, byte Scale,
        bool IsNullable, bool IsComputed, string? Collation, string? Default, string? ComputedDefinition);

    private static async Task<List<ColumnDef>> GetColumnsAsync(SqlConnection conn, string fq)
    {
        const string sql = @"
            SELECT c.name, t.name AS type_name, c.max_length, c.precision, c.scale, c.is_nullable,
                   c.is_computed, c.collation_name, dc.definition AS default_def, cc.definition AS computed_def
            FROM sys.columns c
            JOIN sys.types t ON c.user_type_id = t.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(@fq)
            ORDER BY c.column_id";

        var list = new List<ColumnDef>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fq", fq);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new ColumnDef(
                r.GetString(0), r.GetString(1), r.GetInt16(2), r.GetByte(3), r.GetByte(4),
                r.GetBoolean(5), r.GetBoolean(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9)));
        }
        return list;
    }

    private static async Task<Dictionary<string, (long Seed, long Incr)>> GetIdentityAsync(SqlConnection conn, string fq)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        const string sql = "SELECT name, CONVERT(bigint, seed_value), CONVERT(bigint, increment_value) " +
                           "FROM sys.identity_columns WHERE object_id = OBJECT_ID(@fq)";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fq", fq);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result[r.GetString(0)] = (r.IsDBNull(1) ? 1 : r.GetInt64(1), r.IsDBNull(2) ? 1 : r.GetInt64(2));
        return result;
    }

    private sealed record IndexDef(string Name, bool IsUnique, bool IsPrimaryKey, bool IsUniqueConstraint,
        string TypeDesc, List<(string Col, bool Desc)> Columns);

    private static async Task<List<IndexDef>> GetIndexesAsync(SqlConnection conn, string fq)
    {
        const string sql = @"
            SELECT i.name, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.type_desc,
                   col.name AS col, ic.is_descending_key, ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns col ON col.object_id = i.object_id AND col.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@fq) AND i.type > 0
            ORDER BY i.index_id, ic.key_ordinal";

        var map = new Dictionary<string, IndexDef>();
        var order = new List<string>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fq", fq);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var name = r.IsDBNull(0) ? "" : r.GetString(0);
            if (!map.TryGetValue(name, out var def))
            {
                def = new IndexDef(name, r.GetBoolean(1), r.GetBoolean(2), r.GetBoolean(3), r.GetString(4), new());
                map[name] = def;
                order.Add(name);
            }
            def.Columns.Add((r.GetString(5), r.GetBoolean(6)));
        }
        return order.Select(n => map[n]).ToList();
    }

    private sealed record FkDef(string Name, string ParentSchema, string ParentTable, string RefSchema, string RefTable,
        List<(string ParentCol, string RefCol)> Columns);

    private static async Task<List<FkDef>> GetForeignKeysAsync(SqlConnection conn, string fq)
    {
        const string sql = @"
            SELECT fk.name,
                   OBJECT_SCHEMA_NAME(fk.parent_object_id), OBJECT_NAME(fk.parent_object_id),
                   OBJECT_SCHEMA_NAME(fk.referenced_object_id), OBJECT_NAME(fk.referenced_object_id),
                   pc.name, rc.name
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = OBJECT_ID(@fq) OR fk.referenced_object_id = OBJECT_ID(@fq)
            ORDER BY fk.name, fkc.constraint_column_id";

        var map = new Dictionary<string, FkDef>();
        var order = new List<string>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fq", fq);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var name = r.GetString(0);
            if (!map.TryGetValue(name, out var def))
            {
                def = new FkDef(name, r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), new());
                map[name] = def;
                order.Add(name);
            }
            def.Columns.Add((r.GetString(5), r.GetString(6)));
        }
        return order.Select(n => map[n]).ToList();
    }

    // ---- builders --------------------------------------------------------

    private static string FormatType(ColumnDef c)
    {
        var t = c.TypeName.ToLowerInvariant();
        switch (t)
        {
            case "varchar": case "char": case "varbinary": case "binary":
                return $"{c.TypeName}({(c.MaxLength == -1 ? "max" : c.MaxLength.ToString())})";
            case "nvarchar": case "nchar":
                return $"{c.TypeName}({(c.MaxLength == -1 ? "max" : (c.MaxLength / 2).ToString())})";
            case "decimal": case "numeric":
                return $"{c.TypeName}({c.Precision},{c.Scale})";
            case "datetime2": case "time": case "datetimeoffset":
                return c.Scale == 7 ? c.TypeName : $"{c.TypeName}({c.Scale})";
            case "float":
                return c.Precision == 53 ? "float" : $"float({c.Precision})";
            default:
                return c.TypeName;
        }
    }

    private static string BuildDdl(string schema, string table, List<ColumnDef> columns,
        Dictionary<string, (long Seed, long Incr)> identity, List<IndexDef> indexes, List<FkDef> fks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {Quote(schema)}.{Quote(table)} (");

        var lines = new List<string>();
        foreach (var c in columns)
        {
            if (c.IsComputed)
            {
                lines.Add($"  {Quote(c.Name)} AS {c.ComputedDefinition}");
                continue;
            }

            var parts = new StringBuilder($"  {Quote(c.Name)} {FormatType(c)}");
            if (c.Collation is not null) parts.Append($" COLLATE {c.Collation}");
            if (identity.TryGetValue(c.Name, out var id)) parts.Append($" IDENTITY({id.Seed},{id.Incr})");
            parts.Append(c.IsNullable ? " NULL" : " NOT NULL");
            if (c.Default is not null) parts.Append($" DEFAULT {c.Default}");
            lines.Add(parts.ToString());
        }

        var pk = indexes.FirstOrDefault(i => i.IsPrimaryKey);
        if (pk is not null)
        {
            var cols = string.Join(", ", pk.Columns.Select(c => Quote(c.Col) + (c.Desc ? " DESC" : "")));
            var clustered = pk.TypeDesc.Contains("CLUSTERED") && !pk.TypeDesc.Contains("NONCLUSTERED")
                ? "CLUSTERED" : "NONCLUSTERED";
            lines.Add($"  CONSTRAINT {Quote(pk.Name)} PRIMARY KEY {clustered} ({cols})");
        }

        sb.AppendLine(string.Join(",\n", lines));
        sb.AppendLine(")");
        sb.AppendLine("GO");

        // Secondary indexes
        foreach (var ix in indexes.Where(i => !i.IsPrimaryKey && !i.IsUniqueConstraint))
        {
            var cols = string.Join(", ", ix.Columns.Select(c => Quote(c.Col) + (c.Desc ? " DESC" : "")));
            var unique = ix.IsUnique ? "UNIQUE " : "";
            sb.AppendLine();
            sb.AppendLine($"CREATE {unique}INDEX {Quote(ix.Name)} ON {Quote(schema)}.{Quote(table)} ({cols})");
            sb.AppendLine("GO");
        }

        // Outgoing foreign keys
        foreach (var fk in fks.Where(f => f.ParentSchema == schema && f.ParentTable == table))
        {
            var pcols = string.Join(", ", fk.Columns.Select(c => Quote(c.ParentCol)));
            var rcols = string.Join(", ", fk.Columns.Select(c => Quote(c.RefCol)));
            sb.AppendLine();
            sb.AppendLine($"ALTER TABLE {Quote(schema)}.{Quote(table)} ADD CONSTRAINT {Quote(fk.Name)}");
            sb.AppendLine($"  FOREIGN KEY ({pcols}) REFERENCES {Quote(fk.RefSchema)}.{Quote(fk.RefTable)} ({rcols})");
            sb.AppendLine("GO");
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task<string> BuildInfoAsync(SqlConnection conn, string fq, string schema, string table,
        List<ColumnDef> columns, List<IndexDef> indexes)
    {
        long rows = -1;
        DateTime? created = null, modified = null;
        try
        {
            const string sql = @"
                SELECT
                    (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(@fq) AND p.index_id IN (0,1)),
                    t.create_date, t.modify_date
                FROM sys.tables t WHERE t.object_id = OBJECT_ID(@fq)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@fq", fq);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                rows = r.IsDBNull(0) ? -1 : r.GetInt64(0);
                created = r.IsDBNull(1) ? null : r.GetDateTime(1);
                modified = r.IsDBNull(2) ? null : r.GetDateTime(2);
            }
        }
        catch { /* info is best-effort */ }

        var loc = LocalizationManager.Instance;
        var pk = indexes.FirstOrDefault(i => i.IsPrimaryKey);
        var sb = new StringBuilder();
        sb.AppendLine($"{loc["Info_Table"],-16}{schema}.{table}");
        sb.AppendLine($"{loc["Info_Columns"],-16}{columns.Count}");
        sb.AppendLine($"{loc["Info_PrimaryKey"],-16}{(pk is null ? loc["Info_None"] : string.Join(", ", pk.Columns.Select(c => c.Col)))}");
        sb.AppendLine($"{loc["Info_Indexes"],-16}{indexes.Count}");
        if (rows >= 0) sb.AppendLine($"{loc["Info_Rows"],-16}{rows:N0}");
        if (created is not null) sb.AppendLine($"{loc["Info_Created"],-16}{created:yyyy-MM-dd HH:mm}");
        if (modified is not null) sb.AppendLine($"{loc["Info_Modified"],-16}{modified:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine(loc["Info_Columns"]);
        foreach (var c in columns)
            sb.AppendLine($"  • {c.Name}  {FormatType(c)}  {(c.IsNullable ? "NULL" : "NOT NULL")}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildRelationships(string schema, string table, List<FkDef> fks)
    {
        var loc = LocalizationManager.Instance;
        if (fks.Count == 0) return loc["Rel_None"];

        var sb = new StringBuilder();
        var outgoing = fks.Where(f => f.ParentSchema == schema && f.ParentTable == table).ToList();
        var incoming = fks.Where(f => f.RefSchema == schema && f.RefTable == table).ToList();

        if (outgoing.Count > 0)
        {
            sb.AppendLine(loc["Rel_References"]);
            foreach (var fk in outgoing)
            {
                var pc = string.Join(", ", fk.Columns.Select(c => c.ParentCol));
                var rc = string.Join(", ", fk.Columns.Select(c => c.RefCol));
                sb.AppendLine($"  {fk.Name}: ({pc}) → {fk.RefSchema}.{fk.RefTable} ({rc})");
            }
            sb.AppendLine();
        }
        if (incoming.Count > 0)
        {
            sb.AppendLine(loc["Rel_ReferencedBy"]);
            foreach (var fk in incoming)
            {
                var pc = string.Join(", ", fk.Columns.Select(c => c.ParentCol));
                var rc = string.Join(", ", fk.Columns.Select(c => c.RefCol));
                sb.AppendLine($"  {fk.Name}: {fk.ParentSchema}.{fk.ParentTable} ({pc}) → ({rc})");
            }
        }
        return sb.ToString().TrimEnd();
    }
}
