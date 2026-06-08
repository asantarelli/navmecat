using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace NavMeCat.Models;

/// <summary>
/// A saved database connection. Either built from individual fields,
/// or supplied as a raw connection string. Supports multiple engines.
/// </summary>
public class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Connection";

    /// <summary>Which database engine this connection targets.</summary>
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.SqlServer;

    public string Server { get; set; } = "";
    public string? Database { get; set; }

    /// <summary>SQLite database file path, or Firebird database path/alias.</summary>
    public string? FilePath { get; set; }

    /// <summary>TCP port (Firebird; 0 = engine default 3050).</summary>
    public int Port { get; set; }

    /// <summary>True = Windows Authentication, False = SQL Server login.</summary>
    public bool IntegratedSecurity { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;

    public bool UseRawConnectionString { get; set; }
    public string? RawConnectionString { get; set; }

    /// <summary>Builds the effective connection string for this connection's engine.</summary>
    public string BuildConnectionString()
    {
        if (UseRawConnectionString && !string.IsNullOrWhiteSpace(RawConnectionString))
            return RawConnectionString!;

        switch (Engine)
        {
            case DatabaseEngine.Sqlite:
                return new SqliteConnectionStringBuilder { DataSource = FilePath ?? "" }.ToString();
            case DatabaseEngine.Firebird:
                var fb = new FbConnectionStringBuilder
                {
                    DataSource = string.IsNullOrWhiteSpace(Server) ? "localhost" : Server,
                    Port = Port > 0 ? Port : 3050,
                    Database = FilePath ?? "",
                    UserID = string.IsNullOrWhiteSpace(Username) ? "SYSDBA" : Username,
                    Password = Password ?? "",
                    Charset = "UTF8",
                    Dialect = 3,
                    ServerType = FbServerType.Default,
                    Pooling = false
                };
                return fb.ToString();
            case DatabaseEngine.SqlServer:
                break; // built below
            default:
                throw new NotSupportedException($"{Engine.DisplayName()} connections are not supported yet.");
        }

        var b = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = 15,
            ApplicationName = "NavMeCat"
        };

        if (!string.IsNullOrWhiteSpace(Database))
            b.InitialCatalog = Database;

        if (IntegratedSecurity)
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.UserID = Username ?? "";
            b.Password = Password ?? "";
        }

        return b.ConnectionString;
    }

    public ConnectionProfile Clone() => (ConnectionProfile)MemberwiseClone();
}
