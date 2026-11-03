using System;

namespace ExploreDB.Services.Profiler.Models;

public sealed class MetricsSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public double Qps { get; init; }
    public double AvgDurationMs { get; init; }
    public double CpuTimeMsPerSecond { get; init; }
    public double LogicalReadsPerSecond { get; init; }

    public long TotalEventsCaptured { get; init; }
    public long TotalErrors { get; init; }
    public long TotalDeadlocks { get; init; }
}
