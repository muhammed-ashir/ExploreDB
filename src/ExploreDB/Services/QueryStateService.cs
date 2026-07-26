namespace ExploreDB.Services;

public enum QueryMode { Select, Update, Delete }

public class QueryStateService
{
    public string? CustomQuery { get; set; }

    // Home.razor State
    public List<string> SelectedColumns { get; set; } = new();
    public HashSet<string> IgnoredRoutingTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WaypointTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string TargetTable { get; set; } = "";
    public QueryMode CurrentMode { get; set; } = QueryMode.Select;
    public string ActiveBuilderTab { get; set; } = "Available";
    public Dictionary<string, string?> UpdateMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
