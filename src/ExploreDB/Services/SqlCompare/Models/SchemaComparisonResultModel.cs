using System.Collections.Generic;
using Microsoft.SqlServer.Dac.Compare;

namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Complete result of a schema comparison execution.
/// </summary>
public class SchemaComparisonResultModel
{
    public List<SchemaDifferenceItem> Differences { get; set; } = [];
    public ComparisonSummary Summary { get; set; } = new();
    public ComparisonOptions Options { get; set; } = new();
    public DatabaseEndpoint SourceEndpoint { get; set; } = new();
    public DatabaseEndpoint TargetEndpoint { get; set; } = new();

    /// <summary>
    /// Underlying DacFx result object, if available, used for incremental script generation.
    /// </summary>
    public SchemaComparisonResult? RawDacResult { get; set; }

    /// <summary>
    /// Generated synchronization script.
    /// </summary>
    public string SynchronizationScript { get; set; } = string.Empty;
}
