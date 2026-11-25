using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly object _cacheLock = new();
        private List<TabHistoryItem>? _historyCache;
        private int _pendingFlushFlag = 0;

        public HistoryService(IConfiguration config, ILogger<HistoryService> logger)
        {
            _config = config;
            _logger = logger;
            _historyFilePath = Path.Combine(AppEnvironmentService.AppDataDirectory, "query_history.json");
        }

        public async Task<List<TabHistoryItem>> GetHistoryAsync()
        {
            lock (_cacheLock)
            {
                if (_historyCache != null)
                    return _historyCache.ToList();
            }

            await _ioLock.WaitAsync();
            try
            {
                lock (_cacheLock)
                {
                    if (_historyCache != null)
                        return _historyCache.ToList();
                }

                if (!File.Exists(_historyFilePath))
                {
                    lock (_cacheLock)
                    {
                        _historyCache ??= new List<TabHistoryItem>();
                        return _historyCache.ToList();
                    }
                }

                try
                {
                    var json = await File.ReadAllTextAsync(_historyFilePath);
                    var raw = JsonSerializer.Deserialize<List<TabHistoryItem>>(json) ?? new List<TabHistoryItem>();

                    // Purge legacy flat query records: only keep real tab history sessions
                    var loaded = raw
                        .Where(x => x.TabId != Guid.Empty && !string.IsNullOrWhiteSpace(x.TabName) && !string.IsNullOrWhiteSpace(x.ScriptText) && !x.TabName.StartsWith("Query_"))
                        .ToList();

                    lock (_cacheLock)
                    {
                        _historyCache = loaded;
                        return _historyCache.ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load tab history");
                    lock (_cacheLock)
                    {
                        _historyCache ??= new List<TabHistoryItem>();
                        return _historyCache.ToList();
                    }
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// Updates in-memory history immediately and flushes to disk asynchronously without blocking query completion.
        /// </summary>
        public void QueueSaveTabHistory(Guid tabId, string tabName, string fullScriptText, string serverName = "", string dbName = "", string connectionString = "", string connectionName = "")
        {
            if (string.IsNullOrWhiteSpace(fullScriptText) || tabId == Guid.Empty) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveTabHistoryAsync(tabId, tabName, fullScriptText, serverName, dbName, connectionString, connectionName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background tab history save failed");
                }
            });
        }

        public async Task SaveTabHistoryAsync(Guid tabId, string tabName, string fullScriptText, string serverName = "", string dbName = "", string connectionString = "", string connectionName = "")
        {
            if (string.IsNullOrWhiteSpace(fullScriptText) || tabId == Guid.Empty) return;

            // Ensure initial cache load is complete before mutating
            bool needsLoad;
            lock (_cacheLock) { needsLoad = _historyCache == null; }
            if (needsLoad)
            {
                await GetHistoryAsync();
            }

            List<TabHistoryItem> snapshot;
            var limit = _config.GetValue<int>("TabHistoryLimit", 200);

            lock (_cacheLock)
            {
                _historyCache ??= new List<TabHistoryItem>();
                var history = _historyCache;

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

                if (history.Count > limit)
                {
                    history.RemoveRange(limit, history.Count - limit);
                }

                snapshot = history.ToList();
            }

            // Coalesce concurrent disk writes so only the latest snapshot is persisted
            Interlocked.Exchange(ref _pendingFlushFlag, 1);
            await _ioLock.WaitAsync();
            try
            {
                if (Interlocked.Exchange(ref _pendingFlushFlag, 0) == 0)
                {
                    return;
                }

                List<TabHistoryItem> latestSnapshot;
                lock (_cacheLock)
                {
                    latestSnapshot = (_historyCache ?? snapshot).ToList();
                }

                var json = JsonSerializer.Serialize(latestSnapshot);
                await File.WriteAllTextAsync(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save tab history");
            }
            finally
            {
                _ioLock.Release();
            }
        }
    }
}
