using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler.Engine;

/// <summary>
/// Manages SQL Server Extended Event session lifecycle (test permissions, create, start, stop, drop).
/// </summary>
public sealed class XEventSessionManager : IAsyncDisposable
{
    private string? _activeConnectionString;
    private string? _activeSessionName;

    public string? ActiveSessionName => _activeSessionName;
    public bool IsSessionActive => !string.IsNullOrEmpty(_activeSessionName);

    /// <summary>
    /// Checks whether the current SQL connection has permission to create and manage Extended Event sessions.
    /// </summary>
    public static async Task<(bool HasPermission, string? ErrorDetail)> CheckPermissionsAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Check for ALTER ANY EVENT SESSION or VIEW SERVER STATE / VIEW SERVER PERFORMANCE STATE
            const string sql = @"
                SELECT 
                    HAS_PERMS_BY_NAME(NULL, 'SERVER', 'ALTER ANY EVENT SESSION') AS HasAlterEventSession,
                    IS_SRVROLEMEMBER('sysadmin') AS IsSysAdmin;
            ";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 10;
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                int hasAlter = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                int isSysAdmin = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

                if (hasAlter == 1 || isSysAdmin == 1)
                {
                    return (true, null);
                }
            }

            return (false, "Your SQL login lacks 'ALTER ANY EVENT SESSION' permission required to create Extended Event sessions.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<System.Collections.Generic.List<string>> GetOnlineDatabasesAsync(string connectionString, CancellationToken ct = default)
    {
        var databases = new System.Collections.Generic.List<string>();
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            const string sql = "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 15;
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                databases.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Fallback empty list if fails
        }
        return databases;
    }

    public async Task StartSessionAsync(string connectionString, TraceSessionConfig config, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // 1. Generate & Execute Create Script
        string createSql = XEventScriptGenerator.GenerateCreateSessionScript(config);
        await using (var cmd = new SqlCommand(createSql, conn))
        {
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Start the Session
        string startSql = XEventScriptGenerator.GenerateStartSessionScript(config.SessionName);
        await using (var cmd = new SqlCommand(startSql, conn))
        {
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _activeConnectionString = connectionString;
        _activeSessionName = config.SessionName;
    }

    public async Task StopAndDropSessionAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_activeConnectionString) || string.IsNullOrEmpty(_activeSessionName))
            return;

        string connStr = _activeConnectionString;
        string sessName = _activeSessionName;
        _activeSessionName = null;

        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            // Stop session
            try
            {
                string stopSql = XEventScriptGenerator.GenerateStopSessionScript(sessName);
                await using var cmdStop = new SqlCommand(stopSql, conn);
                cmdStop.CommandTimeout = 15;
                await cmdStop.ExecuteNonQueryAsync(ct);
            }
            catch
            {
                // Ignore if already stopped
            }

            // Drop session
            try
            {
                string dropSql = XEventScriptGenerator.GenerateDropSessionScript(sessName);
                await using var cmdDrop = new SqlCommand(dropSql, conn);
                cmdDrop.CommandTimeout = 15;
                await cmdDrop.ExecuteNonQueryAsync(ct);
            }
            catch
            {
                // Ignore if already dropped
            }
        }
        catch
        {
            // Suppress teardown errors
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAndDropSessionAsync();
    }
}
