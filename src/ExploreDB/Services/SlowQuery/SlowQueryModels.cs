using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

    public bool IsAdHoc => string.IsNullOrWhiteSpace(ObjectName);

    public string ObjectFullName => string.IsNullOrEmpty(ObjectName) 
        ? string.Empty 
        : (string.IsNullOrEmpty(ObjectSchema) ? ObjectName : $"{ObjectSchema}.{ObjectName}");

    public string DisplayObjectName
    {
        get
        {
            if (!IsAdHoc)
            {
                return ObjectFullName;
            }

            var summary = GetOperationAndTargetTable(StatementText);
            if (!string.IsNullOrEmpty(summary))
            {
                return summary;
            }

            var preview = GetSqlSnippetPreview(StatementText, 60);
            return !string.IsNullOrEmpty(preview) ? preview : "[Ad-hoc Statement]";
        }
    }

    public static string GetOperationAndTargetTable(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

        // Take initial chunk to avoid expensive regex on huge script batches
        string text = sql.Length > 2500 ? sql.Substring(0, 2500) : sql;

        // 1. Remove comments
        try
        {
            text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
        }
        catch { }

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var nonCommentLines = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("--")) continue;

            int dashDash = line.IndexOf("--", StringComparison.Ordinal);
            if (dashDash >= 0)
            {
                line = line.Substring(0, dashDash).Trim();
            }

            if (!string.IsNullOrEmpty(line))
            {
                nonCommentLines.Add(line);
            }
        }
        text = string.Join(" ", nonCommentLines);

        // 2. Skip parameter definitions prefix e.g. "(@p0 int, @p1 nvarchar(max))SELECT..."
        try
        {
            var match = Regex.Match(
                text, 
                @"\b(SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE)\b", 
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100));

            if (match.Success && match.Index > 0)
            {
                var prefix = text.Substring(0, match.Index).Trim();
                if (prefix.StartsWith("(") || prefix.Length < 80)
                {
                    text = text.Substring(match.Index);
                }
            }
        }
        catch { }

        // Collapse whitespace
        try
        {
            text = Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
        }
        catch
        {
            text = text.Trim();
        }

        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 3. Extract Operation Verb
        var verbMatch = Regex.Match(text, @"^(SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE)\b", RegexOptions.IgnoreCase);
        if (!verbMatch.Success)
        {
            return string.Empty;
        }

        string verb = verbMatch.Value.ToUpperInvariant();

        // 4. Handle SELECT
        if (verb == "SELECT")
        {
            string topInfo = "";
            var topMatch = Regex.Match(text, @"^SELECT\s+TOP\s*\(?\s*(\d+)\s*\)?", RegexOptions.IgnoreCase);
            if (topMatch.Success && long.TryParse(topMatch.Groups[1].Value, out long topVal))
            {
                topInfo = $" (TOP {FormatNumber(topVal)})";
            }

            // Find target table after FROM
            var fromMatch = Regex.Match(
                text, 
                @"\bFROM\s+((?:\[?[a-zA-Z_0-9@#]+\]?\.)*\[?[a-zA-Z_0-9@#]+\]?)", 
                RegexOptions.IgnoreCase);

            if (fromMatch.Success)
            {
                string table = fromMatch.Groups[1].Value.Trim();
                if (!string.Equals(table, "SELECT", StringComparison.OrdinalIgnoreCase) && !table.StartsWith("("))
                {
                    return $"SELECT{topInfo} from {table}";
                }
            }

            var snippet = GetSqlSnippetPreview(text, 50);
            return !string.IsNullOrEmpty(snippet) ? snippet : $"SELECT{topInfo}";
        }

        // 5. Handle INSERT
        if (verb == "INSERT")
        {
            var insertMatch = Regex.Match(
                text, 
                @"^INSERT\s+(?:INTO\s+)?((?:\[?[a-zA-Z_0-9@#]+\]?\.)*\[?[a-zA-Z_0-9@#]+\]?)", 
                RegexOptions.IgnoreCase);

            if (insertMatch.Success)
            {
                string table = insertMatch.Groups[1].Value.Trim();
                return $"INSERT into {table}";
            }
            return "INSERT";
        }

        // 6. Handle UPDATE
        if (verb == "UPDATE")
        {
            var fromMatch = Regex.Match(
                text, 
                @"\bFROM\s+((?:\[?[a-zA-Z_0-9@#]+\]?\.)*\[?[a-zA-Z_0-9@#]+\]?)", 
                RegexOptions.IgnoreCase);

            if (fromMatch.Success)
            {
                string table = fromMatch.Groups[1].Value.Trim();
                return $"UPDATE {table}";
            }

            var updateMatch = Regex.Match(
                text, 
                @"^UPDATE\s+((?:\[?[a-zA-Z_0-9@#]+\]?\.)*\[?[a-zA-Z_0-9@#]+\]?)", 
                RegexOptions.IgnoreCase);

            if (updateMatch.Success)
            {
                string table = updateMatch.Groups[1].Value.Trim();
                return $"UPDATE {table}";
            }
            return "UPDATE";
        }

        // 7. Handle DELETE
        if (verb == "DELETE")
        {
            var deleteMatch = Regex.Match(
                text, 
                @"^DELETE\s+(?:FROM\s+)?((?:\[?[a-zA-Z_0-9@#]+\]?\.)*\[?[a-zA-Z_0-9@#]+\]?)", 
                RegexOptions.IgnoreCase);

            if (deleteMatch.Success)
            {
                string table = deleteMatch.Groups[1].Value.Trim();
                return $"DELETE from {table}";
            }
            return "DELETE";
        }

        // 8. Handle MERGE
        if (verb == "MERGE")
        {
            var mergeMatch = Regex.Match(
                text, 
                @"^MERGE\s+(?:INTO\s+)?((?:\[?[a-zA-Z_0-9@#]+\]?\.)*\[?[a-zA-Z_0-9@#]+\]?)", 
                RegexOptions.IgnoreCase);

            if (mergeMatch.Success)
            {
                string table = mergeMatch.Groups[1].Value.Trim();
                return $"MERGE into {table}";
            }
            return "MERGE";
        }

        // 9. Handle EXEC / EXECUTE
        if (verb == "EXEC" || verb == "EXECUTE")
        {
            var execMatch = Regex.Match(
                text, 
                @"^(?:EXEC|EXECUTE)\s+((?:\[?[a-zA-Z_0-9@#]+\]?\.)*\[?[a-zA-Z_0-9@#]+\]?)", 
                RegexOptions.IgnoreCase);

            if (execMatch.Success)
            {
                string proc = execMatch.Groups[1].Value.Trim();
                return $"EXEC {proc}";
            }
            return "EXEC";
        }

        return string.Empty;
    }

    public static string GetSqlSnippetPreview(string? sql, int maxLength = 85)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

        // Take only the initial chunk to prevent expensive processing on huge batches
        string text = sql.Length > 2000 ? sql.Substring(0, 2000) : sql;

        // 1. Remove block comments /* ... */
        try
        {
            text = Regex.Replace(
                text, @"/\*.*?\*/", " ", 
                RegexOptions.Singleline, 
                TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            // If regex times out, proceed with text
        }

        // 2. Remove single-line comments -- ...
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var nonCommentLines = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("--")) continue;

            int dashDash = line.IndexOf("--", StringComparison.Ordinal);
            if (dashDash >= 0)
            {
                line = line.Substring(0, dashDash).Trim();
            }

            if (!string.IsNullOrEmpty(line))
            {
                nonCommentLines.Add(line);
            }
        }

        text = string.Join(" ", nonCommentLines);

        // 3. Skip parameter definitions prefix e.g. "(@p0 int, @p1 nvarchar(max))SELECT..."
        try
        {
            var match = Regex.Match(
                text, 
                @"\b(SELECT|INSERT|UPDATE|DELETE|MERGE|WITH|EXEC|EXECUTE|CREATE|ALTER|DROP)\b", 
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100));

            if (match.Success && match.Index > 0)
            {
                var prefix = text.Substring(0, match.Index).Trim();
                // If prefix starts with '(' (parameter definition) or is short
                if (prefix.StartsWith("(") || prefix.Length < 60)
                {
                    text = text.Substring(match.Index);
                }
            }
        }
        catch
        {
            // Proceed without prefix stripping if regex times out
        }

        // 4. Collapse consecutive whitespace
        try
        {
            text = Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
        }
        catch
        {
            text = text.Trim();
        }

        if (string.IsNullOrEmpty(text)) return string.Empty;

        if (text.Length > maxLength)
        {
            return text.Substring(0, maxLength).TrimEnd() + "…";
        }

        return text;
    }

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
