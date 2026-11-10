using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace ExploreDB.Services.SpDebugger;

public class SpDebuggerService : IDisposable
{
    private readonly ILogger<SpDebuggerService> _logger;
    private SpDebugSessionState? _activeSession;
    private CancellationTokenSource? _activeCts;
    private SqlConnection? _activeConnection;

    public SpDebuggerService(ILogger<SpDebuggerService> logger)
    {
        _logger = logger;
    }

    public SpDebugSessionState? ActiveSession => _activeSession;

    public SpDebugSessionState CreateSession(string database, string schema, string procedureName, string procedureDefinition)
    {
        // Cleanup any prior session to guarantee zero memory leaks
        DisposeActiveSession();

        var session = new SpDebugSessionState
        {
            Database = database,
            Schema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema,
            ProcedureName = procedureName,
            ProcedureDefinition = procedureDefinition
        };

        ParseAndPrepareSession(session);
        _activeSession = session;
        return session;
    }

    public void ParseAndPrepareSession(SpDebugSessionState session)
    {
        if (string.IsNullOrWhiteSpace(session.ProcedureDefinition))
        {
            session.ErrorMessage = "Procedure definition is empty.";
            return;
        }

        try
        {
            var parser = new TSql160Parser(true);
            using var reader = new StringReader(session.ProcedureDefinition);
            var fragment = parser.Parse(reader, out IList<ParseError> errors);

            if (errors != null && errors.Count > 0)
            {
                var sb = new StringBuilder("SQL Parse Errors:\n");
                foreach (var err in errors)
                {
                    sb.AppendLine($"Line {err.Line}, Col {err.Column}: {err.Message}");
                }
                session.ErrorMessage = sb.ToString();
                return;
            }

            var visitor = new ProcedureAstVisitor(session.ProcedureDefinition);
            fragment.Accept(visitor);

            session.Parameters = visitor.Parameters;
            session.Statements = visitor.Statements;
            session.Warnings = visitor.Warnings;

            if (session.Statements.Count == 0)
            {
                // Fallback: If no procedural body statements parsed, treat entire body as a single statement
                session.Statements.Add(new SpStatementStep
                {
                    StepIndex = 0,
                    LineNumber = 1,
                    EndLineNumber = session.ProcedureDefinition.Split('\n').Length,
                    StatementText = session.ProcedureDefinition,
                    StatementType = "BATCH"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse stored procedure definition");
            session.ErrorMessage = $"Failed to parse stored procedure: {ex.Message}";
        }
    }

    public async Task<bool> RecordDebugSessionAsync(string connectionString, SpDebugSessionState session, CancellationToken externalCts = default)
    {
        if (session.Statements.Count == 0)
        {
            session.ErrorMessage = "No executable statements found in procedure.";
            return false;
        }

        DisposeActiveCts();
        _activeCts = CancellationTokenSource.CreateLinkedTokenSource(externalCts);
        var ct = _activeCts.Token;

        session.IsRecording = true;
        session.ErrorMessage = null;
        session.StepSnapshots.Clear();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var conn = new SqlConnection(connectionString);
            _activeConnection = conn;
            await conn.OpenAsync(ct);

            var messages = new List<string>();
            conn.InfoMessage += (s, e) =>
            {
                foreach (SqlError err in e.Errors)
                {
                    messages.Add(err.Message);
                }
                if (e.Errors.Count == 0 && !string.IsNullOrEmpty(e.Message))
                {
                    messages.Add(e.Message);
                }
            };

            // Build instrumented T-SQL batch that executes in a single roundtrip with sub-second rollback
            var script = BuildInstrumentedBatch(session);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = script;
            cmd.CommandTimeout = 120; // 2 min max for recording

            int currentStep = 0;
            var currentVars = new Dictionary<string, SpVariableInfo>(StringComparer.OrdinalIgnoreCase);

            // Initialize variables with input parameter values
            foreach (var p in session.Parameters)
            {
                currentVars[p.Name] = new SpVariableInfo
                {
                    Name = p.Name,
                    DataType = p.DataType,
                    Value = p.Value ?? "NULL",
                    IsParameter = true
                };
            }

            using (ct.Register(() =>
            {
                try { cmd.Cancel(); } catch { }
            }))
            {
                using var reader = await cmd.ExecuteReaderAsync(ct);

                do
                {
                    int fieldCount = reader.FieldCount;
                    if (fieldCount == 0) continue;

                    string firstCol = reader.GetName(0);

                    // 1. Step Marker: Step {i} is executing
                    if (firstCol.Equals("__EXPLOREDB_STEP__", StringComparison.OrdinalIgnoreCase))
                    {
                        if (await reader.ReadAsync(ct))
                        {
                            currentStep = Convert.ToInt32(reader[0]);
                        }
                        EnsureSnapshot(session, currentStep, currentVars, messages, stopwatch.ElapsedMilliseconds);
                        continue;
                    }

                    // 2. Temp Table probe
                    if (firstCol.Equals("__EXPLOREDB_TEMP_NAME__", StringComparison.OrdinalIgnoreCase))
                    {
                        string? ttName = null;
                        int totalRows = 0;
                        if (await reader.ReadAsync(ct))
                        {
                            ttName = reader.GetString(0);
                            totalRows = Convert.ToInt32(reader[1]);
                        }

                        if (await reader.NextResultAsync(ct) && ttName != null)
                        {
                            var ttInfo = new SpTempTableInfo
                            {
                                Name = ttName,
                                TotalRowCount = totalRows
                            };
                            int fCount = reader.FieldCount;
                            for (int c = 0; c < fCount; c++)
                            {
                                ttInfo.Columns.Add(reader.GetName(c));
                            }
                            int ttRowCount = 0;
                            while (await reader.ReadAsync(ct))
                            {
                                if (++ttRowCount <= 500)
                                {
                                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                    for (int c = 0; c < fCount; c++)
                                    {
                                        row[ttInfo.Columns[c]] = await reader.IsDBNullAsync(c, ct) ? null : reader.GetValue(c);
                                    }
                                    ttInfo.Rows.Add(row);
                                }
                            }

                            var snap = EnsureSnapshot(session, currentStep, currentVars, messages, stopwatch.ElapsedMilliseconds);
                            snap.TempTables.RemoveAll(t => t.Name.Equals(ttName, StringComparison.OrdinalIgnoreCase));
                            snap.TempTables.Add(ttInfo);
                        }
                        continue;
                    }

                    // 3. Variables Probe
                    if (firstCol.Equals("__EXPLOREDB_VAR_NAME__", StringComparison.OrdinalIgnoreCase))
                    {
                        var snap = EnsureSnapshot(session, currentStep, currentVars, messages, stopwatch.ElapsedMilliseconds);
                        while (await reader.ReadAsync(ct))
                        {
                            var vName = reader.GetString(0);
                            var vType = reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetString(1) : "VARCHAR(50)";
                            var vVal = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetString(2) : "NULL";

                            var prevVal = currentVars.TryGetValue(vName, out var existingVar) ? existingVar.Value : null;
                            var updatedVar = new SpVariableInfo
                            {
                                Name = vName,
                                DataType = vType,
                                Value = vVal,
                                PreviousValue = prevVal,
                                IsParameter = session.Parameters.Any(p => p.Name.Equals(vName, StringComparison.OrdinalIgnoreCase))
                            };
                            currentVars[vName] = updatedVar;
                            snap.Variables[vName] = updatedVar;
                        }

                        messages.Clear();
                        continue;
                    }

                    // 4. Real user SELECT result set produced by statement {currentStep}!
                    var stmt = currentStep < session.Statements.Count ? session.Statements[currentStep] : null;
                    var rsInfo = new SpResultSetInfo
                    {
                        StepIndex = currentStep,
                        LineNumber = stmt?.LineNumber ?? 0,
                        StatementSummary = stmt?.StatementText?.Trim() ?? ""
                    };
                    for (int i = 0; i < fieldCount; i++)
                    {
                        var colName = reader.GetName(i);
                        rsInfo.Columns.Add(string.IsNullOrEmpty(colName) ? $"Column{i}" : colName);
                    }

                    int rowLimit = 500; // Cap to 500 preview rows to protect RAM
                    int rowCount = 0;
                    while (await reader.ReadAsync(ct))
                    {
                        if (++rowCount <= rowLimit)
                        {
                            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < fieldCount; i++)
                            {
                                row[rsInfo.Columns[i]] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                            }
                            rsInfo.Rows.Add(row);
                        }
                    }

                    var targetSnap = EnsureSnapshot(session, currentStep, currentVars, messages, stopwatch.ElapsedMilliseconds);
                    targetSnap.ResultSets.Add(rsInfo);
                }
                while (await reader.NextResultAsync(ct));
            }

            // Fill snapshots for any steps that didn't emit probes
            for (int i = 0; i < session.Statements.Count; i++)
            {
                EnsureSnapshot(session, i, currentVars, messages, stopwatch.ElapsedMilliseconds);
            }

            session.IsRecorded = true;
            session.CurrentStepIndex = session.Statements.Count > 0 ? 0 : -1;
            stopwatch.Stop();
            session.TotalServerExecutionMs = stopwatch.ElapsedMilliseconds;
            return true;
        }
        catch (OperationCanceledException)
        {
            session.ErrorMessage = "Debug recording was cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stored procedure debug recording");
            session.ErrorMessage = $"Execution Error: {ex.Message}";
            return false;
        }
        finally
        {
            session.IsRecording = false;
            _activeConnection = null;
        }
    }

