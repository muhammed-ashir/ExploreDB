using System;
using System.Collections.Generic;
using Microsoft.SqlServer.XEvent.XELite;
using ExploreDB.Services.Profiler.Analytics;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler.Engine;

/// <summary>
/// Converts raw XELite IXEvent instances into high-performance TraceEvent domain models.
/// </summary>
public static class XEventParser
{
    private static long _sequenceId = 0;

    public static TraceEvent Parse(IXEvent rawEvent, string? fallbackDatabaseName = null)
    {
        long id = System.Threading.Interlocked.Increment(ref _sequenceId);

        string name = rawEvent.Name;
        var eventType = MapEventType(name);

        // Read metrics from fields
        long durationUs = GetLong(rawEvent, "duration");
        long cpuUs = GetLong(rawEvent, "cpu_time");
        long logicalReads = GetLong(rawEvent, "logical_reads");
        long physicalReads = GetLong(rawEvent, "physical_reads");
        long writes = GetLong(rawEvent, "writes");
        long rowCount = GetLong(rawEvent, "row_count");

        // Read query text (try 'statement' in fields first, then 'sql_text' in actions)
        string? textData = GetString(rawEvent, "statement") ?? GetString(rawEvent, "sql_text");

        // Read context from actions or fields
        string? dbName = GetString(rawEvent, "database_name") ?? fallbackDatabaseName;
        int? dbId = GetInt(rawEvent, "database_id");
        int? spid = GetInt(rawEvent, "session_id");
        string? appName = GetString(rawEvent, "client_app_name");
        string? hostName = GetString(rawEvent, "client_hostname");
        string? ntUser = GetString(rawEvent, "nt_username") ?? GetString(rawEvent, "server_principal_name") ?? GetString(rawEvent, "username");
        long? txId = GetNullableLong(rawEvent, "transaction_id");

        // Read errors or deadlocks
        int? errorNum = GetInt(rawEvent, "error_number");
        int? severity = GetInt(rawEvent, "severity");
        int? state = GetInt(rawEvent, "state");
        string? errorMessage = GetString(rawEvent, "message");
        string? deadlockXml = GetString(rawEvent, "xml_report");

        // Precompute normalization and query hash for SQL queries
        string? normalized = null;
        string? hash = null;
        if (!string.IsNullOrWhiteSpace(textData))
        {
            normalized = QueryNormalizer.Normalize(textData);
            hash = QueryNormalizer.ComputeHash(normalized);
        }

        // Build fields dictionary
        var customFields = new Dictionary<string, object?>(rawEvent.Fields.Count + rawEvent.Actions.Count);
        foreach (var kvp in rawEvent.Fields)
        {
            customFields[kvp.Key] = kvp.Value;
        }
        foreach (var kvp in rawEvent.Actions)
        {
            customFields[kvp.Key] = kvp.Value;
        }

        return new TraceEvent
        {
            Id = id,
            Timestamp = rawEvent.Timestamp,
            Name = name,
            EventType = eventType,
            DurationMicroseconds = durationUs,
            CpuMicroseconds = cpuUs,
            LogicalReads = logicalReads,
            PhysicalReads = physicalReads,
            Writes = writes,
            RowCount = rowCount,
            TextData = textData,
            NormalizedText = normalized,
            QueryHash = hash,
            DatabaseName = dbName,
            DatabaseId = dbId,
            Spid = spid,
            ClientAppName = appName,
            ClientHostName = hostName,
            NtUserName = ntUser,
            TransactionId = txId,
            ErrorNumber = errorNum,
            Severity = severity,
            State = state,
            ErrorMessage = errorMessage,
            DeadlockXml = deadlockXml,
            Fields = customFields
        };
    }

    private static TraceEventType MapEventType(string name)
    {
        return name switch
        {
            "sql_batch_completed" => TraceEventType.SqlBatchCompleted,
            "rpc_completed" => TraceEventType.RpcCompleted,
            "error_reported" => TraceEventType.ErrorReported,
            "xml_deadlock_report" or "database_xml_deadlock_report" => TraceEventType.Deadlock,
            "attention" => TraceEventType.Attention,
            _ => TraceEventType.Other
        };
    }

    private static string? GetString(IXEvent evt, string key)
    {
        if (evt.Fields.TryGetValue(key, out var val) && val != null)
            return val.ToString();

        if (evt.Actions.TryGetValue(key, out val) && val != null)
            return val.ToString();

        return null;
    }

    private static long GetLong(IXEvent evt, string key)
    {
        if (evt.Fields.TryGetValue(key, out var val) && val != null)
        {
            if (val is long l) return l;
            if (val is int i) return i;
            if (val is ulong ul) return (long)ul;
            if (long.TryParse(val.ToString(), out long parsed)) return parsed;
        }

        if (evt.Actions.TryGetValue(key, out val) && val != null)
        {
            if (val is long l) return l;
            if (val is int i) return i;
            if (long.TryParse(val.ToString(), out long parsed)) return parsed;
        }

        return 0;
    }

    private static long? GetNullableLong(IXEvent evt, string key)
    {
        if (evt.Fields.TryGetValue(key, out var val) && val != null)
        {
            if (val is long l) return l;
            if (val is int i) return i;
            if (long.TryParse(val.ToString(), out long parsed)) return parsed;
        }

        if (evt.Actions.TryGetValue(key, out val) && val != null)
        {
            if (val is long l) return l;
            if (val is int i) return i;
            if (long.TryParse(val.ToString(), out long parsed)) return parsed;
        }

        return null;
    }

    private static int? GetInt(IXEvent evt, string key)
    {
        if (evt.Fields.TryGetValue(key, out var val) && val != null)
        {
            if (val is int i) return i;
            if (val is short s) return s;
            if (val is byte b) return b;
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
        }

        if (evt.Actions.TryGetValue(key, out val) && val != null)
        {
            if (val is int i) return i;
            if (val is short s) return s;
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
        }

        return null;
    }
}
