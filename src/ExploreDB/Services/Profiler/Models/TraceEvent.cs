using System;
using System.Collections.Generic;

namespace ExploreDB.Services.Profiler.Models;

/// <summary>
/// Represents a captured SQL Server Extended Event item with metrics and execution context.
/// </summary>
public sealed class TraceEvent
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Name { get; init; } = string.Empty;
    public TraceEventType EventType { get; init; }

    // Core Metrics
    public long DurationMicroseconds { get; init; }
    public double DurationMilliseconds => DurationMicroseconds / 1000.0;
    public long CpuMicroseconds { get; init; }
    public double CpuMilliseconds => CpuMicroseconds / 1000.0;
    public long LogicalReads { get; init; }
    public long PhysicalReads { get; init; }
    public long Writes { get; init; }
    public long RowCount { get; init; }

    // Query Data
    public string? TextData { get; init; }
    public string? NormalizedText { get; set; }
    public string? QueryHash { get; set; }

    // Execution Context
    public string? DatabaseName { get; init; }
    public int? DatabaseId { get; init; }
    public int? Spid { get; init; }
    public string? ClientAppName { get; init; }
    public string? ClientHostName { get; init; }
    public string? NtUserName { get; init; }
    public long? TransactionId { get; init; }

    // Error / Deadlock Info
    public int? ErrorNumber { get; init; }
    public int? Severity { get; init; }
    public int? State { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DeadlockXml { get; init; }

    // Custom fields / actions captured from XEvent
    public IReadOnlyDictionary<string, object?> Fields { get; init; } = new Dictionary<string, object?>();

    // Helper for fast summary display
    public string SummaryText
    {
        get
        {
            if (EventType == TraceEventType.Deadlock)
                return "[Deadlock Occurred]";

            if (EventType == TraceEventType.ErrorReported)
                return $"Error {ErrorNumber} (Sev {Severity}): {ErrorMessage ?? TextData}";

            if (string.IsNullOrWhiteSpace(TextData))
                return Name;

            var firstLine = TextData.AsSpan().Trim();
            int newlineIdx = firstLine.IndexOfAny('\r', '\n');
            if (newlineIdx > 0)
                firstLine = firstLine[..newlineIdx].Trim();

            return firstLine.Length > 200 ? $"{firstLine[..200]}..." : firstLine.ToString();
        }
    }
}
