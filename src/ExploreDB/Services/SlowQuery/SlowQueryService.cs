using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace ExploreDB.Services.SlowQuery;

public class SlowQueryService
{
    private readonly ILogger<SlowQueryService> _logger;

    public SlowQueryService(ILogger<SlowQueryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks the Query Store state on the target database.
    /// </summary>
    public async Task<QueryStoreState> CheckQueryStoreStatusAsync(string connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return QueryStoreState.NotSupported;

        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            const string sql = "SELECT actual_state FROM sys.database_query_store_options;";
            using var cmd = new SqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync(ct);

            if (result != null && result != DBNull.Value)
            {
                int state = Convert.ToInt32(result);
                return state switch
                {
                    1 => QueryStoreState.ReadOnly,
                    2 => QueryStoreState.Enabled,
                    _ => QueryStoreState.Disabled
                };
            }

            return QueryStoreState.Disabled;
        }
        catch (SqlException ex)
        {
            // Object doesn't exist (SQL Server < 2016) or insufficient permissions
            _logger.LogInformation(ex, "Query Store options view not accessible or unsupported.");
            return QueryStoreState.NotSupported;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Query Store status.");
            return QueryStoreState.NotSupported;
        }
    }

    /// <summary>
    /// Loads the top resource-consuming queries using Query Store or Live Plan Cache DMVs.
    /// </summary>
    public async Task<SlowQueryResult> GetTopQueriesAsync(
        string connectionString,
        SlowQueryFilterOptions options,
        bool forceDmv = false,
        CancellationToken ct = default)
    {
        var result = new SlowQueryResult();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            result.ErrorMessage = "No active database connection.";
            return result;
        }

        try
        {
            QueryStoreState storeState = await CheckQueryStoreStatusAsync(connectionString, ct);
            result.StoreState = storeState;

            bool canUseQueryStore = storeState == QueryStoreState.Enabled || storeState == QueryStoreState.ReadOnly;
            bool useQueryStore = !forceDmv && canUseQueryStore;
            result.IsUsingQueryStore = useQueryStore;

            if (useQueryStore)
            {
                result.Queries = await QueryStoreTopQueriesAsync(connectionString, options, ct);
            }
            else
            {
                result.Queries = await DmvTopQueriesAsync(connectionString, options, ct);
            }

            // Calculate relative impact percentage based on the #1 query
            CalculateImpactPercentages(result.Queries, options.Metric);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing top queries analysis.");
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<List<SlowQueryItem>> QueryStoreTopQueriesAsync(
        string connectionString,
        SlowQueryFilterOptions options,
        CancellationToken ct)
    {
        var items = new List<SlowQueryItem>();
        var (startTime, endTime) = options.GetResolvedTimeRange();
        string orderByClause = GetOrderByClause(options.Metric, isQueryStore: true);

        string searchFilter = "";
        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            searchFilter = "AND (qt.query_sql_text LIKE @Search OR OBJECT_NAME(q.object_id) LIKE @Search)";
        }

        string sql = $@"
SELECT TOP (@TopCount)
    q.query_id AS QueryId,
    qt.query_sql_text AS StatementText,
    OBJECT_SCHEMA_NAME(q.object_id) AS ObjectSchema,
    OBJECT_NAME(q.object_id) AS ObjectName,
    COUNT(DISTINCT p.plan_id) AS PlanCount,
    SUM(rs.count_executions) AS ExecutionCount,
    -- Duration in milliseconds
    SUM(rs.avg_duration * rs.count_executions) / 1000.0 AS TotalDurationMs,
    (SUM(rs.avg_duration * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0)) / 1000.0 AS AvgDurationMs,
    MIN(rs.min_duration) / 1000.0 AS MinDurationMs,
    MAX(rs.max_duration) / 1000.0 AS MaxDurationMs,
    -- CPU in milliseconds
    SUM(rs.avg_cpu_time * rs.count_executions) / 1000.0 AS TotalCpuMs,
    (SUM(rs.avg_cpu_time * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0)) / 1000.0 AS AvgCpuMs,
    MIN(rs.min_cpu_time) / 1000.0 AS MinCpuMs,
    MAX(rs.max_cpu_time) / 1000.0 AS MaxCpuMs,
    -- Physical Reads
    SUM(rs.avg_physical_io_reads * rs.count_executions) AS TotalPhysicalReads,
    SUM(rs.avg_physical_io_reads * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0) AS AvgPhysicalReads,
    -- Logical Reads
    SUM(rs.avg_logical_io_reads * rs.count_executions) AS TotalLogicalReads,
    SUM(rs.avg_logical_io_reads * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0) AS AvgLogicalReads,
    -- Logical Writes
    SUM(rs.avg_logical_io_writes * rs.count_executions) AS TotalLogicalWrites,
    SUM(rs.avg_logical_io_writes * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0) AS AvgLogicalWrites,
    -- Rows
    SUM(rs.avg_rowcount * rs.count_executions) AS TotalRows,
    SUM(rs.avg_rowcount * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0) AS AvgRows,
    MIN(rsi.start_time) AS FirstSeen,
    MAX(rsi.end_time) AS LastSeen
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan p ON q.query_id = p.query_id
JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
JOIN sys.query_store_runtime_stats_interval rsi ON rs.runtime_stats_interval_id = rsi.runtime_stats_interval_id
WHERE rsi.start_time >= @StartTime AND rsi.end_time <= @EndTime
{searchFilter}
GROUP BY q.query_id, qt.query_sql_text, q.object_id
ORDER BY {orderByClause};";

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 45;
        cmd.Parameters.AddWithValue("@TopCount", options.TopCount);
        cmd.Parameters.AddWithValue("@StartTime", new DateTimeOffset(startTime, TimeSpan.Zero));
        cmd.Parameters.AddWithValue("@EndTime", new DateTimeOffset(endTime, TimeSpan.Zero));
        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            cmd.Parameters.AddWithValue("@Search", $"%{options.SearchTerm.Trim()}%");
        }

