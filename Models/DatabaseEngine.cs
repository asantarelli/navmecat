namespace NavMeCat.Models;

/// <summary>The database engine a connection talks to.</summary>
public enum DatabaseEngine
{
    SqlServer,
    Sqlite,
    PostgreSql,
    MongoDb,
    Firebird,
    MySql,
    MariaDb,
    Tps
}

public static class DatabaseEngineInfo
{
    /// <summary>Short, human-friendly engine name for the UI.</summary>
    public static string DisplayName(this DatabaseEngine e) => e switch
    {
        DatabaseEngine.SqlServer => "SQL Server",
        DatabaseEngine.Sqlite => "SQLite",
        DatabaseEngine.PostgreSql => "PostgreSQL",
        DatabaseEngine.MongoDb => "MongoDB",
        DatabaseEngine.Firebird => "Firebird",
        DatabaseEngine.MySql => "MySQL",
        DatabaseEngine.MariaDb => "MariaDB",
        DatabaseEngine.Tps => "TPS (Clarion)",
        _ => e.ToString()
    };

    /// <summary>MySQL and MariaDB share the same driver and SQL.</summary>
    public static bool IsMySql(this DatabaseEngine e) =>
        e is DatabaseEngine.MySql or DatabaseEngine.MariaDb;

    /// <summary>True for engines that are fully implemented today.</summary>
    public static bool IsSupported(this DatabaseEngine e) =>
        e is DatabaseEngine.SqlServer or DatabaseEngine.Sqlite
            or DatabaseEngine.Firebird or DatabaseEngine.MongoDb
            or DatabaseEngine.MySql or DatabaseEngine.MariaDb
            or DatabaseEngine.Tps;

    /// <summary>Read-only engines: browse and copy out, but no editing, designing or writing back.</summary>
    public static bool IsReadOnly(this DatabaseEngine e) =>
        e is DatabaseEngine.MongoDb or DatabaseEngine.Tps;
}
