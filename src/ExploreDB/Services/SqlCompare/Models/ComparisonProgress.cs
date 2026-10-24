namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Status update event payload for ongoing comparison tasks.
/// </summary>
public class ComparisonProgress
{
    public string Message { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }

    public ComparisonProgress(string message, double progressPercentage = 0)
    {
        Message = message;
        ProgressPercentage = progressPercentage;
    }
}
