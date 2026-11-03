using System;

namespace ExploreDB.Services.Profiler.Models;

/// <summary>
/// Criteria used for both server-side session filtering and client-side live filtering.
/// </summary>
public sealed class TraceFilterCriteria
{
    public double? MinDurationMs { get; set; }
    public double? MinCpuMs { get; set; }
    public long? MinLogicalReads { get; set; }

    public string? DatabaseName { get; set; }
    public string? ClientAppName { get; set; }
    public string? ClientHostName { get; set; }
    public int? Spid { get; set; }

    public string? SearchText { get; set; }

    public bool ExcludeSelf { get; set; } = true;
    public string SelfAppName { get; set; } = "ExploreDB";

    public bool IncludeSqlBatch { get; set; } = true;
    public bool IncludeRpc { get; set; } = true;
    public bool IncludeErrors { get; set; } = true;
    public bool IncludeDeadlocks { get; set; } = true;
    public bool IncludeAttention { get; set; } = true;

    /// <summary>
    /// Fast in-memory check whether an event matches the criteria.
    /// </summary>
    public bool Matches(TraceEvent evt)
    {
        // Event Type check
        if (!IncludeSqlBatch && evt.EventType == TraceEventType.SqlBatchCompleted) return false;
        if (!IncludeRpc && evt.EventType == TraceEventType.RpcCompleted) return false;
        if (!IncludeErrors && evt.EventType == TraceEventType.ErrorReported) return false;
        if (!IncludeDeadlocks && evt.EventType == TraceEventType.Deadlock) return false;
        if (!IncludeAttention && evt.EventType == TraceEventType.Attention) return false;

        // Self Exclude
        if (ExcludeSelf && string.Equals(evt.ClientAppName, SelfAppName, StringComparison.OrdinalIgnoreCase))
            return false;

        // Metrics threshold check
        if (MinDurationMs.HasValue && evt.DurationMilliseconds < MinDurationMs.Value) return false;
        if (MinCpuMs.HasValue && evt.CpuMilliseconds < MinCpuMs.Value) return false;
        if (MinLogicalReads.HasValue && evt.LogicalReads < MinLogicalReads.Value) return false;

        // Execution context checks
        if (Spid.HasValue && evt.Spid != Spid.Value) return false;

        if (!string.IsNullOrWhiteSpace(DatabaseName) &&
            !string.Equals(evt.DatabaseName, DatabaseName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(ClientAppName) &&
            (evt.ClientAppName == null || !evt.ClientAppName.Contains(ClientAppName, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(ClientHostName) &&
            (evt.ClientHostName == null || !evt.ClientHostName.Contains(ClientHostName, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Text search across SQL, summary, database, application, or error
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            bool matched = (evt.TextData != null && evt.TextData.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                           (evt.NormalizedText != null && evt.NormalizedText.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                           (evt.ErrorMessage != null && evt.ErrorMessage.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                           (evt.DatabaseName != null && evt.DatabaseName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                           (evt.ClientAppName != null && evt.ClientAppName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (!matched) return false;
        }

        return true;
    }
}
