using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExploreDB.Services
{
    public class TabHistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public Guid TabId { get; set; } = Guid.Empty;
        public string TabName { get; set; } = string.Empty;
        public string ScriptText { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
        public string ServerName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public string ConnectionName { get; set; } = string.Empty;
    }

    public class HistoryService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<HistoryService> _logger;
        private readonly string _historyFilePath;
        private List<TabHistoryItem>? _historyCache;

        public HistoryService(IConfiguration config, ILogger<HistoryService> logger)
        {
            _config = config;
            _logger = logger;
            _historyFilePath = Path.Combine(AppEnvironmentService.AppDataDirectory, "query_history.json");
        }

        public async Task<List<TabHistoryItem>> GetHistoryAsync()
        {
            if (_historyCache != null)
                return _historyCache;

            if (!File.Exists(_historyFilePath))
            {
                _historyCache = new List<TabHistoryItem>();
                return _historyCache;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_historyFilePath);
                var raw = JsonSerializer.Deserialize<List<TabHistoryItem>>(json) ?? new List<TabHistoryItem>();

                // Purge legacy flat query records: only keep real tab history sessions
                _historyCache = raw
                    .Where(x => x.TabId != Guid.Empty && !string.IsNullOrWhiteSpace(x.TabName) && !string.IsNullOrWhiteSpace(x.ScriptText) && !x.TabName.StartsWith("Query_"))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tab history");
                _historyCache = new List<TabHistoryItem>();
            }

            return _historyCache;
        }

        public async Task SaveTabHistoryAsync(Guid tabId, string tabName, string fullScriptText, string serverName = "", string dbName = "", string connectionString = "", string connectionName = "")
        {
            if (string.IsNullOrWhiteSpace(fullScriptText) || tabId == Guid.Empty) return;

            var history = await GetHistoryAsync();

            var existing = history.FirstOrDefault(h => h.TabId == tabId);
            if (existing != null)
            {
                existing.TabName = !string.IsNullOrWhiteSpace(tabName) ? tabName : existing.TabName;
                existing.ScriptText = fullScriptText;
                existing.ExecutedAt = DateTime.Now;
                if (!string.IsNullOrEmpty(serverName)) existing.ServerName = serverName;
                if (!string.IsNullOrEmpty(dbName)) existing.DatabaseName = dbName;
                if (!string.IsNullOrEmpty(connectionString)) existing.ConnectionString = connectionString;
                if (!string.IsNullOrEmpty(connectionName)) existing.ConnectionName = connectionName;

                history.Remove(existing);
                history.Insert(0, existing);
            }
            else
            {
                var item = new TabHistoryItem
                {
                    TabId = tabId,
                    TabName = !string.IsNullOrWhiteSpace(tabName) ? tabName : "SQLQuery.sql",
                    ScriptText = fullScriptText,
                    ExecutedAt = DateTime.Now,
                    ServerName = serverName,
                    DatabaseName = dbName,
                    ConnectionString = connectionString,
                    ConnectionName = connectionName
                };
                history.Insert(0, item);
            }

            var limit = _config.GetValue<int>("TabHistoryLimit", 200);
            if (history.Count > limit)
            {
                history.RemoveRange(limit, history.Count - limit);
            }

            try
            {
                var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save tab history");
            }
        }
    }
}