    private static SpDebugStepSnapshot EnsureSnapshot(SpDebugSessionState session, int stepIndex, Dictionary<string, SpVariableInfo> currentVars, List<string> messages, long elapsedMs)
    {
        if (session.StepSnapshots.TryGetValue(stepIndex, out var existing))
        {
            return existing;
        }

        var stmt = stepIndex < session.Statements.Count ? session.Statements[stepIndex] : null;
        var snap = new SpDebugStepSnapshot
        {
            StepIndex = stepIndex,
            LineNumber = stmt?.LineNumber ?? 1,
            EndLineNumber = stmt?.EndLineNumber ?? 1,
            StatementText = stmt?.StatementText ?? "",
            StatementType = stmt?.StatementType ?? "STATEMENT",
            DurationMs = elapsedMs,
            Messages = new List<string>(messages)
        };

        // Inherit state from previous step (Option A: Cumulative)
        if (stepIndex > 0 && session.StepSnapshots.TryGetValue(stepIndex - 1, out var prevSnap))
        {
            // Inherit variables
            foreach (var kv in prevSnap.Variables)
            {
                snap.Variables[kv.Key] = new SpVariableInfo
                {
                    Name = kv.Value.Name,
                    DataType = kv.Value.DataType,
                    Value = kv.Value.Value,
                    PreviousValue = kv.Value.Value,
                    IsParameter = kv.Value.IsParameter
                };
            }

            // Inherit temp tables
            foreach (var tt in prevSnap.TempTables)
            {
                var cloneTt = new SpTempTableInfo
                {
                    Name = tt.Name,
                    TotalRowCount = tt.TotalRowCount,
                    Columns = new List<string>(tt.Columns)
                };
                foreach (var r in tt.Rows)
                {
                    cloneTt.Rows.Add(new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase));
                }
                snap.TempTables.Add(cloneTt);
            }

            // Inherit result sets (Option A: Cumulative)
            foreach (var rs in prevSnap.ResultSets)
            {
                var cloneRs = new SpResultSetInfo
                {
                    StepIndex = rs.StepIndex,
                    LineNumber = rs.LineNumber,
                    StatementSummary = rs.StatementSummary,
                    Columns = new List<string>(rs.Columns),
                    Rows = new List<IDictionary<string, object?>>(rs.Rows)
                };
                snap.ResultSets.Add(cloneRs);
            }
        }
        else
        {
            // First step: seed with parameter values
            foreach (var kv in currentVars)
            {
                snap.Variables[kv.Key] = new SpVariableInfo
                {
                    Name = kv.Value.Name,
                    DataType = kv.Value.DataType,
                    Value = kv.Value.Value,
                    PreviousValue = null,
                    IsParameter = kv.Value.IsParameter
                };
            }
        }

