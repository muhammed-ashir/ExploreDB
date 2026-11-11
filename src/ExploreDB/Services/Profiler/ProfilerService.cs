using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ExploreDB.Services.Profiler.Analytics;
using ExploreDB.Services.Profiler.Buffers;
using ExploreDB.Services.Profiler.Engine;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler;

/// <summary>
/// Singleton coordinator service managing real-time SQL Extended Event streaming,
/// bounded ring buffer storage, query aggregates, metrics calculation, and demo simulation.
/// </summary>
public sealed class ProfilerService : IAsyncDisposable
{
    private readonly XEventSessionManager _sessionManager = new();
    private readonly ProfilerStreamerService _streamer = new();
    private readonly WorkloadSimulator _simulator = new();

    private CircularEventBuffer<TraceEvent> _eventBuffer = new(100_000);
    private readonly MetricsCollector _metricsCollector = new();
    private readonly ConcurrentDictionary<string, QueryAggregate> _aggregates = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _traceCts;
    private Task? _streamingTask;
    private Task? _consumerTask;
    private Task? _metricsTimerTask;

    private readonly List<TraceEvent> _pendingUiBatch = new();
    private readonly object _batchLock = new();

    private readonly ConnectionService? _connectionService;

    public ProfilerService(ConnectionService? connectionService = null)
    {
        _connectionService = connectionService;
        if (_connectionService != null)
        {
            _connectionService.OnConnectionChanged += HandleConnectionChanged;
        }
    }

    private void HandleConnectionChanged()
    {
        if (IsStreaming)
        {
            _ = StopAsync();
        }
    }

