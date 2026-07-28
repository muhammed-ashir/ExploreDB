using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ExploreDB.Services
{
    public class QueryHistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title => $"Query {Id.Substring(0, 5).ToUpper()}";
        public string QueryText { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
        public string ServerName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public long ExecutionTimeMs { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class HistoryService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<HistoryService> _logger;
        private readonly string _historyFilePath;
        private List<QueryHistoryItem>? _historyCache;

        public HistoryService(IConfiguration config, ILogger<HistoryService> logger)
        {
            _config = config;
            _logger = logger;
            _historyFilePath = Path.Combine(FileSystem.AppDataDirectory, "query_history.json");
        }

        public async Task<List<QueryHistoryItem>> GetHistoryAsync()
        {
            if (_historyCache != null)
                return _historyCache;

            if (!File.Exists(_historyFilePath))
            {
                _historyCache = new List<QueryHistoryItem>();
                return _historyCache;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_historyFilePath);
                _historyCache = JsonSerializer.Deserialize<List<QueryHistoryItem>>(json) ?? new List<QueryHistoryItem>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load query history");
                _historyCache = new List<QueryHistoryItem>();
            }

            return _historyCache;
        }

        public async Task AddHistoryItemAsync(string query, string server, string database, long executionTimeMs, bool isSuccess, string? errorMessage = null)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            var history = await GetHistoryAsync();

            var item = new QueryHistoryItem
            {
                QueryText = query,
                ServerName = server,
                DatabaseName = database,
                ExecutionTimeMs = executionTimeMs,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                ExecutedAt = DateTime.Now
            };

            // Insert at the top (newest first)
            history.Insert(0, item);

            var limit = _config.GetValue<int>("QueryHistoryLimit", 500);

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
                _logger.LogError(ex, "Failed to save query history");
            }
        }
    }
}
