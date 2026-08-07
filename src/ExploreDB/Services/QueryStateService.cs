using System.Text.Json;
using Microsoft.Maui.Storage;

namespace ExploreDB.Services;

public class QueryTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "SQLQuery1.sql";
    public string Content { get; set; } = "";
    public string OriginalContent { get; set; } = "";
    public string? FilePath { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDirty => Content != OriginalContent;

    public string ConnectionString { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<IEnumerable<dynamic>> ResultSets { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> Messages { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasExecuted { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsExecuting { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public long LastExecutionTimeMs { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public int ExecElapsedSeconds { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCurrentQuerySelect { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationTokenSource? QueryCts { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPoppedOut { get; set; }
}

public enum QueryMode { Select, Update, Delete }

public class QueryStateService
{
    public List<QueryTab> Tabs { get; set; } = new();
    public Guid ActiveTabId { get; set; }
    
    public event Action<Guid>? OnTabUpdated;
    
    public void NotifyTabUpdated(Guid tabId)
    {
        OnTabUpdated?.Invoke(tabId);
    }
    
    // Tracks if the session prompt has already been shown this app lifecycle
    public bool Initialized { get; set; } = false;

    private string _tabsFilePath => Path.Combine(FileSystem.AppDataDirectory, "tabs_state.json");

    public bool HasSavedTabs() => File.Exists(_tabsFilePath);

    public void ClearSavedState()
    {
        if (File.Exists(_tabsFilePath))
        {
            try { File.Delete(_tabsFilePath); } catch { }
        }
    }

    public async Task LoadTabsAsync()
    {
        if (File.Exists(_tabsFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_tabsFilePath);
                var loadedTabs = JsonSerializer.Deserialize<List<QueryTab>>(json);
                if (loadedTabs != null && loadedTabs.Any())
                {
                    Tabs = loadedTabs;
                    ActiveTabId = Tabs.First().Id;
                    return;
                }
            }
            catch { }
        }

        Tabs.Clear();
        var initialTab = new QueryTab();
        Tabs.Add(initialTab);
        ActiveTabId = initialTab.Id;
    }

    public async Task SaveTabsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Tabs);
            await File.WriteAllTextAsync(_tabsFilePath, json);
        }
        catch { }
    }

    public string? CustomQuery { get; set; }

    // QueryBuilder.razor State
    public List<string> SelectedColumns { get; set; } = new();
    public HashSet<string> IgnoredRoutingTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IgnoredRoutingEdges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string TargetTable { get; set; } = "";
    public QueryMode CurrentMode { get; set; } = QueryMode.Select;
    public string ActiveBuilderTab { get; set; } = "Available";
    public Dictionary<string, string?> UpdateMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