        using var reader = await cmd.ExecuteReaderAsync(ct);
        int rank = 1;
        while (await reader.ReadAsync(ct))
        {
            var item = new SlowQueryItem
            {
                Rank = rank++,
                QueryId = reader.GetInt64(reader.GetOrdinal("QueryId")),
                StatementText = reader.GetString(reader.GetOrdinal("StatementText")),
                ObjectSchema = reader.IsDBNull(reader.GetOrdinal("ObjectSchema")) ? null : reader.GetString(reader.GetOrdinal("ObjectSchema")),
                ObjectName = reader.IsDBNull(reader.GetOrdinal("ObjectName")) ? null : reader.GetString(reader.GetOrdinal("ObjectName")),
                PlanCount = reader.GetInt32(reader.GetOrdinal("PlanCount")),
                ExecutionCount = reader.IsDBNull(reader.GetOrdinal("ExecutionCount")) ? 0 : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("ExecutionCount"))),
                TotalDurationMs = reader.IsDBNull(reader.GetOrdinal("TotalDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("TotalDurationMs"))),
                AvgDurationMs = reader.IsDBNull(reader.GetOrdinal("AvgDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgDurationMs"))),
                MinDurationMs = reader.IsDBNull(reader.GetOrdinal("MinDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MinDurationMs"))),
                MaxDurationMs = reader.IsDBNull(reader.GetOrdinal("MaxDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MaxDurationMs"))),
                TotalCpuMs = reader.IsDBNull(reader.GetOrdinal("TotalCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("TotalCpuMs"))),
                AvgCpuMs = reader.IsDBNull(reader.GetOrdinal("AvgCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgCpuMs"))),
                MinCpuMs = reader.IsDBNull(reader.GetOrdinal("MinCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MinCpuMs"))),
                MaxCpuMs = reader.IsDBNull(reader.GetOrdinal("MaxCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MaxCpuMs"))),
                TotalPhysicalReads = reader.IsDBNull(reader.GetOrdinal("TotalPhysicalReads")) ? 0 : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("TotalPhysicalReads"))),
                AvgPhysicalReads = reader.IsDBNull(reader.GetOrdinal("AvgPhysicalReads")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgPhysicalReads"))),
                TotalLogicalReads = reader.IsDBNull(reader.GetOrdinal("TotalLogicalReads")) ? 0 : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("TotalLogicalReads"))),
                AvgLogicalReads = reader.IsDBNull(reader.GetOrdinal("AvgLogicalReads")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgLogicalReads"))),
                TotalLogicalWrites = reader.IsDBNull(reader.GetOrdinal("TotalLogicalWrites")) ? 0 : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("TotalLogicalWrites"))),
                AvgLogicalWrites = reader.IsDBNull(reader.GetOrdinal("AvgLogicalWrites")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgLogicalWrites"))),
                TotalRows = reader.IsDBNull(reader.GetOrdinal("TotalRows")) ? 0 : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("TotalRows"))),
                AvgRows = reader.IsDBNull(reader.GetOrdinal("AvgRows")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgRows"))),
                FirstSeen = ReadDateTimeSafe(reader, "FirstSeen"),
                LastExecutionTime = ReadDateTimeSafe(reader, "LastSeen")
            };

            item.FullBatchText = item.StatementText;
            items.Add(item);
        }

        return items;
    }

    private async Task<List<SlowQueryItem>> DmvTopQueriesAsync(
        string connectionString,
        SlowQueryFilterOptions options,
        CancellationToken ct)
    {
        var items = new List<SlowQueryItem>();
        string orderByClause = GetOrderByClause(options.Metric, isQueryStore: false);

        string searchFilter = "";
        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            searchFilter = "AND (st.text LIKE @Search OR OBJECT_NAME(st.objectid, st.dbid) LIKE @Search)";
        }

        string sql = $@"
