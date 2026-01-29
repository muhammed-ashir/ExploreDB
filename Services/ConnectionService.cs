using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace DbExplore.Services;

public class ConnectionService
{
    private const string SettingsFile = "settings.json";
    public string ConnectionString { get; private set; } = string.Empty;

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

    public void SaveConnection(string connString)
    {
        ConnectionString = connString;
        var data = new { ConnectionString = connString };
        var json = JsonSerializer.Serialize(data);
        // Save to AppData so it persists
        var path = Path.Combine(FileSystem.AppDataDirectory, SettingsFile);
        
        // Ensure directory exists
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        
        File.WriteAllText(path, json);
        OnConnectionChanged?.Invoke();
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
                if (!string.IsNullOrEmpty(data?.ConnectionString))
                {
                    ConnectionString = data.ConnectionString;
                }
            }
            catch { }
        }
    }

    private class SettingsModel
    {
        public string? ConnectionString { get; set; }
    }
}
