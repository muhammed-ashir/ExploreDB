using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ExploreDB.Services;

public class ConnectionService
{
    private const string SettingsFile = "settings.json";
    public string ConnectionString { get; private set; } = string.Empty;
    public string ActiveServer
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConnectionString)) return string.Empty;
            try
            {
                var builder = new SqlConnectionStringBuilder(ConnectionString);
                return builder.DataSource ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
    public List<SavedConnection> SavedConnections { get; private set; } = new();

    public event Action? OnConnectionChanged;
    public event Action? OnSavedConnectionsChanged;
    public event Action<string>? OnDatabaseSwitching;
    public event Action? OnDatabaseSwitched;

    public void NotifyDatabaseSwitching(string dbName) => OnDatabaseSwitching?.Invoke(dbName);
    public void NotifyDatabaseSwitched() => OnDatabaseSwitched?.Invoke();

    public ConnectionService()
    {
        LoadSettings();
    }

    public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(string connString, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync(cancellationToken);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested || ex is OperationCanceledException)
            {
                return (false, "Connection cancelled.");
            }
            return (false, ex.Message);
        }
    }

    public List<SavedConnection> ActiveSessionConnections { get; } = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _prewarmedConnections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures a connection string uses at least PacketSize=32767 in a deterministic form
    /// so connection pool keys match between PrewarmConnection and query execution.
    /// </summary>
    public static string EnsureOptimalConnectionString(string connString, out string databaseName)
    {
        databaseName = string.Empty;
        if (string.IsNullOrWhiteSpace(connString)) return string.Empty;

        try
        {
            var builder = new SqlConnectionStringBuilder(connString);
            databaseName = builder.InitialCatalog ?? string.Empty;
            if (builder.PacketSize < 32767)
            {
                builder.PacketSize = 32767;
            }
            return builder.ConnectionString;
        }
        catch
        {
            return connString;
        }
    }

    public static string EnsureOptimalConnectionString(string connString)
        => EnsureOptimalConnectionString(connString, out _);

    public static string GetAuthMode(SqlConnectionStringBuilder builder)
    {
        if (builder.IntegratedSecurity) return "Windows";
        if (builder.Authentication == SqlAuthenticationMethod.ActiveDirectoryInteractive ||
            builder.Authentication == SqlAuthenticationMethod.ActiveDirectoryIntegrated ||
            builder.Authentication == SqlAuthenticationMethod.ActiveDirectoryPassword ||
            builder.Authentication == SqlAuthenticationMethod.ActiveDirectoryDefault)
        {
            return "EntraMfa";
        }
        return "SqlServer";
    }

    public static string GetAuthMode(SavedConnection connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.AuthType))
        {
            if (connection.AuthType.Equals("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
            if (connection.AuthType.Equals("EntraMfa", StringComparison.OrdinalIgnoreCase)) return "EntraMfa";
            return "SqlServer";
        }
        return connection.UseWindowsAuth ? "Windows" : "SqlServer";
    }

    public static string GetConnectionIdentityKey(string? connString)
    {
        if (string.IsNullOrWhiteSpace(connString)) return string.Empty;
        try
        {
            var builder = new SqlConnectionStringBuilder(connString);
            string server = (builder.DataSource ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(server)) return string.Empty;
            string authMode = GetAuthMode(builder).ToUpperInvariant();
            string userId = (builder.UserID ?? string.Empty).Trim().ToUpperInvariant();
            return $"{server}|{authMode}|{userId}";
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GetConnectionIdentityKey(SavedConnection connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.Server))
        {
            string server = connection.Server.Trim().ToUpperInvariant();
            string authMode = GetAuthMode(connection).ToUpperInvariant();
            string userId = (connection.Username ?? string.Empty).Trim().ToUpperInvariant();
            return $"{server}|{authMode}|{userId}";
        }
        return GetConnectionIdentityKey(connection.ConnectionString);
    }

    public SavedConnection? ResolveConnectionMetadata(string? connString)
    {
        if (string.IsNullOrWhiteSpace(connString)) return null;

        string targetKey = GetConnectionIdentityKey(connString);

        var active = ActiveSessionConnections.FirstOrDefault(c =>
            string.Equals(c.ConnectionString, connString, StringComparison.OrdinalIgnoreCase));
        if (active != null && !string.IsNullOrWhiteSpace(active.Name) &&
            !string.Equals(active.Name, active.Server, StringComparison.OrdinalIgnoreCase))
        {
            return active;
        }

        var savedExact = SavedConnections.FirstOrDefault(c =>
            string.Equals(c.ConnectionString, connString, StringComparison.OrdinalIgnoreCase));
        if (savedExact != null) return savedExact;

        if (!string.IsNullOrEmpty(targetKey))
        {
            var savedByKey = SavedConnections.FirstOrDefault(c =>
                string.Equals(GetConnectionIdentityKey(c), targetKey, StringComparison.OrdinalIgnoreCase));
            if (savedByKey != null) return savedByKey;

            var activeByKey = ActiveSessionConnections.FirstOrDefault(c =>
                string.Equals(GetConnectionIdentityKey(c), targetKey, StringComparison.OrdinalIgnoreCase));
            if (activeByKey != null) return activeByKey;
        }

        if (active != null) return active;

        try
        {
            var builder = new SqlConnectionStringBuilder(connString);
            string server = builder.DataSource ?? string.Empty;
            string authMode = GetAuthMode(builder);
            string userId = builder.UserID ?? string.Empty;

            var savedByServerUser = SavedConnections.FirstOrDefault(c =>
                string.Equals(c.Server?.Trim(), server.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals((c.Username ?? string.Empty).Trim(), userId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (savedByServerUser != null) return savedByServerUser;

            if (!string.IsNullOrEmpty(server))
            {
                return new SavedConnection
                {
                    Name = string.Empty,
                    Server = server,
                    AuthType = authMode,
                    UseWindowsAuth = authMode == "Windows",
                    Username = userId,
                    Database = builder.InitialCatalog ?? string.Empty,
                    ConnectionString = connString
                };
            }
        }
        catch { }

        return null;
    }

    public void PrewarmConnection(string connString)
    {
        if (string.IsNullOrWhiteSpace(connString)) return;

        var optimalConnStr = EnsureOptimalConnectionString(connString);
        if (string.IsNullOrWhiteSpace(optimalConnStr)) return;

        if (_prewarmedConnections.TryGetValue(optimalConnStr, out var lastWarmed) && (DateTime.UtcNow - lastWarmed).TotalMinutes < 3)
        {
            return;
        }

        _prewarmedConnections[optimalConnStr] = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                using var conn = new SqlConnection(optimalConnStr);
                await conn.OpenAsync();
            }
            catch
            {
                // Silent background pre-warm
            }
        });
    }

    public void SaveActiveConnection(string connString)
    {
        ConnectionString = connString;
        
        if (!string.IsNullOrEmpty(connString))
        {
            var builder = new SqlConnectionStringBuilder(connString);
            string server = builder.DataSource;
            string userId = builder.UserID ?? "";
            
            var existingActive = ActiveSessionConnections.FirstOrDefault(c => 
                string.Equals(c.Server, server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Username ?? "", userId, StringComparison.OrdinalIgnoreCase));
            
            if (existingActive != null)
            {
                existingActive.ConnectionString = connString;
                existingActive.Database = builder.InitialCatalog;
            }
            else
            {
                var saved = SavedConnections.FirstOrDefault(c => string.Equals(c.ConnectionString, connString, StringComparison.OrdinalIgnoreCase));
                if (saved == null)
                {
                    saved = SavedConnections.FirstOrDefault(c => 
                        string.Equals(c.Server, server, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.Username ?? "", userId, StringComparison.OrdinalIgnoreCase));
                }
                
                if (saved != null)
                {
                    ActiveSessionConnections.Add(new SavedConnection
                    {
                        Id = saved.Id,
                        Name = saved.Name,
                        Server = saved.Server,
                        AuthType = saved.AuthType,
                        UseWindowsAuth = saved.UseWindowsAuth,
                        Username = saved.Username,
                        Database = builder.InitialCatalog,
                        ConnectionString = connString,
                        LastUsed = DateTime.Now
                    });
                }
                else
                {
                    ActiveSessionConnections.Add(new SavedConnection
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = server,
                        Server = server,
                        Username = userId,
                        Database = builder.InitialCatalog,
                        ConnectionString = connString,
                        LastUsed = DateTime.Now
                    });
                }
            }
        }
        
        SaveToDisk();
        PrewarmConnection(connString);
        OnConnectionChanged?.Invoke();
    }

    public void DisconnectConnection(string connString)
    {
        if (string.IsNullOrWhiteSpace(connString)) return;

        string targetKey = GetConnectionIdentityKey(connString);
        string server = "";
        string userId = "";
        try
        {
            var builder = new SqlConnectionStringBuilder(connString);
            server = builder.DataSource;
            userId = builder.UserID ?? "";
        }
        catch { }

        var toRemove = ActiveSessionConnections.Where(c =>
            string.Equals(c.ConnectionString, connString, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(targetKey) &&
             string.Equals(GetConnectionIdentityKey(c), targetKey, StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var item in toRemove)
        {
            ActiveSessionConnections.Remove(item);
        }

        try
        {
            using var conn = new SqlConnection(connString);
            SqlConnection.ClearPool(conn);
        }
        catch { }

        bool isCurrent = false;
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            if (string.Equals(ConnectionString, connString, StringComparison.OrdinalIgnoreCase))
            {
                isCurrent = true;
            }
            else if (!string.IsNullOrEmpty(targetKey))
            {
                string currentKey = GetConnectionIdentityKey(ConnectionString);
                if (!string.IsNullOrEmpty(currentKey) &&
                    string.Equals(currentKey, targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    isCurrent = true;
                }
            }
            else if (!string.IsNullOrEmpty(server))
            {
                try
                {
                    var currentBuilder = new SqlConnectionStringBuilder(ConnectionString);
                    if (string.Equals(currentBuilder.DataSource, server, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(currentBuilder.UserID ?? "", userId, StringComparison.OrdinalIgnoreCase))
                    {
                        isCurrent = true;
                    }
                }
                catch { }
            }
        }

        if (isCurrent)
        {
            var next = ActiveSessionConnections.LastOrDefault();
            if (next != null && !string.IsNullOrEmpty(next.ConnectionString))
            {
                SaveActiveConnection(next.ConnectionString);
            }
            else
            {
                ConnectionString = string.Empty;
                SaveToDisk();
                OnConnectionChanged?.Invoke();
            }
        }
        else
        {
            OnConnectionChanged?.Invoke();
        }
    }

    public void DisconnectActiveServer()
    {
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            DisconnectConnection(ConnectionString);
        }
    }

    public void DisconnectAll()
    {
        ActiveSessionConnections.Clear();
        try
        {
            SqlConnection.ClearAllPools();
        }
        catch { }
        ConnectionString = string.Empty;
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
        OnSavedConnectionsChanged?.Invoke();
    }

    public string BuildConnectionString(string server, string authType, string username, string password, string database = "")
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            TrustServerCertificate = true, // Helpful for local dev
            PacketSize = 32767
        };

        if (authType == "Windows")
        {
            builder.IntegratedSecurity = true;
            builder.ConnectTimeout = 15;
        }
        else if (authType == "EntraMfa")
        {
            builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
            builder.ConnectTimeout = 60;
            if (!string.IsNullOrWhiteSpace(username))
            {
                builder.UserID = username;
            }
        }
        else
        {
            builder.UserID = username;
            builder.Password = password;
            builder.ConnectTimeout = 15;
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

    public enum ImportConflictPolicy
    {
        AddAsCopy,
        Overwrite,
        Skip
    }

    public record ImportResult(int TotalInFile, int Added, int Updated, int Skipped, string? ErrorMessage);

    public class ExploreDbExportModel
    {
        public string App { get; set; } = "ExploreDB";
        public string Version { get; set; } = "1.0";
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public bool IncludesPasswords { get; set; }
        public bool PasswordsEncrypted { get; set; } = true;
        public int ConnectionCount { get; set; }
        public List<ExportedConnectionItem> Connections { get; set; } = new();
    }

    public class ExportedConnectionItem
    {
        public string Name { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string AuthType { get; set; } = "Windows";
        public bool UseWindowsAuth { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
    }

    private static readonly byte[] ExportKey = SHA256.HashData(Encoding.UTF8.GetBytes("ExploreDB::SecureConnectionProfileExportKey::2026!#@"));

    public static string EncryptPassword(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        try
        {
            using var aes = Aes.Create();
            aes.Key = ExportKey;
            aes.GenerateIV();
            byte[] iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, iv);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            byte[] combined = new byte[iv.Length + cipherBytes.Length];
            Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
            Buffer.BlockCopy(cipherBytes, 0, combined, iv.Length, cipherBytes.Length);

            return "ENC:v1:" + Convert.ToBase64String(combined);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error encrypting password: {ex.Message}");
            return plainText;
        }
    }

    public static string DecryptPassword(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

        if (!encryptedText.StartsWith("ENC:v1:"))
        {
            return encryptedText;
        }

        try
        {
            string base64 = encryptedText.Substring("ENC:v1:".Length);
            byte[] combined = Convert.FromBase64String(base64);

            if (combined.Length <= 16)
            {
                return string.Empty;
            }

            byte[] iv = new byte[16];
            byte[] cipherBytes = new byte[combined.Length - 16];
            Buffer.BlockCopy(combined, 0, iv, 0, 16);
            Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherBytes.Length);

            using var aes = Aes.Create();
            aes.Key = ExportKey;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error decrypting password: {ex.Message}");
            return string.Empty;
        }
    }

    public string ExportConnections(IEnumerable<string> connectionIds, bool includePasswords)
    {
        var idSet = new HashSet<string>(connectionIds);
        var toExport = SavedConnections.Where(c => idSet.Contains(c.Id)).ToList();

        var exportData = new ExploreDbExportModel
        {
            App = "ExploreDB",
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            IncludesPasswords = includePasswords,
            PasswordsEncrypted = includePasswords,
            ConnectionCount = toExport.Count,
            Connections = toExport.Select(c => new ExportedConnectionItem
            {
                Name = c.Name,
                Server = c.Server,
                AuthType = string.IsNullOrEmpty(c.AuthType) ? (c.UseWindowsAuth ? "Windows" : "SqlServer") : c.AuthType,
                UseWindowsAuth = c.UseWindowsAuth,
                Username = c.Username,
                Password = includePasswords ? EncryptPassword(c.Password) : string.Empty,
                Database = c.Database
            }).ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        return JsonSerializer.Serialize(exportData, options);
    }

    public ImportResult ImportConnections(string jsonContent, ImportConflictPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return new ImportResult(0, 0, 0, 0, "The imported file is empty.");
        }

        List<ExportedConnectionItem> itemsToImport = new();
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        try
        {
            // 1. Try parsing ExploreDbExportModel
            try
            {
                var exportModel = JsonSerializer.Deserialize<ExploreDbExportModel>(jsonContent, jsonOptions);
                if (exportModel?.Connections != null && exportModel.Connections.Count > 0)
                {
                    itemsToImport = exportModel.Connections;
                }
            }
            catch (JsonException) { }

            // 2. Try parsing direct list of ExportedConnectionItem
            if (itemsToImport.Count == 0)
            {
                try
                {
                    var listItems = JsonSerializer.Deserialize<List<ExportedConnectionItem>>(jsonContent, jsonOptions);
                    if (listItems != null && listItems.Count > 0)
                    {
                        itemsToImport = listItems;
                    }
                }
                catch (JsonException) { }
            }

            // 3. Try parsing SettingsModel
            if (itemsToImport.Count == 0)
            {
                try
                {
                    var settingsModel = JsonSerializer.Deserialize<SettingsModel>(jsonContent, jsonOptions);
                    if (settingsModel?.SavedConnections != null && settingsModel.SavedConnections.Count > 0)
                    {
                        itemsToImport = settingsModel.SavedConnections.Select(c => new ExportedConnectionItem
                        {
                            Name = c.Name,
                            Server = c.Server,
                            AuthType = c.AuthType,
                            UseWindowsAuth = c.UseWindowsAuth,
                            Username = c.Username,
                            Password = c.Password,
                            Database = c.Database
                        }).ToList();
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex)
        {
            return new ImportResult(0, 0, 0, 0, $"Failed to read file: {ex.Message}");
        }

        if (itemsToImport.Count == 0)
        {
            return new ImportResult(0, 0, 0, 0, "Invalid or corrupted file format. Please ensure you are importing a valid .exploredb file.");
        }

        int added = 0;
        int updated = 0;
        int skipped = 0;

        foreach (var item in itemsToImport)
        {
            string name = string.IsNullOrWhiteSpace(item.Name) ? (string.IsNullOrWhiteSpace(item.Server) ? "Unnamed Connection" : item.Server) : item.Name.Trim();
            string server = item.Server?.Trim() ?? string.Empty;
            string authType = string.IsNullOrWhiteSpace(item.AuthType) ? (item.UseWindowsAuth ? "Windows" : "SqlServer") : item.AuthType.Trim();
            string username = item.Username?.Trim() ?? string.Empty;
            string password = DecryptPassword(item.Password ?? string.Empty);
            string database = item.Database?.Trim() ?? string.Empty;

            var existing = SavedConnections.FirstOrDefault(c => 
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(c.Server, server, StringComparison.OrdinalIgnoreCase) && 
                 string.Equals(c.Database, database, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                if (policy == ImportConflictPolicy.Skip)
                {
                    skipped++;
                    continue;
                }
                else if (policy == ImportConflictPolicy.Overwrite)
                {
                    existing.Server = server;
                    existing.AuthType = authType;
                    existing.UseWindowsAuth = authType == "Windows";
                    existing.Username = username;
                    if (!string.IsNullOrEmpty(password))
                    {
                        existing.Password = password;
                    }
                    existing.Database = database;
                    existing.ConnectionString = BuildConnectionString(server, authType, username, existing.Password, database);
                    existing.LastUsed = DateTime.Now;
                    updated++;
                    continue;
                }
                else // AddAsCopy
                {
                    string baseName = name;
                    int copyIndex = 1;
                    string candidateName = $"{baseName} (Imported)";
                    while (SavedConnections.Any(c => string.Equals(c.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
                    {
                        copyIndex++;
                        candidateName = $"{baseName} (Imported {copyIndex})";
                    }
                    name = candidateName;
                }
            }

            string connStr = BuildConnectionString(server, authType, username, password, database);
            SavedConnections.Add(new SavedConnection
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Server = server,
                AuthType = authType,
                UseWindowsAuth = authType == "Windows",
                Username = username,
                Password = password,
                Database = database,
                ConnectionString = connStr,
                LastUsed = DateTime.Now
            });
            added++;
        }

        SaveToDisk();
        OnSavedConnectionsChanged?.Invoke();
        OnConnectionChanged?.Invoke();

        return new ImportResult(itemsToImport.Count, added, updated, skipped, null);
    }

    public void DeleteConnection(string id)
    {
        var existing = SavedConnections.FirstOrDefault(c => c.Id == id);
        if (existing != null)
        {
            SavedConnections.Remove(existing);
            SaveToDisk();
            OnSavedConnectionsChanged?.Invoke();
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
        var path = Path.Combine(AppEnvironmentService.AppDataDirectory, SettingsFile);
        Directory.CreateDirectory(AppEnvironmentService.AppDataDirectory);
        File.WriteAllText(path, json);
    }

    private void LoadSettings()
    {
        var path = Path.Combine(AppEnvironmentService.AppDataDirectory, SettingsFile);
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
                        foreach (var c in data.SavedConnections)
                        {
                            if (string.IsNullOrEmpty(c.Id)) c.Id = Guid.NewGuid().ToString();
                        }
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
