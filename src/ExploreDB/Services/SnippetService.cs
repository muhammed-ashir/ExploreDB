using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
namespace ExploreDB.Services
{
    public class SqlSnippet
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Shortcut { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class SnippetService
    {
        private readonly ILogger<SnippetService>? _logger;
        private readonly string _snippetFilePath;
        private List<SqlSnippet>? _snippetsCache;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SnippetService(ILogger<SnippetService>? logger = null)
        {
            _logger = logger;
            _snippetFilePath = Path.Combine(AppEnvironmentService.AppDataDirectory, "snippets.json");
        }

        public async Task<List<SqlSnippet>> GetSnippetsAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_snippetsCache != null)
                    return _snippetsCache;

                if (File.Exists(_snippetFilePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(_snippetFilePath);
                        _snippetsCache = JsonSerializer.Deserialize<List<SqlSnippet>>(json) ?? new List<SqlSnippet>();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to load snippets from {Path}", _snippetFilePath);
                        _snippetsCache = null;
                    }
                }

                if (_snippetsCache == null || _snippetsCache.Count == 0)
                {
                    _snippetsCache = GetDefaultSnippets();
                    await SaveCacheToFileAsync();
                }

                return _snippetsCache;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveSnippetAsync(SqlSnippet snippet)
        {
            if (string.IsNullOrWhiteSpace(snippet.Shortcut) || string.IsNullOrWhiteSpace(snippet.Code))
                return;

            await _lock.WaitAsync();
            try
            {
                if (_snippetsCache == null)
                {
                    _snippetsCache = await LoadInternalAsync();
                }

                var existing = _snippetsCache.FirstOrDefault(s => s.Id == snippet.Id || s.Shortcut.Equals(snippet.Shortcut.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Shortcut = snippet.Shortcut.Trim();
                    existing.Description = snippet.Description?.Trim() ?? string.Empty;
                    existing.Code = snippet.Code;
                }
                else
                {
                    snippet.Shortcut = snippet.Shortcut.Trim();
                    snippet.Description = snippet.Description?.Trim() ?? string.Empty;
                    _snippetsCache.Add(snippet);
                }

                await SaveCacheToFileAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteSnippetAsync(string id)
        {
            await _lock.WaitAsync();
            try
            {
                if (_snippetsCache == null)
                {
                    _snippetsCache = await LoadInternalAsync();
                }

                _snippetsCache.RemoveAll(s => s.Id == id);
                await SaveCacheToFileAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            await _lock.WaitAsync();
            try
            {
                _snippetsCache = GetDefaultSnippets();
                await SaveCacheToFileAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<List<SqlSnippet>> LoadInternalAsync()
        {
            if (File.Exists(_snippetFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_snippetFilePath);
                    var list = JsonSerializer.Deserialize<List<SqlSnippet>>(json);
                    if (list != null && list.Count > 0)
                        return list;
                }
                catch { }
            }
            return GetDefaultSnippets();
        }

        private async Task SaveCacheToFileAsync()
        {
            if (_snippetsCache == null) return;
            try
            {
                var dir = Path.GetDirectoryName(_snippetFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_snippetsCache, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_snippetFilePath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save snippets to {Path}", _snippetFilePath);
            }
        }

        public static List<SqlSnippet> GetDefaultSnippets()
        {
            return new List<SqlSnippet>
            {
                new() { Shortcut = "ssf", Description = "SELECT * FROM", Code = "SELECT * FROM $0", IsBuiltIn = true },
                new() { Shortcut = "ssfe", Description = "SELECT * FROM ... WHERE", Code = "SELECT * FROM $1 WHERE $0", IsBuiltIn = true },
                new() { Shortcut = "top10", Description = "SELECT TOP 10 * FROM", Code = "SELECT TOP 10 * FROM $0", IsBuiltIn = true },
                new() { Shortcut = "ct", Description = "CREATE TABLE template", Code = "CREATE TABLE $1 (\n    Id INT IDENTITY(1,1) PRIMARY KEY,\n    $0\n);", IsBuiltIn = true },
                new() { Shortcut = "ii", Description = "INSERT INTO template", Code = "INSERT INTO $1 ($2)\nVALUES ($0);", IsBuiltIn = true },
                new() { Shortcut = "wc", Description = "WHERE clause", Code = "WHERE $0", IsBuiltIn = true },
                new() { Shortcut = "tc", Description = "SELECT COUNT(*) FROM", Code = "SELECT COUNT(*) FROM $0", IsBuiltIn = true },
                new() { Shortcut = "sc", Description = "SELECT COUNT WITH (NOLOCK)", Code = "SELECT COUNT(*) FROM $1 WITH (NOLOCK) WHERE $0", IsBuiltIn = true },
                new() { Shortcut = "sfn", Description = "SELECT * WITH (NOLOCK)", Code = "SELECT * FROM $1 WITH (NOLOCK)", IsBuiltIn = true },
                new() { Shortcut = "df", Description = "DELETE FROM ... WHERE", Code = "DELETE FROM $1 WHERE $0", IsBuiltIn = true },
                new() { Shortcut = "uf", Description = "UPDATE ... SET ... WHERE", Code = "UPDATE $1\nSET $2\nWHERE $0", IsBuiltIn = true },
                new() { Shortcut = "gb", Description = "GROUP BY", Code = "GROUP BY $0", IsBuiltIn = true },
                new() { Shortcut = "ob", Description = "ORDER BY", Code = "ORDER BY $0", IsBuiltIn = true },
                new() { Shortcut = "btran", Description = "BEGIN TRANSACTION block", Code = "BEGIN TRANSACTION;\n\n$0\n\n-- COMMIT TRANSACTION;\n-- ROLLBACK TRANSACTION;", IsBuiltIn = true }
            };
        }
    }
}
