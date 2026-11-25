using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace ExploreDB.Services;

public sealed class QueryExecutionRequest
{
    public required QueryTab Tab { get; init; }
    public required string QueryText { get; init; }
    public string FullScript { get; init; } = string.Empty;
    public int LineOffset { get; init; }
    public required Func<Func<Task>, Task> InvokeOnUiAsync { get; init; }
    public Action<string>? SetActiveResultTab { get; init; }
    public Action? NotifyUi { get; init; }
    public Func<Task<bool>>? ConfirmDangerousQueryAsync { get; init; }
    public bool SaveHistory { get; init; } = true;
    public bool IsPopout { get; init; } = false;
}

public sealed class QueryEditorInput
{
    public string FullText { get; set; } = string.Empty;
    public string SelectedText { get; set; } = string.Empty;
    public int StartLineNumber { get; set; } = 1;
}

public class QueryExecutionService
{
    private static readonly Regex FromTableRegex = new(
        @"\bFROM\s+([\[\]a-zA-Z0-9_\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LineNumberRegex = new(
        @"\bLine\s+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IConfiguration _config;
    private readonly HistoryService _historyService;

    public QueryExecutionService(IConfiguration config, HistoryService historyService)
    {
        _config = config;
        _historyService = historyService;
    }

    public async Task ExecuteAsync(QueryExecutionRequest request)
    {
        var tab = request.Tab;
        if (tab.IsExecuting) return;

        if (string.IsNullOrWhiteSpace(request.QueryText))
        {
            tab.AddMessage(new QueryOutputMessage("Error: Query is empty.", isError: true));
            request.SetActiveResultTab?.Invoke("Messages");
            request.NotifyUi?.Invoke();
            return;
        }

        if (string.IsNullOrEmpty(tab.ConnectionString))
        {
            tab.HasExecuted = true;
            tab.ResultSets.Clear();
            tab.ClearMessages();
            tab.AddMessage(new QueryOutputMessage("Error: Tab is disconnected. Please connect to a server using the plug icon in the top right.", isError: true));
            request.SetActiveResultTab?.Invoke("Messages");
            request.NotifyUi?.Invoke();
            return;
        }

        string effectiveConnStr = ConnectionService.EnsureOptimalConnectionString(tab.ConnectionString, out string catalog);
        if (string.IsNullOrEmpty(catalog))
        {
            tab.HasExecuted = true;
            tab.ResultSets.Clear();
            tab.ClearMessages();
            tab.AddMessage(new QueryOutputMessage("Error: No database selected. Please select a database from the sidebar before executing.", isError: true));
            request.SetActiveResultTab?.Invoke("Messages");
            request.NotifyUi?.Invoke();
            return;
        }

        bool isReadOnly = IsReadOnlyQuery(request.QueryText);
        tab.IsCurrentQuerySelect = isReadOnly;

        if (!isReadOnly && request.ConfirmDangerousQueryAsync != null)
        {
            bool allowNonSelect = _config.GetValue<bool>("AllowNonSelectQueries", false);
            if (!allowNonSelect)
            {
                tab.HasExecuted = true;
                tab.ResultSets.Clear();
                tab.ClearMessages();
                tab.AddMessage(new QueryOutputMessage("Error: Execution of non-SELECT statements is disabled.", isError: true));
                request.SetActiveResultTab?.Invoke("Messages");
                request.NotifyUi?.Invoke();
                return;
            }

            bool confirm = await request.ConfirmDangerousQueryAsync();
            if (!confirm)
            {
                tab.HasExecuted = true;
                tab.ResultSets.Clear();
                tab.ClearMessages();
                tab.AddMessage("Execution cancelled by user.");
                request.SetActiveResultTab?.Invoke("Messages");
                request.NotifyUi?.Invoke();
                return;
            }
        }

        var cts = new CancellationTokenSource();
        tab.IsExecuting = true;
        tab.IsCancelling = false;
        tab.HasExecuted = true;
        tab.ExecElapsedSeconds = 0;
        tab.ExecutionStartedUtc = DateTime.UtcNow;
        tab.QueryCts = cts;
        tab.ResultSets.Clear();
        tab.ClearMessages();

        request.SetActiveResultTab?.Invoke("Results");
        request.NotifyUi?.Invoke();

        var connWatch = new Stopwatch();
        var queryWatch = new Stopwatch();
        bool isSuccess = false;
        string? errorMessage = null;
        string dbName = catalog;
        string serverName = string.Empty;
        string finalResultTab = "Results";
        var pendingMessages = new ConcurrentQueue<QueryOutputMessage>();

        string? fallbackTableName = null;
        var fromMatch = FromTableRegex.Match(request.QueryText);
        if (fromMatch.Success)
        {
            fallbackTableName = fromMatch.Groups[1].Value.Trim();
        }

        // Run connection, execution, and synchronous TDS draining entirely off the Blazor UI SynchronizationContext.
        await Task.Run(async () =>
        {
            QueryResultTable? currentStreamingTable = null;
            bool currentTableAddedToTab = false;
            var ct = cts.Token;
            long lastMessageFlushTicks = Environment.TickCount64;
            int msgFlushScheduled = 0;

            void FlushPendingMessagesToTab()
            {
                if (pendingMessages.IsEmpty) return;
                var drained = new List<QueryOutputMessage>();
                while (pendingMessages.TryDequeue(out var msg))
                {
                    drained.Add(msg);
                }
                if (drained.Count > 0)
                {
                    tab.AddMessages(drained);
                }
            }

            void ScheduleMessageFlush()
            {
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastMessageFlushTicks) < 120) return;
                if (Interlocked.CompareExchange(ref msgFlushScheduled, 1, 0) != 0) return;

                _ = request.InvokeOnUiAsync(() =>
                {
                    Interlocked.Exchange(ref lastMessageFlushTicks, Environment.TickCount64);
                    Interlocked.Exchange(ref msgFlushScheduled, 0);
                    FlushPendingMessagesToTab();
                    request.NotifyUi?.Invoke();
                    return Task.CompletedTask;
                });
            }

            try
            {
                connWatch.Start();
                using var conn = new SqlConnection(effectiveConnStr);
                dbName = conn.Database;
                serverName = conn.DataSource;

                conn.InfoMessage += (_, args) =>
                {
                    foreach (SqlError err in args.Errors)
                    {
                        if (err.Class > 10)
                        {
                            var (hMsg, nLine) = FormatSqlErrorHeader(err, request.LineOffset);
                            pendingMessages.Enqueue(new QueryOutputMessage(hMsg, isError: true, lineNumber: nLine));
                            pendingMessages.Enqueue(new QueryOutputMessage(err.Message, isError: true, lineNumber: nLine));
                        }
                        else
                        {
                            pendingMessages.Enqueue(new QueryOutputMessage(err.Message, isError: false));
                        }
                    }
                    if (args.Errors.Count == 0 && !string.IsNullOrEmpty(args.Message))
                    {
                        pendingMessages.Enqueue(new QueryOutputMessage(args.Message, isError: false));
                    }
                    ScheduleMessageFlush();
                };

                tab.CancelAction = () => { try { conn.Close(); } catch { } };
                await conn.OpenAsync(ct).ConfigureAwait(false);
                connWatch.Stop();

                queryWatch.Start();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = request.QueryText;
                cmd.CommandTimeout = 0;

                tab.CancelAction = () =>
                {
                    try { cmd.Cancel(); } catch { }
                    try { conn.Close(); } catch { }
                };

                using var ctr = ct.Register(() =>
                {
                    try { cmd.Cancel(); } catch { }
                    try { conn.Close(); } catch { }
                });

                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
                int affectedStatementCount = 0;
                var flushWatch = Stopwatch.StartNew();

                do
                {
                    try
                    {
                        int fieldCount = reader.FieldCount;
                        if (fieldCount > 0)
                        {
                            var columnNames = new string[fieldCount];
                            var columnTypes = new string[fieldCount];
                            var fieldTypes = new Type?[fieldCount];

                            for (int i = 0; i < fieldCount; i++)
                            {
                                var name = reader.GetName(i);
                                columnNames[i] = string.IsNullOrEmpty(name) ? $"Column{i}" : name;
                                try { columnTypes[i] = reader.GetDataTypeName(i); } catch { columnTypes[i] = ""; }
                                try { fieldTypes[i] = reader.GetFieldType(i); } catch { fieldTypes[i] = null; }
                            }

                            var table = new QueryResultTable(columnNames, columnTypes, fieldTypes)
                            {
                                TableName = fallbackTableName
                            };
                            currentStreamingTable = table;
                            currentTableAddedToTab = false;
                            flushWatch.Restart();

                            // Synchronous Read() on ThreadPool avoids per-row ReadAsync Task overhead and dispatcher hops.
                            while (reader.Read())
                            {
                                if (ct.IsCancellationRequested || tab.IsCancelling)
                                {
                                    throw new OperationCanceledException(ct);
                                }

                                int written = table.AppendRowFromReader(reader);

                                if (!currentTableAddedToTab)
                                {
                                    if (written >= 100 || flushWatch.ElapsedMilliseconds >= 40)
                                    {
                                        table.PublishWrittenRows();
                                        currentTableAddedToTab = true;
                                        var addedTable = table;
                                        await request.InvokeOnUiAsync(() =>
                                        {
                                            tab.ResultSets.Add(addedTable);
                                            request.SetActiveResultTab?.Invoke("Results");
                                            request.NotifyUi?.Invoke();
                                            return Task.CompletedTask;
                                        }).ConfigureAwait(false);
                                        flushWatch.Restart();
                                    }
                                }
                                else if ((written & 4095) == 0 || flushWatch.ElapsedMilliseconds >= 180)
                                {
                                    table.PublishWrittenRows();
                                    table.NotifyRowsUpdated();
                                    flushWatch.Restart();
                                }
                            }

                            table.PublishWrittenRows();
                            table.IsComplete = true;
                            currentStreamingTable = null;

                            if (!currentTableAddedToTab)
                            {
                                currentTableAddedToTab = true;
                                var completedTable = table;
                                await request.InvokeOnUiAsync(() =>
                                {
                                    tab.ResultSets.Add(completedTable);
                                    request.SetActiveResultTab?.Invoke("Results");
                                    request.NotifyUi?.Invoke();
                                    return Task.CompletedTask;
                                }).ConfigureAwait(false);
                            }
                            else
                            {
                                table.NotifyRowsUpdated();
                            }

                            int finalRowCount = table.RowCount;
                            var rowWord = finalRowCount == 1 ? "row" : "rows";
                            pendingMessages.Enqueue($"({finalRowCount} {rowWord} returned)");
                            pendingMessages.Enqueue("");
                            affectedStatementCount++;
                        }
                        else
                        {
                            if (reader.RecordsAffected >= 0)
                            {
                                var rowWord = reader.RecordsAffected == 1 ? "row" : "rows";
                                pendingMessages.Enqueue($"({reader.RecordsAffected} {rowWord} affected)");
                                pendingMessages.Enqueue("");
                                affectedStatementCount++;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested && !tab.IsCancelling)
                    {
                        pendingMessages.Enqueue(new QueryOutputMessage($"Error reading result set: {ex.Message}", isError: true));
                        break;
                    }
                }
                while (!ct.IsCancellationRequested && !tab.IsCancelling && reader.NextResult());

                if (ct.IsCancellationRequested || tab.IsCancelling)
                {
                    throw new OperationCanceledException(ct);
                }

                if (affectedStatementCount == 0 && tab.ResultSets.Count == 0 && reader.RecordsAffected >= 0)
                {
                    var rowWord = reader.RecordsAffected == 1 ? "row" : "rows";
                    pendingMessages.Enqueue($"({reader.RecordsAffected} {rowWord} affected)");
                    pendingMessages.Enqueue("");
                }

                pendingMessages.Enqueue("");

                finalResultTab = tab.ResultSets.Count == 0 ? "Messages" : "Results";
                isSuccess = true;
            }
            catch (Exception ex) when (ex is OperationCanceledException || ct.IsCancellationRequested || tab.IsCancelling)
            {
                if (currentStreamingTable != null)
                {
                    currentStreamingTable.PublishWrittenRows();
                    currentStreamingTable.IsComplete = true;
                    if (!currentTableAddedToTab && currentStreamingTable.RowCount > 0)
                    {
                        currentTableAddedToTab = true;
                        var partialTable = currentStreamingTable;
                        await request.InvokeOnUiAsync(() =>
                        {
                            tab.ResultSets.Add(partialTable);
                            return Task.CompletedTask;
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        currentStreamingTable.NotifyRowsUpdated();
                    }
                }

                foreach (var rs in tab.ResultSets.OfType<QueryResultTable>())
                {
                    rs.IsComplete = true;
                }

                int partialTotal = tab.ResultSets.OfType<QueryResultTable>().Sum(t => t.RowCount);
                if (partialTotal > 0)
                {
                    pendingMessages.Enqueue($"({partialTotal:N0} {(partialTotal == 1 ? "row" : "rows")} returned - Execution cancelled by user)");
                    finalResultTab = "Results";
                }
                else
                {
                    pendingMessages.Enqueue(request.IsPopout ? "Query execution was canceled by the user." : "Execution cancelled by user.");
                    finalResultTab = "Messages";
                }
                pendingMessages.Enqueue("");
                errorMessage = "Cancelled";
            }
            catch (SqlException sqlEx)
            {
                finalResultTab = "Messages";
                errorMessage = sqlEx.Message;

                foreach (SqlError err in sqlEx.Errors)
                {
                    var (hMsg, nLine) = FormatSqlErrorHeader(err, request.LineOffset);
                    pendingMessages.Enqueue(new QueryOutputMessage(hMsg, isError: true, lineNumber: nLine));
                    pendingMessages.Enqueue(new QueryOutputMessage(err.Message, isError: true, lineNumber: nLine));
                }
                if (sqlEx.Errors.Count == 0)
                {
                    var (hEx, nExLine) = FormatSqlErrorHeader(sqlEx, request.LineOffset);
                    pendingMessages.Enqueue(new QueryOutputMessage(hEx, isError: true, lineNumber: nExLine));
                    pendingMessages.Enqueue(new QueryOutputMessage(sqlEx.Message, isError: true, lineNumber: nExLine));
                }
                pendingMessages.Enqueue("");
            }
            catch (Exception ex)
            {
                pendingMessages.Enqueue(new QueryOutputMessage($"ERROR: {ex.Message}", isError: true));
                pendingMessages.Enqueue("");
                finalResultTab = "Messages";
                errorMessage = ex.Message;
            }
            finally
            {
                if (connWatch.IsRunning) connWatch.Stop();
                if (queryWatch.IsRunning) queryWatch.Stop();

                long connMs = connWatch.ElapsedMilliseconds;
                long queryMs = queryWatch.ElapsedMilliseconds;
                long totalMs = queryMs > 0 ? (connMs + queryMs) : connMs;

                tab.LastConnectionTimeMs = connMs;
                tab.LastQueryTimeMs = queryMs;
                tab.LastExecutionTimeMs = totalMs;

                if (isSuccess)
                {
                    Serilog.Log.Information(
                        request.IsPopout
                            ? "Query executed in {ExecTimeMs}ms (query: {QueryTimeMs}ms, conn: {ConnTimeMs}ms) on {Server}/{Database} [{TabName}] (Popout)"
                            : "Query executed in {ExecTimeMs}ms (query: {QueryTimeMs}ms, conn: {ConnTimeMs}ms) on {Server}/{Database} [{TabName}]",
                        tab.LastExecutionTimeMs,
                        tab.LastQueryTimeMs,
                        tab.LastConnectionTimeMs,
                        string.IsNullOrEmpty(serverName) ? "Server" : serverName,
                        string.IsNullOrEmpty(dbName) ? "Database" : dbName,
                        tab.Name);
                }
                else if (!string.IsNullOrEmpty(errorMessage))
                {
                    Serilog.Log.Information(
                        request.IsPopout
                            ? "Query finished with {Status} in {ExecTimeMs}ms on {Server}/{Database} [{TabName}] (Popout): {Error}"
                            : "Query finished with {Status} in {ExecTimeMs}ms on {Server}/{Database} [{TabName}]: {Error}",
                        errorMessage == "Cancelled" ? "Cancellation" : "Error",
                        tab.LastExecutionTimeMs,
                        string.IsNullOrEmpty(serverName) ? "Server" : serverName,
                        string.IsNullOrEmpty(dbName) ? "Database" : dbName,
                        tab.Name,
                        errorMessage);
                }

                await request.InvokeOnUiAsync(() =>
                {
                    FlushPendingMessagesToTab();
                    tab.AddCompletionTime();
                    tab.IsExecuting = false;
                    tab.IsCancelling = false;
                    tab.CancelAction = null;
                    tab.QueryCts = null;
                    request.SetActiveResultTab?.Invoke(finalResultTab);
                    request.NotifyUi?.Invoke();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

                cts.Dispose();
            }
        }).ConfigureAwait(false);

        if (request.SaveHistory && (!string.IsNullOrWhiteSpace(request.FullScript) || !string.IsNullOrWhiteSpace(tab.Content)))
        {
            var scriptToSave = !string.IsNullOrWhiteSpace(request.FullScript) ? request.FullScript : tab.Content;
            _historyService.QueueSaveTabHistory(
                tab.Id,
                tab.Name,
                scriptToSave,
                serverName,
                dbName,
                tab.ConnectionString,
                tab.ConnectionName);
        }
    }

    public static string StripLeadingComments(string sql)
    {
        var s = sql.AsSpan();
        while (true)
        {
            s = s.TrimStart();
            if (s.IsEmpty) break;

            if (s.StartsWith("--".AsSpan(), StringComparison.Ordinal))
            {
                var nl = s.IndexOf('\n');
                s = nl >= 0 ? s[(nl + 1)..] : ReadOnlySpan<char>.Empty;
                continue;
            }

            if (s.StartsWith("/*".AsSpan(), StringComparison.Ordinal))
            {
                var end = s.IndexOf("*/", StringComparison.Ordinal);
                s = end >= 0 ? s[(end + 2)..] : ReadOnlySpan<char>.Empty;
                continue;
            }

            break;
        }
        return s.ToString();
    }

    public static bool IsReadOnlyQuery(string queryText)
    {
        var q = StripLeadingComments(queryText).TrimStart();
        if (q.StartsWith("select", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("with", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var stripped = q;
        if (stripped.StartsWith("execute", StringComparison.OrdinalIgnoreCase))
        {
            stripped = stripped.Substring(7).TrimStart();
        }
        else if (stripped.StartsWith("exec", StringComparison.OrdinalIgnoreCase))
        {
            stripped = stripped.Substring(4).TrimStart();
        }

        if (stripped.StartsWith("sp_helptext", StringComparison.OrdinalIgnoreCase) ||
            stripped.StartsWith("sp_help", StringComparison.OrdinalIgnoreCase) ||
            stripped.StartsWith("sp_spaceused", StringComparison.OrdinalIgnoreCase) ||
            stripped.StartsWith("sp_depends", StringComparison.OrdinalIgnoreCase) ||
            stripped.StartsWith("sp_who", StringComparison.OrdinalIgnoreCase) ||
            stripped.StartsWith("sp_columns", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static (string Header, int? DisplayLine) FormatSqlErrorHeader(SqlError err, int lineOffset)
    {
        int displayLine = (string.IsNullOrEmpty(err.Procedure) && err.LineNumber > 0)
            ? err.LineNumber + lineOffset
            : err.LineNumber;

        string procPart = !string.IsNullOrEmpty(err.Procedure) ? $", Procedure {err.Procedure}" : "";
        string header = $"Msg {err.Number}, Level {err.Class}, State {err.State}{procPart}, Line {displayLine}";

        int? navLine = string.IsNullOrEmpty(err.Procedure) && displayLine > 0 ? displayLine : (int?)null;
        return (header, navLine);
    }

    public static (string Header, int? DisplayLine) FormatSqlErrorHeader(SqlException ex, int lineOffset)
    {
        int displayLine = (string.IsNullOrEmpty(ex.Procedure) && ex.LineNumber > 0)
            ? ex.LineNumber + lineOffset
            : ex.LineNumber;

        string procPart = !string.IsNullOrEmpty(ex.Procedure) ? $", Procedure {ex.Procedure}" : "";
        string header = $"Msg {ex.Number}, Level {ex.Class}, State {ex.State}{procPart}, Line {displayLine}";

        int? navLine = string.IsNullOrEmpty(ex.Procedure) && displayLine > 0 ? displayLine : (int?)null;
        return (header, navLine);
    }

    public static int? ParseLineNumberFromErrorText(string text)
    {
        var match = LineNumberRegex.Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int pLine))
            return pLine;
        return null;
    }

    public static int GetResultRowCount(object? rs)
    {
        if (rs == null) return 0;
        if (rs is QueryResultTable qrt) return qrt.RowCount;
        if (rs is DataTable dt) return dt.Rows.Count;
        if (rs is System.Collections.ICollection c) return c.Count;
        if (rs is IEnumerable<dynamic> d) return d.Count();
        return 0;
    }

    public static bool IsResultTextDefinition(object? rs)
    {
        if (rs is QueryResultTable qrt)
        {
            return qrt.Columns.Count == 1 &&
                   qrt.Columns[0].Equals("Text", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static string GetResultSetPaneStyle(object? rsItem, int totalCount)
    {
        if (totalCount <= 1)
        {
            return "flex: 1 0 100%; min-height: 0; position: relative;";
        }

        if (IsResultTextDefinition(rsItem))
        {
            return "flex: 1 1 auto; min-height: 280px; position: relative;";
        }

        int rowCount = GetResultRowCount(rsItem);
        if (rowCount <= 3)
        {
            int compactHeight = 28 + (Math.Max(rowCount, 1) * 24) + 16;
            return $"flex: 0 0 {compactHeight}px; height: {compactHeight}px; min-height: 54px; position: relative;";
        }

        return "flex: 1 1 auto; min-height: 90px; position: relative;";
    }

    public static async Task<(string QueryText, string FullScript, int LineOffset)> ResolveEditorInputAsync(
        IJSRuntime js,
        string editorId,
        Func<Task<string>>? fallbackGetValueAsync,
        QueryTab? tabToSync = null)
    {
        string queryText = "";
        string fullScript = "";
        int lineOffset = 0;

        try
        {
            var input = await js.InvokeAsync<QueryEditorInput?>("getQueryExecutionInput", editorId);
            if (input != null)
            {
                fullScript = input.FullText ?? "";
                if (tabToSync != null) tabToSync.Content = fullScript;

                if (!string.IsNullOrWhiteSpace(input.SelectedText))
                {
                    queryText = input.SelectedText;
                    lineOffset = Math.Max(0, input.StartLineNumber - 1);
                }
                else
                {
                    queryText = fullScript;
                }
            }
        }
        catch { }

        if (string.IsNullOrEmpty(fullScript) && fallbackGetValueAsync != null)
        {
            try
            {
                fullScript = await fallbackGetValueAsync() ?? "";
                if (tabToSync != null) tabToSync.Content = fullScript;

                if (string.IsNullOrWhiteSpace(queryText))
                {
                    queryText = fullScript;
                    lineOffset = 0;
                }
            }
            catch { }
        }

        return (queryText, fullScript, lineOffset);
    }
}
