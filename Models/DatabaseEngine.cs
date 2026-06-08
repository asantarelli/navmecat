namespace NavMeCat.Models;

/// <summary>The database engine a connection talks to.</summary>
public enum DatabaseEngine
{
    SqlServer,
    Sqlite,
    PostgreSql,
    MongoDb
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
        _ => e.ToString()
    };

    /// <summary>True for engines that are fully implemented today.</summary>
    public static bool IsSupported(this DatabaseEngine e) =>
        e is DatabaseEngine.SqlServer or DatabaseEngine.Sqlite;
}
