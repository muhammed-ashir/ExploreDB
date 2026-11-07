using System;
using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac.Compare;

namespace ExploreDB.Services.SqlCompare.Models;

public enum EndpointType
{
    LiveDatabase,
    DacpacFile,
    ConnectionString
}

/// <summary>
/// Defines a source or target endpoint for database schema comparison.
/// </summary>
public class DatabaseEndpoint
{
    public EndpointType Type { get; set; } = EndpointType.LiveDatabase;

    /// <summary>
    /// Server address or host (e.g. localhost, 127.0.0.1, myserver.database.windows.net)
    /// </summary>
    public string Server { get; set; } = "localhost";

    /// <summary>
    /// Database / Initial Catalog name.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Authentication type: "Windows", "SqlServer", "EntraMfa"
    /// </summary>
    public string AuthType { get; set; } = "Windows";

    /// <summary>
    /// User ID / Email address for SQL Auth or Entra MFA.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password for SQL Auth.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Path to a .dacpac file if Type is DacpacFile.
    /// </summary>
    public string DacpacPath { get; set; } = string.Empty;

    /// <summary>
    /// Custom connection string if Type is ConnectionString.
    /// </summary>
    public string CustomConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Trust server certificate (helpful for local and self-signed certificates).
    /// </summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeout { get; set; } = 30;

    /// <summary>
    /// Builds the standard ADO.NET connection string from current properties.
    /// </summary>
    public string BuildConnectionString()
    {
        if (Type == EndpointType.ConnectionString)
        {
            return CustomConnectionString;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = ConnectionTimeout
        };

        if (!string.IsNullOrWhiteSpace(Database))
        {
            builder.InitialCatalog = Database;
        }

        if (string.Equals(AuthType, "Windows", StringComparison.OrdinalIgnoreCase))
        {
            builder.IntegratedSecurity = true;
        }
        else if (string.Equals(AuthType, "EntraMfa", StringComparison.OrdinalIgnoreCase))
        {
            builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
            if (!string.IsNullOrWhiteSpace(Username))
            {
                builder.UserID = Username;
            }
        }
        else // SqlServer
        {
            builder.UserID = Username;
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Converts this endpoint to the corresponding DacFx SchemaCompareEndpoint.
    /// </summary>
    public SchemaCompareEndpoint ToDacCompareEndpoint()
    {
        if (Type == EndpointType.DacpacFile)
        {
            if (string.IsNullOrWhiteSpace(DacpacPath) || !File.Exists(DacpacPath))
            {
                throw new FileNotFoundException($"The specified .dacpac file was not found: {DacpacPath}");
            }
            return new SchemaCompareDacpacEndpoint(DacpacPath);
        }

        var connString = BuildConnectionString();
        return new SchemaCompareDatabaseEndpoint(connString);
    }

    /// <summary>
    /// Returns a human-friendly display description of this endpoint.
    /// </summary>
    public override string ToString()
    {
        return Type switch
        {
            EndpointType.DacpacFile => $"[Dacpac] {Path.GetFileName(DacpacPath)}",
            EndpointType.ConnectionString => $"[Custom] {Database} on {Server}",
            _ => $"{Database} on {Server} ({AuthType})"
        };
    }
}