SELECT TOP (@TopCount)
    CONVERT(varchar(64), qs.sql_handle, 1) AS SqlHandleHex,
    CONVERT(varchar(64), qs.plan_handle, 1) AS PlanHandleHex,
    qs.statement_start_offset AS StatementStartOffset,
    qs.statement_end_offset AS StatementEndOffset,
    qs.execution_count AS ExecutionCount,
    -- Duration in milliseconds
    qs.total_elapsed_time / 1000.0 AS TotalDurationMs,
    (qs.total_elapsed_time / 1000.0) / NULLIF(qs.execution_count, 0) AS AvgDurationMs,
    qs.min_elapsed_time / 1000.0 AS MinDurationMs,
    qs.max_elapsed_time / 1000.0 AS MaxDurationMs,
    -- CPU in milliseconds
    qs.total_worker_time / 1000.0 AS TotalCpuMs,
    (qs.total_worker_time / 1000.0) / NULLIF(qs.execution_count, 0) AS AvgCpuMs,
    qs.min_worker_time / 1000.0 AS MinCpuMs,
    qs.max_worker_time / 1000.0 AS MaxCpuMs,
    -- I/O
    qs.total_physical_reads AS TotalPhysicalReads,
    qs.total_physical_reads / NULLIF(qs.execution_count, 0) AS AvgPhysicalReads,
    qs.total_logical_reads AS TotalLogicalReads,
    qs.total_logical_reads / NULLIF(qs.execution_count, 0) AS AvgLogicalReads,
    qs.total_logical_writes AS TotalLogicalWrites,
    qs.total_logical_writes / NULLIF(qs.execution_count, 0) AS AvgLogicalWrites,
    -- Rows
    qs.total_rows AS TotalRows,
    qs.total_rows / NULLIF(qs.execution_count, 0) AS AvgRows,
    qs.creation_time AS CreationTime,
    qs.last_execution_time AS LastExecutionTime,
    SUBSTRING(
        st.text,
        (qs.statement_start_offset / 2) + 1,
        ((CASE qs.statement_end_offset
            WHEN -1 THEN DATALENGTH(st.text)
            ELSE qs.statement_end_offset
          END - qs.statement_start_offset) / 2) + 1
    ) AS StatementText,
    st.text AS FullBatchText,
    OBJECT_SCHEMA_NAME(st.objectid, st.dbid) AS ObjectSchema,
    OBJECT_NAME(st.objectid, st.dbid) AS ObjectName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_plan_attributes(qs.plan_handle) pa
