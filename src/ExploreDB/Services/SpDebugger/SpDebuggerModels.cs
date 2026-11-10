using System;
using System.Collections.Generic;

namespace ExploreDB.Services.SpDebugger;

public class SpVariableInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? PreviousValue { get; set; }
    public bool HasChanged => PreviousValue != null && PreviousValue != Value;
    public bool IsParameter { get; set; }
}

public class SpTempTableInfo
{
    public string Name { get; set; } = string.Empty;
    public int TotalRowCount { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<IDictionary<string, object?>> Rows { get; set; } = new();

    public void Clear()
    {
        Rows.Clear();
        Columns.Clear();
    }
}

public class SpStatementStep
{
    public int StepIndex { get; set; }
    public int LineNumber { get; set; }
    public int EndLineNumber { get; set; }
    public int Column { get; set; }
    public string StatementText { get; set; } = string.Empty;
    public string StatementType { get; set; } = "STATEMENT";
}

public class SpResultSetInfo
{
    public int StepIndex { get; set; }
    public int LineNumber { get; set; }
    public string StatementSummary { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public List<IDictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRowCount => Rows.Count;

    public void Clear()
    {
        Columns.Clear();
        Rows.Clear();
    }
}

public class SpDebugStepSnapshot
{
    public int StepIndex { get; set; }
    public int LineNumber { get; set; }
    public int EndLineNumber { get; set; }
    public string StatementText { get; set; } = string.Empty;
    public string StatementType { get; set; } = "STATEMENT";
    public Dictionary<string, SpVariableInfo> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SpTempTableInfo> TempTables { get; set; } = new();
    public List<SpResultSetInfo> ResultSets { get; set; } = new();
    public List<string> Messages { get; set; } = new();
    public long DurationMs { get; set; }

    public void Clear()
    {
        Variables.Clear();
        foreach (var tt in TempTables)
        {
            tt.Clear();
        }
        TempTables.Clear();
        foreach (var rs in ResultSets)
        {
            rs.Clear();
        }
        ResultSets.Clear();
        Messages.Clear();
    }
}

public class SpParameterInput
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsOutput { get; set; }
    public bool HasDefault { get; set; }
    public string? DefaultValue { get; set; }
}

public class SpPreFlightWarning
{
    public string RuleName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Severity { get; set; } = "Warning"; // "Warning", "Danger", "Info"
}

public class SpDebugSessionState : IDisposable
{
    public string Database { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string ProcedureName { get; set; } = string.Empty;
    public string FullName => string.IsNullOrWhiteSpace(Schema) ? ProcedureName : $"{Schema}.{ProcedureName}";
    public string ProcedureDefinition { get; set; } = string.Empty;

    public List<SpParameterInput> Parameters { get; set; } = new();
    public List<SpStatementStep> Statements { get; set; } = new();
    public List<SpPreFlightWarning> Warnings { get; set; } = new();
    public Dictionary<int, SpDebugStepSnapshot> StepSnapshots { get; set; } = new();

    public int CurrentStepIndex { get; set; } = -1;
    public bool IsRecorded { get; set; }
    public bool IsRecording { get; set; }
    public bool DryRun { get; set; } = true;
    public long TotalServerExecutionMs { get; set; }
    public string? ErrorMessage { get; set; }

    public SpDebugStepSnapshot? CurrentSnapshot =>
        CurrentStepIndex >= 0 && StepSnapshots.TryGetValue(CurrentStepIndex, out var snap) ? snap : null;

    public SpStatementStep? CurrentStatement =>
        CurrentStepIndex >= 0 && CurrentStepIndex < Statements.Count ? Statements[CurrentStepIndex] : null;

    public void ClearMemory()
    {
        foreach (var snap in StepSnapshots.Values)
        {
            snap.Clear();
        }
        StepSnapshots.Clear();
        Statements.Clear();
        Parameters.Clear();
        Warnings.Clear();
        ProcedureDefinition = string.Empty;
        ErrorMessage = null;
        CurrentStepIndex = -1;
        IsRecorded = false;
        IsRecording = false;
    }

    public void Dispose()
    {
        ClearMemory();
    }
}
