using System;
using System.Collections.Generic;
using System.Text;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler.Engine;

/// <summary>
/// Generates T-SQL scripts for provisioning, starting, stopping, and dropping Extended Event sessions on SQL Server.
/// </summary>
public static class XEventScriptGenerator
{
    public static string GenerateCreateSessionScript(TraceSessionConfig config)
    {
        string sessionName = EscapeIdentifier(config.SessionName);
        var sb = new StringBuilder();

        // Check and drop existing if already present
        sb.AppendLine($"IF EXISTS (SELECT * FROM sys.server_event_sessions WHERE name = '{EscapeString(config.SessionName)}')");
        sb.AppendLine($"    DROP EVENT SESSION {sessionName} ON SERVER;");
        sb.AppendLine();

        sb.AppendLine($"CREATE EVENT SESSION {sessionName} ON SERVER");

        var eventDefinitions = new List<string>();

        // Build server-side predicates
        var predicates = new List<string>();
        if (config.Filters.ExcludeSelf)
        {
            predicates.Add($"([sqlserver].[client_app_name] <> N'{EscapeString(config.Filters.SelfAppName)}')");
        }

        if (config.Filters.MinDurationMs.HasValue && config.Filters.MinDurationMs.Value > 0)
        {
            // Duration in XEvent is in microseconds
            long minDurMicroseconds = (long)(config.Filters.MinDurationMs.Value * 1000);
            predicates.Add($"([duration] >= {minDurMicroseconds})");
        }

        if (!string.IsNullOrWhiteSpace(config.Filters.DatabaseName))
        {
            predicates.Add($"([sqlserver].[database_name] = N'{EscapeString(config.Filters.DatabaseName)}')");
        }

        string whereClause = predicates.Count > 0
            ? " WHERE (" + string.Join(" AND ", predicates) + ")"
            : string.Empty;

        // Common actions to capture
        string actions = @"ACTION(
            [sqlserver].[client_app_name],
            [sqlserver].[client_hostname],
            [sqlserver].[database_name],
            [sqlserver].[database_id],
            [sqlserver].[nt_username],
            [sqlserver].[session_id],
            [sqlserver].[sql_text],
            [sqlserver].[transaction_id]
        )";

        // 1. sql_batch_completed
        if (config.Filters.IncludeSqlBatch)
        {
            eventDefinitions.Add($"ADD EVENT [sqlserver].[sql_batch_completed]({actions}{whereClause})");
        }

        // 2. rpc_completed
        if (config.Filters.IncludeRpc)
        {
            eventDefinitions.Add($"ADD EVENT [sqlserver].[rpc_completed]({actions}{whereClause})");
        }

        // 3. error_reported
        if (config.Filters.IncludeErrors)
        {
            // Errors don't have duration, only exclude self & filter severity >= 11
            string errorWhere = config.Filters.ExcludeSelf
                ? $" WHERE ([sqlserver].[client_app_name] <> N'{EscapeString(config.Filters.SelfAppName)}' AND [severity] >= 11)"
                : " WHERE ([severity] >= 11)";
            eventDefinitions.Add($"ADD EVENT [sqlserver].[error_reported]({actions}{errorWhere})");
        }

        // 4. xml_deadlock_report
        if (config.Filters.IncludeDeadlocks)
        {
            eventDefinitions.Add($"ADD EVENT [sqlserver].[xml_deadlock_report]({actions})");
        }

        // 5. attention (canceled queries)
        if (config.Filters.IncludeAttention)
        {
            eventDefinitions.Add($"ADD EVENT [sqlserver].[attention]({actions}{whereClause})");
        }

        // Fallback if none selected
        if (eventDefinitions.Count == 0)
        {
            eventDefinitions.Add($"ADD EVENT [sqlserver].[sql_batch_completed]({actions}{whereClause})");
        }

        sb.AppendLine(string.Join("," + Environment.NewLine, eventDefinitions));

        // Session options
        sb.AppendLine($"WITH (");
        sb.AppendLine($"    MAX_MEMORY = {config.MaxMemoryKb} KB,");
        sb.AppendLine($"    EVENT_RETENTION_MODE = ALLOW_SINGLE_EVENT_LOSS,");
        sb.AppendLine($"    MAX_DISPATCH_LATENCY = {config.MaxDispatchLatencySeconds} SECONDS,");
        sb.AppendLine($"    MAX_EVENT_SIZE = 0 KB,");
        sb.AppendLine($"    MEMORY_PARTITION_MODE = NONE,");
        sb.AppendLine($"    TRACK_CAUSALITY = OFF,");
        sb.AppendLine($"    STARTUP_STATE = OFF");
        sb.AppendLine($");");

        return sb.ToString();
    }

    public static string GenerateStartSessionScript(string sessionName) =>
        $"ALTER EVENT SESSION {EscapeIdentifier(sessionName)} ON SERVER STATE = START;";

    public static string GenerateStopSessionScript(string sessionName) =>
        $"ALTER EVENT SESSION {EscapeIdentifier(sessionName)} ON SERVER STATE = STOP;";

    public static string GenerateDropSessionScript(string sessionName) =>
        $"IF EXISTS (SELECT * FROM sys.server_event_sessions WHERE name = '{EscapeString(sessionName)}')\n" +
        $"    DROP EVENT SESSION {EscapeIdentifier(sessionName)} ON SERVER;";

    private static string EscapeIdentifier(string name) => $"[{name.Replace("]", "]]")}]";
    private static string EscapeString(string value) => value.Replace("'", "''");
}