        session.StepSnapshots[stepIndex] = snap;
        return snap;
    }

    private string BuildInstrumentedBatch(SpDebugSessionState session)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET LOCK_TIMEOUT 5000;");
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine();

        // 1. Begin Sandbox Transaction
        sb.AppendLine("-- [EXPLOREDB DEBUG SANDBOX: DRY RUN]");
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine();

        // 2. Declare and initialize parameters
        foreach (var p in session.Parameters)
        {
            var val = string.IsNullOrWhiteSpace(p.Value) ? "NULL" : p.Value.Trim();
            // Wrap in quotes if string/date and not already quoted
            if (!val.Equals("NULL", StringComparison.OrdinalIgnoreCase) &&
                (p.DataType.Contains("CHAR", StringComparison.OrdinalIgnoreCase) ||
                 p.DataType.Contains("DATE", StringComparison.OrdinalIgnoreCase) ||
                 p.DataType.Contains("TIME", StringComparison.OrdinalIgnoreCase)) &&
                !val.StartsWith("'"))
            {
                val = $"'{val.Replace("'", "''")}'";
            }
            sb.AppendLine($"DECLARE {p.Name} {p.DataType} = {val};");
        }
        sb.AppendLine();

        // Collect all variable names and types (parameters + declared)
        var varTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in session.Parameters)
        {
            varTypes[p.Name] = p.DataType;
        }

        var allTempTables = new List<string>();

        // 3. Emit each statement followed by step markers, variable probes, and temp table probes
        for (int i = 0; i < session.Statements.Count; i++)
        {
            var stmt = session.Statements[i];

            // If statement is a DECLARE statement, also track any newly declared variables and their data types
            ExtractDeclaredVarsWithTypes(stmt.StatementText, varTypes);

            // Track any temp tables referenced
            var ttMatches = System.Text.RegularExpressions.Regex.Matches(stmt.StatementText, @"(?<!\w)#[a-zA-Z0-9_]+");
            foreach (System.Text.RegularExpressions.Match match in ttMatches)
            {
                if (!allTempTables.Contains(match.Value, StringComparer.OrdinalIgnoreCase))
                {
                    allTempTables.Add(match.Value);
                }
            }

            sb.AppendLine($"-- STEP {i}: Line {stmt.LineNumber}");
            // Emit Step Marker BEFORE statement execution so any result sets emitted are captured into step {i}
            sb.AppendLine($"SELECT {i} AS [__EXPLOREDB_STEP__];");
            sb.AppendLine();

            sb.AppendLine(stmt.StatementText.TrimEnd(';'));
            sb.AppendLine(";");
            sb.AppendLine();

            // Emit Variables Probe
            if (varTypes.Count > 0)
            {
                sb.AppendLine("SELECT [__EXPLOREDB_VAR_NAME__], [DataType], [Val] FROM (");
                int vIdx = 0;
                foreach (var kvp in varTypes)
                {
                    var varName = kvp.Key;
                    var dataType = kvp.Value.Replace("'", "''");
                    var union = vIdx > 0 ? "UNION ALL " : "";
                    sb.AppendLine($"  {union}SELECT '{varName}' AS [__EXPLOREDB_VAR_NAME__], '{dataType}' AS [DataType], TRY_CONVERT(NVARCHAR(4000), {varName}) AS [Val]");
                    vIdx++;
                }
                sb.AppendLine(") AS [_vars];");
            }

            // Emit Temp Table Probes
            foreach (var tt in allTempTables)
            {
                sb.AppendLine($"IF OBJECT_ID('tempdb..{tt}') IS NOT NULL");
                sb.AppendLine("BEGIN");
                sb.AppendLine($"    SELECT '{tt}' AS [__EXPLOREDB_TEMP_NAME__], COUNT_BIG(*) AS [TotalRows] FROM {tt};");
                sb.AppendLine($"    SELECT TOP 500 * FROM {tt};");
                sb.AppendLine("END;");
            }

            sb.AppendLine();
        }

        // 4. Guaranteed Safety Rollback in all cases
        sb.AppendLine("-- [EXPLOREDB DEBUG FINISH: MANDATORY ROLLBACK]");
        sb.AppendLine("IF @@TRANCOUNT > 0");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    ROLLBACK TRANSACTION;");
        sb.AppendLine("END;");

        return sb.ToString();
    }

    private static void ExtractDeclaredVarsWithTypes(string text, Dictionary<string, string> varTypes)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!text.TrimStart().StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)) return;

        // Strip leading DECLARE
        var declContent = text.TrimStart();
        if (declContent.Length > 7)
        {
            declContent = declContent.Substring(7).Trim();
        }

        // Split by comma at the top level
        var parts = declContent.Split(',');
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.StartsWith("@"))
            {
                var tokens = part.Split(new[] { ' ', '\t', '=', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 1)
                {
                    var varName = tokens[0].TrimEnd(';', ',');
                    // Exclude table variables
                    if (tokens.Length >= 2 && tokens[1].Equals("TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var dataType = tokens.Length >= 2 ? tokens[1].TrimEnd(';', ',') : "VARCHAR(50)";
                    if (!varTypes.ContainsKey(varName))
                    {
                        varTypes[varName] = dataType;
                    }
                }
            }
        }
    }

    public void CancelRecording()
    {
        try
        {
            _activeCts?.Cancel();
        }
        catch { }
    }

    public void DisposeActiveSession()
    {
        CancelRecording();
        if (_activeSession != null)
        {
            _activeSession.ClearMemory();
            _activeSession = null;
        }
    }

    private void DisposeActiveCts()
    {
        if (_activeCts != null)
        {
            try { _activeCts.Dispose(); } catch { }
            _activeCts = null;
        }
    }

    public void Dispose()
    {
        DisposeActiveSession();
        DisposeActiveCts();
        if (_activeConnection != null)
        {
            try { _activeConnection.Close(); _activeConnection.Dispose(); } catch { }
            _activeConnection = null;
        }
        GC.SuppressFinalize(this);
    }
}

