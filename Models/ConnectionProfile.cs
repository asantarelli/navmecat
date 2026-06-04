using Microsoft.Data.SqlClient;

namespace NavMeCat.Models;

/// <summary>
/// A saved SQL Server connection. Either built from individual fields,
/// or supplied as a raw connection string.
/// </summary>
public class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Connection";

    public string Server { get; set; } = "";
    public string? Database { get; set; }

    /// <summary>True = Windows Authentication, False = SQL Server login.</summary>
    public bool IntegratedSecurity { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;

    public bool UseRawConnectionString { get; set; }
    public string? RawConnectionString { get; set; }

    /// <summary>Builds the effective connection string used to talk to SQL Server.</summary>
    public string BuildConnectionString()
    {
        if (UseRawConnectionString && !string.IsNullOrWhiteSpace(RawConnectionString))
            return RawConnectionString!;

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