WHERE pa.attribute = 'dbid'
  AND (CAST(pa.value AS int) = DB_ID() OR st.dbid = DB_ID())
{searchFilter}
ORDER BY {orderByClause};";

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 45;
        cmd.Parameters.AddWithValue("@TopCount", options.TopCount);
        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            cmd.Parameters.AddWithValue("@Search", $"%{options.SearchTerm.Trim()}%");
        }

        using var reader = await cmd.ExecuteReaderAsync(ct);
        int rank = 1;
        while (await reader.ReadAsync(ct))
        {
            string planHex = reader.IsDBNull(reader.GetOrdinal("PlanHandleHex")) ? "" : reader.GetString(reader.GetOrdinal("PlanHandleHex"));
            string stmt = reader.IsDBNull(reader.GetOrdinal("StatementText")) ? "" : reader.GetString(reader.GetOrdinal("StatementText"));
            string fullBatch = reader.IsDBNull(reader.GetOrdinal("FullBatchText")) ? "" : reader.GetString(reader.GetOrdinal("FullBatchText"));

            var item = new SlowQueryItem
            {
                Rank = rank++,
                QueryHash = planHex,
                StatementText = string.IsNullOrWhiteSpace(stmt) ? fullBatch : stmt,
                FullBatchText = fullBatch,
                ObjectSchema = reader.IsDBNull(reader.GetOrdinal("ObjectSchema")) ? null : reader.GetString(reader.GetOrdinal("ObjectSchema")),
                ObjectName = reader.IsDBNull(reader.GetOrdinal("ObjectName")) ? null : reader.GetString(reader.GetOrdinal("ObjectName")),
                PlanCount = 1,
                ExecutionCount = reader.IsDBNull(reader.GetOrdinal("ExecutionCount")) ? 0 : reader.GetInt64(reader.GetOrdinal("ExecutionCount")),
                TotalDurationMs = reader.IsDBNull(reader.GetOrdinal("TotalDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("TotalDurationMs"))),
                AvgDurationMs = reader.IsDBNull(reader.GetOrdinal("AvgDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgDurationMs"))),
                MinDurationMs = reader.IsDBNull(reader.GetOrdinal("MinDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MinDurationMs"))),
                MaxDurationMs = reader.IsDBNull(reader.GetOrdinal("MaxDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MaxDurationMs"))),
                TotalCpuMs = reader.IsDBNull(reader.GetOrdinal("TotalCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("TotalCpuMs"))),
                AvgCpuMs = reader.IsDBNull(reader.GetOrdinal("AvgCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgCpuMs"))),
                MinCpuMs = reader.IsDBNull(reader.GetOrdinal("MinCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MinCpuMs"))),
                MaxCpuMs = reader.IsDBNull(reader.GetOrdinal("MaxCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("MaxCpuMs"))),
                TotalPhysicalReads = reader.IsDBNull(reader.GetOrdinal("TotalPhysicalReads")) ? 0 : reader.GetInt64(reader.GetOrdinal("TotalPhysicalReads")),
                AvgPhysicalReads = reader.IsDBNull(reader.GetOrdinal("AvgPhysicalReads")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgPhysicalReads"))),
                TotalLogicalReads = reader.IsDBNull(reader.GetOrdinal("TotalLogicalReads")) ? 0 : reader.GetInt64(reader.GetOrdinal("TotalLogicalReads")),
                AvgLogicalReads = reader.IsDBNull(reader.GetOrdinal("AvgLogicalReads")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgLogicalReads"))),
                TotalLogicalWrites = reader.IsDBNull(reader.GetOrdinal("TotalLogicalWrites")) ? 0 : reader.GetInt64(reader.GetOrdinal("TotalLogicalWrites")),
                AvgLogicalWrites = reader.IsDBNull(reader.GetOrdinal("AvgLogicalWrites")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgLogicalWrites"))),
                TotalRows = reader.IsDBNull(reader.GetOrdinal("TotalRows")) ? 0 : reader.GetInt64(reader.GetOrdinal("TotalRows")),
                AvgRows = reader.IsDBNull(reader.GetOrdinal("AvgRows")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgRows"))),
                FirstSeen = ReadDateTimeSafe(reader, "CreationTime"),
                LastExecutionTime = ReadDateTimeSafe(reader, "LastExecutionTime")
            };

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Loads execution trends for a Query Store query across runtime stat intervals.
    /// </summary>
    public async Task<List<QueryIntervalTrend>> GetQueryExecutionTrendsAsync(
        string connectionString,
        long queryId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken ct = default)
    {
        var list = new List<QueryIntervalTrend>();
        if (string.IsNullOrWhiteSpace(connectionString) || queryId <= 0) return list;

        try
        {
            const string sql = @"
SELECT 
    rsi.start_time AS IntervalStart,
    rsi.end_time AS IntervalEnd,
    SUM(rs.count_executions) AS Executions,
    (SUM(rs.avg_duration * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0)) / 1000.0 AS AvgDurationMs,
    (SUM(rs.avg_cpu_time * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0)) / 1000.0 AS AvgCpuMs,
    SUM(rs.avg_physical_io_reads * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0) AS AvgPhysicalReads,
    SUM(rs.avg_logical_io_reads * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0) AS AvgLogicalReads
FROM sys.query_store_runtime_stats rs
JOIN sys.query_store_runtime_stats_interval rsi ON rs.runtime_stats_interval_id = rsi.runtime_stats_interval_id
JOIN sys.query_store_plan p ON rs.plan_id = p.plan_id
WHERE p.query_id = @QueryId
  AND rsi.start_time >= @StartTime AND rsi.end_time <= @EndTime
GROUP BY rsi.start_time, rsi.end_time
ORDER BY rsi.start_time ASC;";

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@QueryId", queryId);
            cmd.Parameters.AddWithValue("@StartTime", new DateTimeOffset(startTime, TimeSpan.Zero));
            cmd.Parameters.AddWithValue("@EndTime", new DateTimeOffset(endTime, TimeSpan.Zero));

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new QueryIntervalTrend
                {
                    IntervalStart = ReadDateTimeRequired(reader, "IntervalStart"),
                    IntervalEnd = ReadDateTimeRequired(reader, "IntervalEnd"),
                    Executions = reader.IsDBNull(reader.GetOrdinal("Executions")) ? 0 : Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Executions"))),
                    AvgDurationMs = reader.IsDBNull(reader.GetOrdinal("AvgDurationMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgDurationMs"))),
                    AvgCpuMs = reader.IsDBNull(reader.GetOrdinal("AvgCpuMs")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgCpuMs"))),
                    AvgPhysicalReads = reader.IsDBNull(reader.GetOrdinal("AvgPhysicalReads")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgPhysicalReads"))),
                    AvgLogicalReads = reader.IsDBNull(reader.GetOrdinal("AvgLogicalReads")) ? 0 : Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgLogicalReads")))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load interval trends for query {QueryId}", queryId);
        }

        return list;
    }

    /// <summary>
    /// Fetches the XML execution plan for a specific query item on demand.
    /// </summary>
    public async Task<string?> GetExecutionPlanXmlAsync(
        string connectionString,
        SlowQueryItem item,
        bool isQueryStore,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || item == null) return null;

        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            if (isQueryStore && item.QueryId > 0)
            {
                const string qsPlanSql = @"
SELECT TOP 1 p.query_plan
FROM sys.query_store_plan p
WHERE p.query_id = @QueryId
ORDER BY p.is_forced_plan DESC, p.last_execution_time DESC;";

                using var cmd = new SqlCommand(qsPlanSql, conn);
                cmd.Parameters.AddWithValue("@QueryId", item.QueryId);
                var plan = await cmd.ExecuteScalarAsync(ct);
                return plan?.ToString();
            }
            else if (!string.IsNullOrEmpty(item.QueryHash))
            {
                const string dmvPlanSql = @"
SELECT TOP 1 qp.query_plan
FROM sys.dm_exec_query_plan(CONVERT(varbinary(64), @PlanHandleHex, 1)) qp;";

                using var cmd = new SqlCommand(dmvPlanSql, conn);
                cmd.Parameters.AddWithValue("@PlanHandleHex", item.QueryHash);
                var plan = await cmd.ExecuteScalarAsync(ct);
                return plan?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch execution plan for query.");
        }

        return null;
    }

    private static DateTime? ReadDateTimeSafe(SqlDataReader reader, string columnName)
    {
        int ord = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ord)) return null;

        object val = reader.GetValue(ord);
        if (val is DateTime dt) return dt;
        if (val is DateTimeOffset dto) return dto.LocalDateTime;
        if (DateTime.TryParse(val.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static DateTime ReadDateTimeRequired(SqlDataReader reader, string columnName)
    {
        int ord = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ord)) return DateTime.UtcNow;

        object val = reader.GetValue(ord);
        if (val is DateTime dt) return dt;
        if (val is DateTimeOffset dto) return dto.LocalDateTime;
        if (DateTime.TryParse(val.ToString(), out var parsed)) return parsed;
        return DateTime.UtcNow;
    }

    private static string GetOrderByClause(RankingMetric metric, bool isQueryStore)
    {
        return (metric, isQueryStore) switch
        {
            (RankingMetric.TotalCpu, true) => "TotalCpuMs DESC",
            (RankingMetric.TotalCpu, false) => "qs.total_worker_time DESC",

            (RankingMetric.AvgCpu, true) => "AvgCpuMs DESC",
            (RankingMetric.AvgCpu, false) => "(qs.total_worker_time / NULLIF(qs.execution_count, 0)) DESC",

            (RankingMetric.TotalDuration, true) => "TotalDurationMs DESC",
            (RankingMetric.TotalDuration, false) => "qs.total_elapsed_time DESC",

            (RankingMetric.AvgDuration, true) => "AvgDurationMs DESC",
            (RankingMetric.AvgDuration, false) => "(qs.total_elapsed_time / NULLIF(qs.execution_count, 0)) DESC",

            (RankingMetric.PhysicalReads, true) => "TotalPhysicalReads DESC",
            (RankingMetric.PhysicalReads, false) => "qs.total_physical_reads DESC",

            (RankingMetric.LogicalReads, true) => "TotalLogicalReads DESC",
            (RankingMetric.LogicalReads, false) => "qs.total_logical_reads DESC",

            (RankingMetric.LogicalWrites, true) => "TotalLogicalWrites DESC",
            (RankingMetric.LogicalWrites, false) => "qs.total_logical_writes DESC",

            (RankingMetric.ExecutionCount, true) => "ExecutionCount DESC",
            (RankingMetric.ExecutionCount, false) => "qs.execution_count DESC",

            _ => isQueryStore ? "TotalCpuMs DESC" : "qs.total_worker_time DESC"
        };
    }

    private static void CalculateImpactPercentages(List<SlowQueryItem> queries, RankingMetric metric)
    {
        if (queries == null || queries.Count == 0) return;

        double maxVal = queries.Max(q => GetMetricValue(q, metric));
        if (maxVal <= 0)
        {
            foreach (var q in queries) q.ImpactPercentage = 0;
            return;
        }

        foreach (var q in queries)
        {
            double val = GetMetricValue(q, metric);
            q.ImpactPercentage = Math.Clamp((val / maxVal) * 100.0, 0, 100);
        }
    }

    private static double GetMetricValue(SlowQueryItem item, RankingMetric metric)
    {
        return metric switch
        {
            RankingMetric.TotalCpu => item.TotalCpuMs,
            RankingMetric.AvgCpu => item.AvgCpuMs,
            RankingMetric.TotalDuration => item.TotalDurationMs,
            RankingMetric.AvgDuration => item.AvgDurationMs,
            RankingMetric.PhysicalReads => item.TotalPhysicalReads,
            RankingMetric.LogicalReads => item.TotalLogicalReads,
            RankingMetric.LogicalWrites => item.TotalLogicalWrites,
            RankingMetric.ExecutionCount => item.ExecutionCount,
            _ => item.TotalCpuMs
        };
    }
}
