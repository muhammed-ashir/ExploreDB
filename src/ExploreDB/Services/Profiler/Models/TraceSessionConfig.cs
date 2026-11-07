using System;

namespace ExploreDB.Services.Profiler.Models;

/// <summary>
/// Session configuration options for Extended Events provisioning.
/// </summary>
public sealed class TraceSessionConfig
{
    public string SessionName { get; set; } = $"ExploreDB_Profiler_{Environment.ProcessId}";
    public int MaxMemoryKb { get; set; } = 8192;
    public int MaxDispatchLatencySeconds { get; set; } = 1;
    public int MaxEventBufferSize { get; set; } = 100_000;

    public TraceFilterCriteria Filters { get; set; } = new();

    public string ConnectionString { get; set; } = string.Empty;
}
