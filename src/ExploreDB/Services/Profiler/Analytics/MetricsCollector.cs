using System;
using System.Threading;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler.Analytics;

/// <summary>
/// Thread-safe performance metrics aggregator that samples events into real-time rates (QPS, CPU/s, Reads/s).
/// </summary>
public sealed class MetricsCollector
{
    private long _totalEvents;
    private long _totalErrors;
    private long _totalDeadlocks;

    // Window accumulators
    private long _windowEvents;
    private long _windowDurationUs;
    private long _windowCpuUs;
    private long _windowLogicalReads;

    private long _lastSampleTicks = Environment.TickCount64;

    public void RecordEvent(TraceEvent evt)
    {
        Interlocked.Increment(ref _totalEvents);
        Interlocked.Increment(ref _windowEvents);
        Interlocked.Add(ref _windowDurationUs, evt.DurationMicroseconds);
        Interlocked.Add(ref _windowCpuUs, evt.CpuMicroseconds);
        Interlocked.Add(ref _windowLogicalReads, evt.LogicalReads);

        if (evt.EventType == TraceEventType.ErrorReported)
        {
            Interlocked.Increment(ref _totalErrors);
        }
        else if (evt.EventType == TraceEventType.Deadlock)
        {
            Interlocked.Increment(ref _totalDeadlocks);
        }
    }

    /// <summary>
    /// Computes and resets the current window snapshot. Call every ~1 second.
    /// </summary>
    public MetricsSnapshot Sample()
    {
        long nowTicks = Environment.TickCount64;
        long lastTicks = Interlocked.Exchange(ref _lastSampleTicks, nowTicks);
        double elapsedSeconds = Math.Max(0.001, (nowTicks - lastTicks) / 1000.0);

        long windowEvts = Interlocked.Exchange(ref _windowEvents, 0);
        long windowDurUs = Interlocked.Exchange(ref _windowDurationUs, 0);
        long windowCpuUs = Interlocked.Exchange(ref _windowCpuUs, 0);
        long windowReads = Interlocked.Exchange(ref _windowLogicalReads, 0);

        double qps = windowEvts / elapsedSeconds;
        double avgDurMs = windowEvts > 0 ? (windowDurUs / 1000.0) / windowEvts : 0;
        double cpuMsPerSec = (windowCpuUs / 1000.0) / elapsedSeconds;
        double readsPerSec = windowReads / elapsedSeconds;

        return new MetricsSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Qps = qps,
            AvgDurationMs = avgDurMs,
            CpuTimeMsPerSecond = cpuMsPerSec,
            LogicalReadsPerSecond = readsPerSec,
            TotalEventsCaptured = Interlocked.Read(ref _totalEvents),
            TotalErrors = Interlocked.Read(ref _totalErrors),
            TotalDeadlocks = Interlocked.Read(ref _totalDeadlocks)
        };
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalEvents, 0);
        Interlocked.Exchange(ref _totalErrors, 0);
        Interlocked.Exchange(ref _totalDeadlocks, 0);
        Interlocked.Exchange(ref _windowEvents, 0);
        Interlocked.Exchange(ref _windowDurationUs, 0);
        Interlocked.Exchange(ref _windowCpuUs, 0);
        Interlocked.Exchange(ref _windowLogicalReads, 0);
        Interlocked.Exchange(ref _lastSampleTicks, Environment.TickCount64);
    }
}
