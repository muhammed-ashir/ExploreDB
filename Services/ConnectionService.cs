using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ExploreDB.Services;

public class ConnectionService
{
    private const string SettingsFile = "settings.json";
    public string ConnectionString { get; private set; } = string.Empty;
    public List<SavedConnection> SavedConnections { get; private set; } = new();

    public event Action? OnConnectionChanged;

    public ConnectionService()
    {
        LoadSettings();
    }

    public async Task<bool> TestConnectionAsync(string connString)
    {
        try
        {
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    public void SaveActiveConnection(string connString)
    {
        ConnectionString = connString;
        SaveToDisk();
        OnConnectionChanged?.Invoke();
    }

    public void UpsertConnection(string id, string name, string connString)
    {
        var existing = SavedConnections.FirstOrDefault(c => c.Id == id);
        if (existing != null)
        {
            existing.Name = name;
            existing.ConnectionString = connString;
            existing.LastUsed = DateTime.Now;
        }
        else
        {
            SavedConnections.Add(new SavedConnection
            {
                Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id,
                Name = string.IsNullOrWhiteSpace(name) ? "Unnamed Connection" : name,
                ConnectionString = connString,
                LastUsed = DateTime.Now
            });
        }
        SaveToDisk();
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
                    ConnectionString = data.ConnectionString ?? "";
                    if (data.SavedConnections != null)
                    {
                        SavedConnections = data.SavedConnections;
                    }
                    else if (!string.IsNullOrEmpty(ConnectionString))
                    {
                        // Legacy migration
                        SavedConnections.Add(new SavedConnection
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = "Default Connection",
                            ConnectionString = ConnectionString
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
        public string ConnectionString { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; } = DateTime.Now;
    }

    private class SettingsModel
    {
        public string? ConnectionString { get; set; }
        public List<SavedConnection> SavedConnections { get; set; } = new();
    }
}
