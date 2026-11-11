using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler.Engine;

/// <summary>
/// Manages SQL Server Extended Event session lifecycle (test permissions, create, start, stop, drop).
/// Supports both Server-level and Database-scoped sessions.
/// </summary>
public sealed class XEventSessionManager : IAsyncDisposable
{
    private string? _activeConnectionString;
    private string? _activeSessionName;
    private bool _isDatabaseScope;

    public string? ActiveSessionName => _activeSessionName;
    public bool IsSessionActive => !string.IsNullOrEmpty(_activeSessionName);
    public bool IsDatabaseScope => _isDatabaseScope;

    public static string BuildDatabaseConnectionString(string baseConnStr, string database)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(baseConnStr)
            {
                InitialCatalog = database
            };
            return builder.ConnectionString;
        }
        catch
        {
            return baseConnStr;
        }
    }

    /// <summary>
    /// Checks whether the current SQL connection has permission to create and manage Extended Event sessions.
    /// Prefers Database-Scoped sessions if a target database is specified and supported.
    /// </summary>
    public static async Task<(bool HasPermission, bool IsDatabaseScope, string? ErrorDetail)> CheckPermissionsAsync(
        string connectionString, 
        string? targetDatabase, 
        CancellationToken ct = default)
    {
        // 1. If targetDatabase is specified, first check Database-Scoped Extended Events permissions directly on that database
        if (!string.IsNullOrWhiteSpace(targetDatabase) && !string.Equals(targetDatabase, "master", StringComparison.OrdinalIgnoreCase))
        {
            string dbConnStr = BuildDatabaseConnectionString(connectionString, targetDatabase);
            try
            {
                await using var dbConn = new SqlConnection(dbConnStr);
                await dbConn.OpenAsync(ct);

                const string dbCheckSql = @"
                    SELECT 
                        CASE WHEN OBJECT_ID('sys.database_event_sessions') IS NOT NULL THEN 1 ELSE 0 END AS SupportsDbScope,
                        COALESCE(HAS_PERMS_BY_NAME(NULL, 'DATABASE', 'ALTER ANY DATABASE EVENT SESSION'), 0) AS HasDbEventPerm,
                        COALESCE(IS_MEMBER('db_owner'), 0) AS IsDbOwner,
                        COALESCE(HAS_PERMS_BY_NAME(NULL, 'SERVER', 'ALTER ANY EVENT SESSION'), 0) AS HasServerEventPerm,
                        COALESCE(IS_SRVROLEMEMBER('sysadmin'), 0) AS IsSysAdmin;
                ";

                await using var cmd = new SqlCommand(dbCheckSql, dbConn);
                cmd.CommandTimeout = 10;
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                if (await reader.ReadAsync(ct))
                {
                    int supportsDbScope = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    int hasDbPerm = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    int isDbOwner = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    int hasServerPerm = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                    int isSysAdmin = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

                    // If database scope is supported and user has DB-level permission (or is db_owner / sysadmin)
                    if (supportsDbScope == 1 && (hasDbPerm == 1 || isDbOwner == 1 || isSysAdmin == 1))
                    {
                        return (true, true, null);
                    }

                    // If database scope is not supported (older SQL Server), but user has server-level permission
                    if (hasServerPerm == 1 || isSysAdmin == 1)
                    {
                        return (true, false, null);
                    }
                }
            }
            catch
            {
                // If connecting directly to targetDatabase failed, fall through to checking server-level master connection
            }
        }

        // 2. Check server-level permissions (e.g. against master or default connection)
        try
        {
            string serverConnStr = BuildDatabaseConnectionString(connectionString, "master");
            await using var srvConn = new SqlConnection(serverConnStr);
            await srvConn.OpenAsync(ct);

            const string srvCheckSql = @"
                SELECT 
                    COALESCE(HAS_PERMS_BY_NAME(NULL, 'SERVER', 'ALTER ANY EVENT SESSION'), 0) AS HasServerEventPerm,
                    COALESCE(IS_SRVROLEMEMBER('sysadmin'), 0) AS IsSysAdmin;
            ";

            await using var cmd = new SqlCommand(srvCheckSql, srvConn);
            cmd.CommandTimeout = 10;
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                int hasServerPerm = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                int isSysAdmin = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

                if (hasServerPerm == 1 || isSysAdmin == 1)
                {
                    return (true, false, null);
                }
            }
        }
        catch
        {
            // If master is unreachable (e.g. Azure SQL DB), continue to friendly error
        }

        string dbNamePart = !string.IsNullOrWhiteSpace(targetDatabase) ? $" on database '{targetDatabase}'" : string.Empty;
        return (false, false, $"Your SQL login lacks permissions to create Extended Event sessions. It needs either 'ALTER ANY DATABASE EVENT SESSION' (or db_owner role){dbNamePart} or server-level 'ALTER ANY EVENT SESSION'.");
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

    public async Task StartSessionAsync(string connectionString, TraceSessionConfig config, bool isDatabaseScope, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // 1. Generate & Execute Create Script
        string createSql = XEventScriptGenerator.GenerateCreateSessionScript(config, isDatabaseScope);
        await using (var cmd = new SqlCommand(createSql, conn))
        {
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Start the Session
        string startSql = XEventScriptGenerator.GenerateStartSessionScript(config.SessionName, isDatabaseScope);
        await using (var cmd = new SqlCommand(startSql, conn))
        {
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _activeConnectionString = connectionString;
        _activeSessionName = config.SessionName;
        _isDatabaseScope = isDatabaseScope;
    }

    public async Task StopAndDropSessionAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_activeConnectionString) || string.IsNullOrEmpty(_activeSessionName))
            return;

        string connStr = _activeConnectionString;
        string sessName = _activeSessionName;
        bool isDbScope = _isDatabaseScope;
        _activeSessionName = null;

        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            // Stop session
            try
            {
                string stopSql = XEventScriptGenerator.GenerateStopSessionScript(sessName, isDbScope);
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
                string dropSql = XEventScriptGenerator.GenerateDropSessionScript(sessName, isDbScope);
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
