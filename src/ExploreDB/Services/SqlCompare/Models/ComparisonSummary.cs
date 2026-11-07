namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Aggregated summary statistics of a schema comparison execution.
/// </summary>
public class ComparisonSummary
{
    public string SourceDescription { get; set; } = string.Empty;
    public string TargetDescription { get; set; } = string.Empty;
    public DateTime ComparisonTimestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan ElapsedTime { get; set; } = TimeSpan.Zero;

    public int TotalObjectsCompared { get; set; }
    public int IdenticalCount { get; set; }
    public int AddedCount { get; set; }
    public int DeletedCount { get; set; }
    public int ModifiedCount { get; set; }

    public int TotalDifferences => AddedCount + DeletedCount + ModifiedCount;

    public double SimilarityPercentage =>
        TotalObjectsCompared > 0
            ? Math.Round((double)IdenticalCount / TotalObjectsCompared * 100, 1)
            : 100.0;
}