internal class ProcedureAstVisitor : TSqlFragmentVisitor
{
    private readonly string _fullScript;
    public List<SpParameterInput> Parameters { get; } = new();
    public List<SpStatementStep> Statements { get; } = new();
    public List<SpPreFlightWarning> Warnings { get; } = new();

    public ProcedureAstVisitor(string fullScript)
    {
        _fullScript = fullScript;
    }

    public override void ExplicitVisit(CreateProcedureStatement node)
    {
        ExtractHeader(node.Parameters);
        ExtractStatements(node.StatementList);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(AlterProcedureStatement node)
    {
        ExtractHeader(node.Parameters);
        ExtractStatements(node.StatementList);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(CreateOrAlterProcedureStatement node)
    {
        ExtractHeader(node.Parameters);
        ExtractStatements(node.StatementList);
        base.ExplicitVisit(node);
    }

    private void ExtractHeader(IList<ProcedureParameter> parameters)
    {
        if (parameters == null) return;
        foreach (var p in parameters)
        {
            var pName = p.VariableName?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(pName)) continue;

            var typeName = GetDataTypeString(p.DataType);
            var isOutput = p.Modifier == ParameterModifier.Output;
            string? defVal = null;
            if (p.Value is Literal lit)
            {
                defVal = lit.Value;
            }

            Parameters.Add(new SpParameterInput
            {
                Name = pName,
                DataType = typeName,
                IsOutput = isOutput,
                HasDefault = defVal != null,
                DefaultValue = defVal,
                Value = defVal
            });
        }
    }

    private void ExtractStatements(StatementList? statementList)
    {
        if (statementList == null || statementList.Statements == null) return;

        foreach (var stmt in statementList.Statements)
        {
            ExtractStatement(stmt);
        }
    }

    private void ExtractStatement(TSqlStatement stmt)
    {
        if (stmt is BeginEndBlockStatement block)
        {
            if (block.StatementList != null)
            {
                ExtractStatements(block.StatementList);
            }
            return;
        }

        if (stmt is TryCatchStatement tc)
        {
            if (tc.TryStatements != null)
            {
                ExtractStatements(tc.TryStatements);
            }
            if (tc.CatchStatements != null)
            {
                ExtractStatements(tc.CatchStatements);
            }
            return;
        }

        ScanForWarnings(stmt);

        var startLine = stmt.StartLine;
        var endLine = stmt.StartLine;
        var text = ExtractFragmentText(stmt);
        if (string.IsNullOrWhiteSpace(text)) return;

        var stmtType = GetStatementTypeName(stmt);

        Statements.Add(new SpStatementStep
        {
            StepIndex = Statements.Count,
            LineNumber = startLine,
            EndLineNumber = endLine,
            Column = stmt.StartColumn,
            StatementText = text,
            StatementType = stmtType
        });
    }

    private void ScanForWarnings(TSqlStatement stmt)
    {
        var text = ExtractFragmentText(stmt).ToUpperInvariant();
        if (text.Contains("SP_SEND_DBMAIL"))
        {
            Warnings.Add(new SpPreFlightWarning
            {
                RuleName = "External Mail Warning",
                Message = "This procedure contains 'sp_send_dbmail'. External emails cannot be rolled back by transactions.",
                LineNumber = stmt.StartLine,
                Severity = "Danger"
            });
        }
        if (text.Contains("XP_CMDSHELL"))
        {
            Warnings.Add(new SpPreFlightWarning
            {
                RuleName = "Shell Command Warning",
                Message = "This procedure contains 'xp_cmdshell'. OS level commands cannot be rolled back by transactions.",
                LineNumber = stmt.StartLine,
                Severity = "Danger"
            });
        }
        if (text.Contains("COMMIT") && text.Contains("TRAN"))
        {
            Warnings.Add(new SpPreFlightWarning
            {
                RuleName = "Internal Commit Warning",
                Message = "This procedure contains internal 'COMMIT TRANSACTION' statements.",
                LineNumber = stmt.StartLine,
                Severity = "Warning"
            });
        }
    }

    private string ExtractFragmentText(TSqlFragment fragment)
    {
        if (fragment.StartOffset >= 0 && fragment.FragmentLength > 0 &&
            fragment.StartOffset + fragment.FragmentLength <= _fullScript.Length)
        {
            return _fullScript.Substring(fragment.StartOffset, fragment.FragmentLength).Trim();
        }
        return string.Empty;
    }

    private static string GetStatementTypeName(TSqlStatement stmt) => stmt switch
    {
        SelectStatement => "SELECT",
        UpdateStatement => "UPDATE",
        InsertStatement => "INSERT",
        DeleteStatement => "DELETE",
        SetVariableStatement => "SET",
        ExecuteStatement => "EXECUTE",
        DeclareVariableStatement => "DECLARE",
        CreateTableStatement => "CREATE TABLE",
        IfStatement => "IF",
        WhileStatement => "WHILE",
        BeginEndBlockStatement => "BLOCK",
        _ => "STATEMENT"
    };

    private static string GetDataTypeString(DataTypeReference? dt)
    {
        if (dt == null) return "VARCHAR(50)";
        if (dt is SqlDataTypeReference sdt)
        {
            var name = sdt.SqlDataTypeOption.ToString().ToUpper();
            if (sdt.Parameters.Count == 1)
            {
                return $"{name}({sdt.Parameters[0].Value})";
            }
            if (sdt.Parameters.Count == 2)
            {
                return $"{name}({sdt.Parameters[0].Value},{sdt.Parameters[1].Value})";
            }
            return name;
        }
        if (dt is UserDataTypeReference udt)
        {
            return udt.Name?.BaseIdentifier?.Value ?? "VARCHAR(50)";
        }
        return "VARCHAR(50)";
    }
}
