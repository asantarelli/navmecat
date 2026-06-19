using System.Data.Common;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using NavMeCat.Models;
using Oracle.ManagedDataAccess.Client;

namespace NavMeCat.Services.SqlCompletion;

/// <summary>
/// Holds table names and their columns for a single connection/database.
/// Loaded with a single bulk query per engine instead of N+1 calls.
/// </summary>
public class SqlSchemaCache
{
    public List<string> Tables { get; } = [];
    public Dictionary<string, List<string>> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsLoaded { get; private set; }
    public string? LoadError { get; private set; }

    public async Task LoadAsync(ConnectionProfile connection, string? database, string? schema)
    {
        try
        {
            if (connection.Engine == DatabaseEngine.Sqlite)
            {
                await LoadSqliteAsync(connection.BuildConnectionString());
                return;
            }

            await using var conn = CreateConnection(connection, database);
            await conn.OpenAsync();

            var (sql, tableParam, schemaParam) = BulkQuery(connection.Engine, database, schema ?? "dbo");

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (tableParam != null)  cmd.Parameters.Add(tableParam);
            if (schemaParam != null) cmd.Parameters.Add(schemaParam);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table  = reader.GetString(0).Trim();
                var column = reader.GetString(1).Trim();

                if (!Columns.ContainsKey(table))
                {
                    Tables.Add(table);
                    Columns[table] = [];
                }
                Columns[table].Add(column);
            }
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoaded = true;
        }
    }

    private async Task LoadSqliteAsync(string cs)
    {
        var tables = await SqliteService.GetTablesAsync(cs);
        Tables.AddRange(tables);
        foreach (var t in tables)
            Columns[t] = await SqliteService.GetColumnNamesAsync(cs, t);
    }

    // Returns (sql, optional table param, optional schema param)
    private static (string Sql, DbParameter? TableParam, DbParameter? SchemaParam)
        BulkQuery(DatabaseEngine engine, string? database, string schema)
    {
        switch (engine)
        {
            case DatabaseEngine.SqlServer:
                var sqlSrv = @"
                    SELECT t.name, c.name
                    FROM sys.tables t
                    JOIN sys.columns c ON c.object_id = t.object_id
                    JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = @schema
                    ORDER BY t.name, c.column_id";
                var sparam = new SqlParameter("@schema", schema);
                return (sqlSrv, null, sparam);

            case DatabaseEngine.MySql:
            case DatabaseEngine.MariaDb:
                var sqlMy = @"
                    SELECT TABLE_NAME, COLUMN_NAME
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @db
                    ORDER BY TABLE_NAME, ORDINAL_POSITION";
                var mparam = new MySqlParameter("@db", database ?? "");
                return (sqlMy, mparam, null);

            case DatabaseEngine.Sqlite:
                // SQLite has no system catalog for columns across all tables in one shot.
                // We'll use the sqlite_master to get table names only; columns handled separately.
                var sqlLite = "SELECT tbl_name, tbl_name FROM sqlite_master WHERE type='table' ORDER BY tbl_name";
                return (sqlLite, null, null);

            case DatabaseEngine.Firebird:
                var sqlFb = @"
                    SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME)
                    FROM RDB$RELATION_FIELDS rf
                    JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
                    WHERE (r.RDB$SYSTEM_FLAG = 0 OR r.RDB$SYSTEM_FLAG IS NULL)
                      AND r.RDB$VIEW_BLR IS NULL
                    ORDER BY rf.RDB$RELATION_NAME, rf.RDB$FIELD_POSITION";
                return (sqlFb, null, null);

            case DatabaseEngine.Oracle:
                var sqlOra = @"
                    SELECT table_name, column_name
                    FROM user_tab_columns
                    ORDER BY table_name, column_id";
                return (sqlOra, null, null);

            default:
                return ("SELECT '' WHERE 1=0", null, null);
        }
    }

    private static DbConnection CreateConnection(ConnectionProfile connection, string? database)
    {
        var cs = connection.BuildConnectionString();
        return connection.Engine switch
        {
            DatabaseEngine.Sqlite   => new SqliteConnection(cs),
            DatabaseEngine.Firebird => new FbConnection(cs),
            DatabaseEngine.MySql or DatabaseEngine.MariaDb =>
                new MySqlConnection(string.IsNullOrEmpty(database)
                    ? cs : MySqlService.WithDatabase(cs, database)),
            DatabaseEngine.Oracle   => new OracleConnection(cs),
            _ => new SqlConnection(string.IsNullOrEmpty(database)
                    ? cs : SqlServerService.WithDatabase(cs, database)),
        };
    }
}
