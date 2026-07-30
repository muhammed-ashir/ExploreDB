using System.Text.Json;
using Microsoft.Maui.Storage;

namespace ExploreDB.Services;

public class QueryTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "SQLQuery1.sql";
    public string Content { get; set; } = "";
    public string? FilePath { get; set; }
}

public enum QueryMode { Select, Update, Delete }

public class QueryStateService
{
    public List<QueryTab> Tabs { get; set; } = new();
    public Guid ActiveTabId { get; set; }
    
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
    public HashSet<string> WaypointTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string TargetTable { get; set; } = "";
    public QueryMode CurrentMode { get; set; } = QueryMode.Select;
    public string ActiveBuilderTab { get; set; } = "Available";
    public Dictionary<string, string?> UpdateMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