    public event Action<IReadOnlyList<TraceEvent>>? EventsBatchAvailable;
    public event Action<MetricsSnapshot>? MetricsUpdated;
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    public bool IsStreaming { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsDemoMode { get; private set; }
    public string? ActiveSessionName => _sessionManager.ActiveSessionName;

    public int TotalBufferedEvents => _eventBuffer.Count;
    public int BufferCapacity => _eventBuffer.Capacity;
    public MetricsSnapshot CurrentMetrics { get; private set; } = new();

    public void SetBufferCapacity(int newCapacity)
    {
        if (newCapacity < 10_000) newCapacity = 10_000;
        if (newCapacity > 500_000) newCapacity = 500_000;

        lock (_batchLock)
        {
            if (_eventBuffer.Capacity == newCapacity)
                return;

            var oldEvents = _eventBuffer.ToList();
            var newBuffer = new CircularEventBuffer<TraceEvent>(newCapacity);
            if (oldEvents.Count > newCapacity)
            {
                oldEvents = oldEvents.Skip(oldEvents.Count - newCapacity).ToList();
            }
            newBuffer.AddRange(oldEvents);
            _eventBuffer = newBuffer;
        }
    }

    public IReadOnlyList<TraceEvent> GetAllBufferedEvents() => _eventBuffer.ToList();

    public IReadOnlyCollection<QueryAggregate> GetAggregates() => _aggregates.Values.ToList();

    private static string NormalizeToMasterConnectionString(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master"
            };
            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    public async Task<(bool Success, string? Error)> StartAsync(TraceSessionConfig config)
    {
        if (IsStreaming)
            return (true, null);

        string? targetDatabase = config.Filters.DatabaseName;

        // Pre-check permissions: check Database-level permissions on targetDatabase first,
        // then fall back to server-level permissions
        var (hasPerms, isDbScope, permError) = await XEventSessionManager.CheckPermissionsAsync(
            config.ConnectionString, 
            targetDatabase);

        if (!hasPerms)
        {
            return (false, permError ?? "User lacks permissions to create Extended Event sessions.");
        }

        string activeConnStr = isDbScope && !string.IsNullOrWhiteSpace(targetDatabase)
            ? XEventSessionManager.BuildDatabaseConnectionString(config.ConnectionString, targetDatabase)
            : NormalizeToMasterConnectionString(config.ConnectionString);

        _traceCts = new CancellationTokenSource();
        var ct = _traceCts.Token;

        string scopeDescription = isDbScope ? $"database '{targetDatabase}'" : "SQL Server instance";
        StatusChanged?.Invoke($"Creating Extended Event session on {scopeDescription}: {config.SessionName}...");

        try
        {
            // 1. Create and start session
            await _sessionManager.StartSessionAsync(activeConnStr, config, isDbScope, ct);

            StatusChanged?.Invoke($"Session active on {scopeDescription}. Ingesting live event stream...");

            var channel = Channel.CreateBounded<TraceEvent>(new BoundedChannelOptions(50_000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

            IsStreaming = true;
            IsPaused = false;
            IsDemoMode = false;

            // 2. Launch live streamer
            _streamingTask = Task.Run(async () =>
            {
                try
                {
                    await _streamer.StreamEventsAsync(activeConnStr, config.SessionName, channel.Writer, ct, targetDatabase);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke($"Streaming error: {ex.Message}");
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }, ct);

            // 3. Launch background consumer
            _consumerTask = Task.Run(async () =>
            {
                await ConsumeEventsAsync(channel.Reader, ct);
            }, ct);

            // 4. Launch metrics timer
            _metricsTimerTask = Task.Run(async () =>
            {
                await RunMetricsLoopAsync(ct);
            }, ct);

            return (true, null);
        }
        catch (Exception ex)
        {
            await StopAsync();
            return (false, ex.Message);
        }
    }

    public async Task StartDemoModeAsync()
    {
        if (IsStreaming)
            await StopAsync();

        _traceCts = new CancellationTokenSource();
        var ct = _traceCts.Token;

        StatusChanged?.Invoke("⚡ Demo Simulation Mode active (Simulating queries, RPC, errors & deadlocks)...");

        var channel = Channel.CreateBounded<TraceEvent>(new BoundedChannelOptions(25_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        IsStreaming = true;
        IsPaused = false;
        IsDemoMode = true;

        // Launch simulation generator
        _streamingTask = Task.Run(async () =>
        {
            try
            {
                await _simulator.StartSimulationAsync(channel.Writer, ct);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        // Launch consumer
        _consumerTask = Task.Run(async () =>
        {
            await ConsumeEventsAsync(channel.Reader, ct);
        }, ct);

        // Launch metrics timer
        _metricsTimerTask = Task.Run(async () =>
        {
            await RunMetricsLoopAsync(ct);
        }, ct);
    }

    public void Pause()
    {
        IsPaused = true;
        StatusChanged?.Invoke("Live trace paused.");
    }

    public void Resume()
    {
        IsPaused = false;
        StatusChanged?.Invoke(IsDemoMode ? "⚡ Demo Simulation active." : "Live trace streaming...");
    }

    public async Task StopAsync()
    {
        if (!IsStreaming && _traceCts == null)
            return;

        StatusChanged?.Invoke("Stopping trace and cleaning up session...");

        try
        {
            _traceCts?.Cancel();
        }
        catch { }

        // Drop session from SQL Server
        if (!IsDemoMode)
        {
            try
            {
                await _sessionManager.StopAndDropSessionAsync();
            }
            catch { }
        }

        try
        {
            if (_streamingTask != null) await Task.WhenAny(_streamingTask, Task.Delay(500));
            if (_consumerTask != null) await Task.WhenAny(_consumerTask, Task.Delay(500));
            if (_metricsTimerTask != null) await Task.WhenAny(_metricsTimerTask, Task.Delay(500));
        }
        catch { }

        _traceCts?.Dispose();
        _traceCts = null;
        _streamingTask = null;
        _consumerTask = null;
        _metricsTimerTask = null;

        IsStreaming = false;
        IsPaused = false;
        IsDemoMode = false;

        // Flush any remaining UI batch
        FlushPendingUiBatch();

        StatusChanged?.Invoke("Trace stopped.");
    }

    public void Clear()
    {
        lock (_batchLock)
        {
            _pendingUiBatch.Clear();
            _eventBuffer.Clear();
            _aggregates.Clear();
            _metricsCollector.Reset();
            CurrentMetrics = new MetricsSnapshot();
        }
        EventsBatchAvailable?.Invoke(Array.Empty<TraceEvent>());
        MetricsUpdated?.Invoke(CurrentMetrics);
    }

    private async Task ConsumeEventsAsync(ChannelReader<TraceEvent> reader, CancellationToken ct)
    {
        var flushTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(70));

        // Background task to flush batches to the UI every 70ms
        _ = Task.Run(async () =>
        {
            try
            {
                while (await flushTimer.WaitForNextTickAsync(ct))
                {
                    FlushPendingUiBatch();
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                flushTimer.Dispose();
            }
        }, ct);

        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var evt))
                {
                    if (evt == null) continue;

                    // 1. Record metrics
                    _metricsCollector.RecordEvent(evt);

                    // 2. Add to circular buffer
                    _eventBuffer.Add(evt);

                    // 3. Update query aggregates (if query hash available)
                    if (!string.IsNullOrEmpty(evt.QueryHash) && !string.IsNullOrEmpty(evt.NormalizedText))
                    {
                        var aggregate = _aggregates.GetOrAdd(evt.QueryHash, _ => new QueryAggregate
                        {
                            QueryHash = evt.QueryHash,
                            NormalizedText = evt.NormalizedText,
                            DatabaseName = evt.DatabaseName
                        });

                        lock (aggregate)
                        {
                            aggregate.AddExecution(evt);
                        }
                    }

                    // 4. Queue for UI batch if not paused
                    if (!IsPaused)
                    {
                        lock (_batchLock)
                        {
                            _pendingUiBatch.Add(evt);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void FlushPendingUiBatch()
    {
        List<TraceEvent> batch;
        lock (_batchLock)
        {
            if (_pendingUiBatch.Count == 0) return;
            batch = new List<TraceEvent>(_pendingUiBatch);
            _pendingUiBatch.Clear();
        }

        try
        {
            EventsBatchAvailable?.Invoke(batch);
        }
        catch { }
    }

    private async Task RunMetricsLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var snapshot = _metricsCollector.Sample();
                CurrentMetrics = snapshot;
                MetricsUpdated?.Invoke(snapshot);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connectionService != null)
        {
            _connectionService.OnConnectionChanged -= HandleConnectionChanged;
        }
        await StopAsync();
        await _sessionManager.DisposeAsync();
    }
}
