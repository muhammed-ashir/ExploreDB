using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ExploreDB.Services;

public class ConnectionService
{
    private const string SettingsFile = "settings.json";
    public string ConnectionString { get; private set; } = string.Empty;
    public List<SavedConnection> SavedConnections { get; private set; } = new();

    public event Action? OnConnectionChanged;
    public event Action<string>? OnDatabaseSwitching;
    public event Action? OnDatabaseSwitched;

    public void NotifyDatabaseSwitching(string dbName) => OnDatabaseSwitching?.Invoke(dbName);
    public void NotifyDatabaseSwitched() => OnDatabaseSwitched?.Invoke();

    public ConnectionService()
    {
        LoadSettings();
    }

    public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(string connString)
    {
        try
        {
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public List<SavedConnection> ActiveSessionConnections { get; } = new();

    public void SaveActiveConnection(string connString)
    {
        ConnectionString = connString;
        
        if (!string.IsNullOrEmpty(connString) && !ActiveSessionConnections.Any(c => c.ConnectionString == connString))
        {
            var saved = SavedConnections.FirstOrDefault(c => c.ConnectionString == connString);
            if (saved != null)
            {
                ActiveSessionConnections.Add(saved);
            }
        }
        
        SaveToDisk();
        OnConnectionChanged?.Invoke();
    }

    public void UpsertConnection(string id, string name, string server, string authType, string username, string password, string database, string connString)
    {
        if (authType == "Windows")
        {
            username = "";
            password = "";
        }

        var existing = SavedConnections.FirstOrDefault(c => c.Id == id);
        if (existing != null)
        {
            existing.Name = name;
            existing.Server = server;
            existing.AuthType = authType;
            existing.Username = username;
            existing.Password = password;
            existing.Database = database;
            existing.ConnectionString = connString;
            existing.LastUsed = DateTime.Now;
        }
        else
        {
            SavedConnections.Add(new SavedConnection
            {
                Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id,
                Name = string.IsNullOrWhiteSpace(name) ? "Unnamed Connection" : name,
                Server = server,
                AuthType = authType,
                Username = username,
                Password = password,
                Database = database,
                ConnectionString = connString,
                LastUsed = DateTime.Now
            });
        }
        SaveToDisk();
    }

    public string BuildConnectionString(string server, string authType, string username, string password, string database = "")
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            TrustServerCertificate = true, // Helpful for local dev
            ConnectTimeout = 60 // Increase connection timeout to 60 seconds
        };

        if (authType == "Windows")
        {
            builder.IntegratedSecurity = true;
        }
        else if (authType == "EntraMfa")
        {
            builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
            if (!string.IsNullOrWhiteSpace(username))
            {
                builder.UserID = username;
            }
        }
        else
        {
            builder.UserID = username;
            builder.Password = password;
        }

        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.InitialCatalog = database;
        }

        return builder.ConnectionString;
    }

    public async Task<List<string>> GetDatabasesAsync(string server, string authType, string username, string password)
    {
        var connString = BuildConnectionString(server, authType, username, password, "master");
        var databases = new List<string>();
        try
        {
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                databases.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching databases: {ex.Message}");
            throw;
        }
        return databases;
    }

    public void DeleteConnection(string id)
    {
        var existing = SavedConnections.FirstOrDefault(c => c.Id == id);
        if (existing != null)
        {
            SavedConnections.Remove(existing);
            SaveToDisk();
        }
    }

    private void SaveToDisk()
    {
        var data = new SettingsModel
        {
            ConnectionString = ConnectionString,
            SavedConnections = SavedConnections
        };
        var json = JsonSerializer.Serialize(data);
        var path = Path.Combine(FileSystem.AppDataDirectory, SettingsFile);
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        File.WriteAllText(path, json);
    }

    private void LoadSettings()
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, SettingsFile);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<SettingsModel>(json);
                if (data != null)
                {
                    // Intentionally start in a disconnected state
                    ConnectionString = ""; 
                    
                    if (data.SavedConnections != null)
                    {
                        SavedConnections = data.SavedConnections;
                    }
                    else if (!string.IsNullOrEmpty(data.ConnectionString))
                    {
                        // Legacy migration
                        SavedConnections.Add(new SavedConnection
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = "Default Connection",
                            ConnectionString = data.ConnectionString
                        });
                        SaveToDisk();
                    }
                }
            }
            catch { }
        }
    }

    public class SavedConnection
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string AuthType { get; set; } = "Windows";
        public bool UseWindowsAuth { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; } = DateTime.Now;
    }

    private class SettingsModel
    {
        public string? ConnectionString { get; set; }
        public List<SavedConnection> SavedConnections { get; set; } = new();
    }
}
