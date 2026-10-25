namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Pre-flight estimated duration and schema complexity metadata for schema comparison.
/// </summary>
public class ComparisonEstimate
{
    public int EstimatedSeconds { get; set; } = 30;
    public bool IsColdStart { get; set; } = false;
    public int? SourceObjectCount { get; set; }
    public int? TargetObjectCount { get; set; }

    public string FormattedEstimate => $"{EstimatedSeconds / 60:D2}:{EstimatedSeconds % 60:D2}";
}
