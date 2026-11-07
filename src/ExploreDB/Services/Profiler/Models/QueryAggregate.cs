using System;

namespace ExploreDB.Services.Profiler.Models;

/// <summary>
/// Aggregated performance metrics for a specific normalized query pattern.
/// </summary>
public sealed class QueryAggregate
{
    public required string QueryHash { get; init; }
    public required string NormalizedText { get; init; }
    public string? DatabaseName { get; init; }

    public long ExecutionCount { get; set; }

    // Duration (in milliseconds)
    public double TotalDurationMs { get; set; }
    public double MinDurationMs { get; set; } = double.MaxValue;
    public double MaxDurationMs { get; set; }
    public double AvgDurationMs => ExecutionCount > 0 ? TotalDurationMs / ExecutionCount : 0;

    // CPU (in milliseconds)
    public double TotalCpuMs { get; set; }
    public double MaxCpuMs { get; set; }
    public double AvgCpuMs => ExecutionCount > 0 ? TotalCpuMs / ExecutionCount : 0;

    // Logical Reads
    public long TotalLogicalReads { get; set; }
    public long MaxLogicalReads { get; set; }
    public double AvgLogicalReads => ExecutionCount > 0 ? (double)TotalLogicalReads / ExecutionCount : 0;

    // Writes
    public long TotalWrites { get; set; }

    // Timestamps
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    public void AddExecution(TraceEvent evt)
    {
        ExecutionCount++;

        double dur = evt.DurationMilliseconds;
        TotalDurationMs += dur;
        if (dur < MinDurationMs) MinDurationMs = dur;
        if (dur > MaxDurationMs) MaxDurationMs = dur;

        double cpu = evt.CpuMilliseconds;
        TotalCpuMs += cpu;
        if (cpu > MaxCpuMs) MaxCpuMs = cpu;

        TotalLogicalReads += evt.LogicalReads;
        if (evt.LogicalReads > MaxLogicalReads) MaxLogicalReads = evt.LogicalReads;

        TotalWrites += evt.Writes;

        if (FirstSeen == default || evt.Timestamp < FirstSeen) FirstSeen = evt.Timestamp;
        if (evt.Timestamp > LastSeen) LastSeen = evt.Timestamp;
    }
}
