using System;
using System.Collections.Generic;

namespace ExploreDB.Services.SlowQuery;

public enum QueryStoreState
{
    NotSupported,
    Disabled,
    ReadOnly,
    Enabled
}

public enum RankingMetric
{
    TotalCpu,
    AvgCpu,
    TotalDuration,
    AvgDuration,
    PhysicalReads,
    LogicalReads,
    LogicalWrites,
    ExecutionCount
}

public enum TimeWindowPreset
{
    Last1Hour,
    Last4Hours,
    Last24Hours,
    Last7Days,
    Last30Days,
    Custom
}

public class SlowQueryFilterOptions
{
    public RankingMetric Metric { get; set; } = RankingMetric.TotalCpu;
    public int TopCount { get; set; } = 10;
    public TimeWindowPreset TimeWindow { get; set; } = TimeWindowPreset.Last24Hours;
    public DateTime? CustomStartTime { get; set; }
    public DateTime? CustomEndTime { get; set; }
    public string? SearchTerm { get; set; }

    public (DateTime Start, DateTime End) GetResolvedTimeRange()
    {
        var now = DateTime.UtcNow;
        return TimeWindow switch
        {
            TimeWindowPreset.Last1Hour => (now.AddHours(-1), now),
            TimeWindowPreset.Last4Hours => (now.AddHours(-4), now),
            TimeWindowPreset.Last24Hours => (now.AddDays(-1), now),
            TimeWindowPreset.Last7Days => (now.AddDays(-7), now),
            TimeWindowPreset.Last30Days => (now.AddDays(-30), now),
            TimeWindowPreset.Custom => (CustomStartTime ?? now.AddDays(-1), CustomEndTime ?? now),
            _ => (now.AddDays(-1), now)
        };
    }
}

public class SlowQueryItem
{
    public int Rank { get; set; }
    public long QueryId { get; set; }
    public string QueryHash { get; set; } = string.Empty;
    public string StatementText { get; set; } = string.Empty;
    public string FullBatchText { get; set; } = string.Empty;
    public string? ObjectSchema { get; set; }
    public string? ObjectName { get; set; }

    public string DisplayObjectName => string.IsNullOrEmpty(ObjectName) 
        ? "[Ad-hoc Statement]" 
        : (string.IsNullOrEmpty(ObjectSchema) ? ObjectName : $"{ObjectSchema}.{ObjectName}");

    public int PlanCount { get; set; } = 1;
    public long ExecutionCount { get; set; }

    // Duration (ms)
    public double TotalDurationMs { get; set; }
    public double AvgDurationMs { get; set; }
    public double MinDurationMs { get; set; }
    public double MaxDurationMs { get; set; }

    // CPU (ms)
    public double TotalCpuMs { get; set; }
    public double AvgCpuMs { get; set; }
    public double MinCpuMs { get; set; }
    public double MaxCpuMs { get; set; }

    // Disk & Memory I/O
    public long TotalPhysicalReads { get; set; }
    public double AvgPhysicalReads { get; set; }
    public long TotalLogicalReads { get; set; }
    public double AvgLogicalReads { get; set; }
    public long TotalLogicalWrites { get; set; }
    public double AvgLogicalWrites { get; set; }

    // Rows
    public long TotalRows { get; set; }
    public double AvgRows { get; set; }

    // Timestamps
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastExecutionTime { get; set; }

    // Execution Plan
    public string? QueryPlanXml { get; set; }

    // Relative visual impact (0-100%)
    public double ImpactPercentage { get; set; }

    public string FormattedTotalDuration => FormatDuration(TotalDurationMs);
    public string FormattedAvgDuration => FormatDuration(AvgDurationMs);
    public string FormattedTotalCpu => FormatDuration(TotalCpuMs);
    public string FormattedAvgCpu => FormatDuration(AvgCpuMs);

    public static string FormatDuration(double ms)
    {
        if (ms < 0.001) return "0 ms";
        if (ms < 1.0) return $"{ms:F2} ms";
        if (ms < 1000) return $"{ms:F1} ms";
        double sec = ms / 1000.0;
        if (sec < 60) return $"{sec:F2} s";
        double min = sec / 60.0;
        if (min < 60) return $"{min:F1} m";
        double hr = min / 60.0;
        return $"{hr:F1} h";
    }

    public static string FormatNumber(long num)
    {
        if (num < 1000) return num.ToString("N0");
        if (num < 1_000_000) return $"{(num / 1000.0):F1}k";
        if (num < 1_000_000_000) return $"{(num / 1_000_000.0):F1}M";
        return $"{(num / 1_000_000_000.0):F1}B";
    }

    public static string FormatNumber(double num)
    {
        if (num < 1000) return num.ToString("N1");
        if (num < 1_000_000) return $"{(num / 1000.0):F1}k";
        return $"{(num / 1_000_000.0):F1}M";
    }
}

public class QueryIntervalTrend
{
    public DateTime IntervalStart { get; set; }
    public DateTime IntervalEnd { get; set; }
    public long Executions { get; set; }
    public double AvgDurationMs { get; set; }
    public double AvgCpuMs { get; set; }
    public double AvgPhysicalReads { get; set; }
    public double AvgLogicalReads { get; set; }
}

public class SlowQueryResult
{
    public QueryStoreState StoreState { get; set; }
    public string? StateDescription { get; set; }
    public bool IsQueryStoreSupported => StoreState == QueryStoreState.Enabled || StoreState == QueryStoreState.ReadOnly;
    public bool IsUsingQueryStore { get; set; }
    public List<SlowQueryItem> Queries { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime SnapshotTime { get; set; } = DateTime.Now;
}
