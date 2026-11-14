using System;
using System.Linq;

namespace ExploreDB.Services.Profiler.Models;

/// <summary>
/// Session configuration options for Extended Events provisioning.
/// </summary>
public sealed class TraceSessionConfig
{
    private static string GenerateDefaultSessionName()
    {
        string machine = new string(Environment.MachineName.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(machine)) machine = "Client";
        if (machine.Length > 24) machine = machine[..24];

        string user = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(user)) user = "User";
        if (user.Length > 24) user = user[..24];

        return $"ExploreDB_Profiler_{machine}_{user}_{Environment.ProcessId}";
    }

    public string SessionName { get; set; } = GenerateDefaultSessionName();
    public int MaxMemoryKb { get; set; } = 8192;
    public int MaxDispatchLatencySeconds { get; set; } = 1;
    public int MaxEventBufferSize { get; set; } = 100_000;
    public int AutoStopMinutes { get; set; } = 30;

    public TraceFilterCriteria Filters { get; set; } = new();

    public string ConnectionString { get; set; } = string.Empty;
}
