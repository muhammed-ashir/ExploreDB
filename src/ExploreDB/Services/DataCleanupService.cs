using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ExploreDB.Services;

public enum CleanupStrategy
{
    Topological,
    ConstraintDisabling
}

public class CleanupTableItem
{
    public string Schema { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{TableName}]";
    public long EstimatedRows { get; set; } = 0;
    public bool IsSelected { get; set; } = true;
    public bool IsAutoSelected { get; set; } = false;
    public string AutoSelectedReason { get; set; } = string.Empty;
    public List<string> ReferencedParents { get; set; } = new();
    public List<string> DependentChildren { get; set; } = new();
}

public class CleanupPlanStep
{
    public int StepNumber { get; set; }
    public string Schema { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{TableName}]";
    public string HierarchyLevel { get; set; } = "Leaf Table"; // "Leaf Table", "Intermediate", "Root Table"
    public long EstimatedRows { get; set; } = 0;
    public List<string> DependsOn { get; set; } = new();
    public List<string> ReferencedBy { get; set; } = new();
    public bool HasSelfReference { get; set; } = false;
    public bool IsInCycle { get; set; } = false;
    public string Explanation { get; set; } = string.Empty;
    public bool IsAutoSelected { get; set; } = false;
    public string AutoSelectedReason { get; set; } = string.Empty;
    public int DependentChildrenCount { get; set; } = 0;
}

public class CleanupPlanResult
{
    public List<CleanupPlanStep> Steps { get; set; } = new();
    public bool HasCycles { get; set; } = false;
    public List<string> CycleTables { get; set; } = new();
    public long TotalEstimatedRows { get; set; } = 0;
    public int TotalTables => Steps.Count;
    public string DatabaseName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public CleanupStrategy Strategy { get; set; } = CleanupStrategy.ConstraintDisabling;
    public bool ReseedIdentities { get; set; } = true;
}

public class CleanupSessionState
{
    public string DatabaseName { get; set; } = string.Empty;
    public List<CleanupTableItem> Tables { get; set; } = new();
    public string TableFilter { get; set; } = string.Empty;
    public bool FilterCaseSensitive { get; set; } = false;
    public bool FilterMatchWholeWord { get; set; } = false;
    public bool FilterExactMatch { get; set; } = false;
    public CleanupStrategy Strategy { get; set; } = CleanupStrategy.ConstraintDisabling;
    public bool ReseedIdentities { get; set; } = true;
    public bool AutoIncludeChildTables { get; set; } = true;
    public bool ShowSelectedOnly { get; set; } = false;
    public CleanupPlanResult? Plan { get; set; }
}

public class DataCleanupService
{
    private readonly ConnectionService _connectionService;
    private readonly SchemaService _schemaService;

    public CleanupSessionState? SessionState { get; set; }

    public void SaveSessionState(CleanupSessionState state)
    {
        SessionState = state;
    }

    public void ClearSessionState()
    {
        SessionState = null;
    }

    public DataCleanupService(ConnectionService connectionService, SchemaService schemaService)
    {
        _connectionService = connectionService;
        _schemaService = schemaService;
    }

    /// <summary>
    /// Fetches all user tables with fast partition-based row estimates and foreign key relationship metadata.
    /// </summary>
    public async Task<List<CleanupTableItem>> GetTablesWithEstimatesAsync()
    {
        var result = new List<CleanupTableItem>();

        if (!_schemaService.IsDatabaseConnected)
        {
            return result;
        }

        // 1. Fetch row estimates from sys.partitions (sub-millisecond metadata query)
        var rowEstimates = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    SUM(p.rows) AS RowCounts
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.partitions p ON t.object_id = p.object_id
                WHERE p.index_id IN (0, 1)
                  AND t.is_ms_shipped = 0
                GROUP BY s.name, t.name;
            ";

            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 30;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                var rows = reader.GetInt64(2);
                rowEstimates[$"[{schema}].[{table}]"] = rows;
            }
        }
        catch
        {
            // Fallback if permissions or query fails
        }

        // 2. Populate from SchemaService.Tables
        foreach (var t in _schemaService.Tables)
        {
            rowEstimates.TryGetValue(t.FullName, out var rowCount);

            var item = new CleanupTableItem
            {
                Schema = t.Schema,
                TableName = t.Name,
                EstimatedRows = rowCount,
                IsSelected = true,
                ReferencedParents = t.OutgoingKeys.Select(k => k.ToTable).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                DependentChildren = t.IncomingKeys.Select(k => k.FromTable).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };

            result.Add(item);
        }

        return result.OrderBy(t => t.FullName).ToList();
    }

    /// <summary>
    /// Calculates the dependency-ordered deletion sequence for the selected tables using Kahn's topological sort.
    /// </summary>
    public CleanupPlanResult CalculateDeletionOrder(
        IEnumerable<CleanupTableItem> selectedTables,
        CleanupStrategy strategy = CleanupStrategy.ConstraintDisabling,
        bool reseedIdentities = true,
        string databaseName = "")
    {
        var plan = new CleanupPlanResult
        {
            Strategy = strategy,
            ReseedIdentities = reseedIdentities,
            DatabaseName = string.IsNullOrWhiteSpace(databaseName) ? _schemaService.ActiveDatabase : databaseName,
            GeneratedAt = DateTime.Now
        };

        var selectedSet = new HashSet<string>(selectedTables.Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);
        var tableDict = selectedTables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

        // Build in-scope graph:
        // A child table C has a foreign key to parent table P.
        // For DELETION: C MUST be deleted BEFORE P.
        // Therefore, table P depends on C being deleted first.
        // P has prerequisites = all selected children C that reference P.
        var prerequisitesCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parentsOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var selfReferencing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var full in selectedSet)
        {
            prerequisitesCount[full] = 0;
            parentsOf[full] = new List<string>();
            childrenOf[full] = new List<string>();
        }

        foreach (var item in selectedTables)
        {
            var childFull = item.FullName;

            foreach (var parentFull in item.ReferencedParents)
            {
                if (string.Equals(childFull, parentFull, StringComparison.OrdinalIgnoreCase))
                {
                    selfReferencing.Add(childFull);
                    continue; // Skip self-reference from in-degree count to avoid self-locking
                }

                if (selectedSet.Contains(parentFull))
                {
                    // parentFull cannot be deleted until childFull is deleted
                    prerequisitesCount[parentFull]++;
                    parentsOf[childFull].Add(parentFull);
                    childrenOf[parentFull].Add(childFull);
                }
            }
        }

        // Kahn's Algorithm:
        // Queue all tables with prerequisitesCount == 0 (Leaf tables: no children waiting to be deleted)
        var queue = new Queue<string>();
        foreach (var full in selectedSet)
        {
            if (prerequisitesCount[full] == 0)
            {
                queue.Enqueue(full);
            }
        }

        var orderedFullNames = new List<string>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            orderedFullNames.Add(current);

            // current is now deleted. For each parent that depended on current:
            foreach (var parent in parentsOf[current])
            {
                prerequisitesCount[parent]--;
                if (prerequisitesCount[parent] == 0)
                {
                    queue.Enqueue(parent);
                }
            }
        }

        // Check if there are cycles
        if (orderedFullNames.Count < selectedSet.Count)
        {
            plan.HasCycles = true;
            var remaining = selectedSet.Where(t => !orderedFullNames.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
            plan.CycleTables = remaining;

            // Append remaining cycle tables to the plan
            foreach (var r in remaining)
            {
                orderedFullNames.Add(r);
            }
        }

        // Build Plan Steps
        int stepIndex = 1;
        long totalRows = 0;

        foreach (var full in orderedFullNames)
        {
            var item = tableDict[full];
            totalRows += item.EstimatedRows;

            var inScopeParents = parentsOf[full].Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var inScopeChildren = childrenOf[full].Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool hasSelfRef = selfReferencing.Contains(full);
            bool inCycle = plan.CycleTables.Contains(full, StringComparer.OrdinalIgnoreCase);

            string level;
            string explanation;

            if (inCycle)
            {
                level = "Circular Dependency";
                explanation = "Part of a circular foreign key loop. Constraint disabling is required.";
            }
            else if (inScopeChildren.Count == 0 && inScopeParents.Count == 0)
            {
                level = "Independent Table";
                explanation = "No foreign key relationships with other selected tables.";
            }
            else if (inScopeChildren.Count == 0)
            {
                level = "Leaf Table";
                explanation = inScopeParents.Count > 0
                    ? $"Safe to delete first. References parent: {string.Join(", ", inScopeParents)}."
                    : "Safe to delete first (no children depend on it).";
            }
            else if (inScopeParents.Count == 0)
            {
                level = "Root Table";
                explanation = $"Root parent table. Deleted after dependent children: {string.Join(", ", inScopeChildren)}.";
            }
            else
            {
                level = "Intermediate Table";
                explanation = $"Deleted after children ({string.Join(", ", inScopeChildren)}), before parent ({string.Join(", ", inScopeParents)}).";
            }

            if (hasSelfRef)
            {
                explanation += " (Has self-referencing foreign key).";
            }

            plan.Steps.Add(new CleanupPlanStep
            {
                StepNumber = stepIndex++,
                Schema = item.Schema,
                TableName = item.TableName,
                HierarchyLevel = level,
                EstimatedRows = item.EstimatedRows,
                DependsOn = inScopeParents,
                ReferencedBy = inScopeChildren,
                HasSelfReference = hasSelfRef,
                IsInCycle = inCycle,
                Explanation = explanation,
                IsAutoSelected = item.IsAutoSelected,
                AutoSelectedReason = item.AutoSelectedReason,
                DependentChildrenCount = item.DependentChildren.Count
            });
        }

        plan.TotalEstimatedRows = totalRows;
        return plan;
    }

    /// <summary>
    /// Generates a production-ready, transactional T-SQL cleanup script.
    /// </summary>
    public string GenerateSqlScript(CleanupPlanResult plan)
    {
        var sb = new StringBuilder();
        var timestamp = plan.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss");
        var dbName = string.IsNullOrWhiteSpace(plan.DatabaseName) ? "Current Database" : plan.DatabaseName;

        sb.AppendLine("-- =============================================================================");
        sb.AppendLine($"-- Database Table Cleanup Script");
        sb.AppendLine($"-- Target Database : [{dbName}]");
        sb.AppendLine($"-- Generated At    : {timestamp}");
        sb.AppendLine($"-- Total Tables    : {plan.TotalTables}");
        sb.AppendLine($"-- Estimated Rows  : {plan.TotalEstimatedRows:N0}");
        sb.AppendLine($"-- Strategy        : {(plan.Strategy == CleanupStrategy.ConstraintDisabling ? "Disable Constraints -> DELETE -> Re-enable WITH CHECK" : "Topological Sort (Child-to-Parent Order)")}");
        sb.AppendLine($"-- Reseed Identity : {(plan.ReseedIdentities ? "Enabled (Reseed to 0)" : "Disabled")}");
        sb.AppendLine("-- =============================================================================");
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine();
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine("BEGIN TRY");

        if (plan.Strategy == CleanupStrategy.ConstraintDisabling || plan.HasCycles)
        {
            sb.AppendLine();
            sb.AppendLine("    -- -------------------------------------------------------------------------");
            sb.AppendLine("    -- STEP 1: Disable Foreign Key Constraints on Selected Tables");
            sb.AppendLine("    -- -------------------------------------------------------------------------");
            foreach (var step in plan.Steps)
            {
                sb.AppendLine($"    ALTER TABLE {step.FullName} NOCHECK CONSTRAINT ALL;");
            }
        }

        sb.AppendLine();
        sb.AppendLine("    -- -------------------------------------------------------------------------");
        sb.AppendLine($"    -- {(plan.Strategy == CleanupStrategy.ConstraintDisabling ? "STEP 2" : "STEP 1")}: Delete Table Data in Dependency Order");
        sb.AppendLine("    -- -------------------------------------------------------------------------");

        foreach (var step in plan.Steps)
        {
            var cycleTag = step.IsInCycle ? " [CIRCULAR FK]" : "";
            var selfTag = step.HasSelfReference ? " [SELF-REF]" : "";
            sb.AppendLine($"    -- Step {step.StepNumber}: {step.HierarchyLevel}{cycleTag}{selfTag} (~{step.EstimatedRows:N0} rows)");
            sb.AppendLine($"    DELETE FROM {step.FullName};");
        }

        if (plan.ReseedIdentities)
        {
            sb.AppendLine();
            sb.AppendLine("    -- -------------------------------------------------------------------------");
            sb.AppendLine($"    -- {(plan.Strategy == CleanupStrategy.ConstraintDisabling ? "STEP 3" : "STEP 2")}: Reseed Identity Columns");
            sb.AppendLine("    -- -------------------------------------------------------------------------");
            foreach (var step in plan.Steps)
            {
                // Only reseed if table has an identity column; DBCC CHECKIDENT handles tables without identity gracefully or we wrap in check
                sb.AppendLine($"    IF OBJECTPROPERTY(OBJECT_ID(N'{step.FullName}'), 'TableHasIdentity') = 1");
                sb.AppendLine($"        DBCC CHECKIDENT (N'{step.FullName}', RESEED, 0) WITH NO_INFOMSGS;");
            }
        }

        if (plan.Strategy == CleanupStrategy.ConstraintDisabling || plan.HasCycles)
        {
            sb.AppendLine();
            sb.AppendLine("    -- -------------------------------------------------------------------------");
            sb.AppendLine($"    -- {(plan.ReseedIdentities ? "STEP 4" : "STEP 3")}: Re-enable and Verify All Foreign Key Constraints");
            sb.AppendLine("    -- -------------------------------------------------------------------------");
            foreach (var step in plan.Steps)
            {
                sb.AppendLine($"    ALTER TABLE {step.FullName} WITH CHECK CHECK CONSTRAINT ALL;");
            }
        }

        sb.AppendLine();
        sb.AppendLine("    COMMIT TRANSACTION;");
        sb.AppendLine("    PRINT '>>> Table cleanup executed successfully. All selected tables have been emptied.';");
        sb.AppendLine("END TRY");
        sb.AppendLine("BEGIN CATCH");
        sb.AppendLine("    IF @@TRANCOUNT > 0");
        sb.AppendLine("        ROLLBACK TRANSACTION;");
        sb.AppendLine();
        sb.AppendLine("    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();");
        sb.AppendLine("    DECLARE @ErrSev INT = ERROR_SEVERITY();");
        sb.AppendLine("    DECLARE @ErrState INT = ERROR_STATE();");
        sb.AppendLine("    RAISERROR(@ErrMsg, @ErrSev, @ErrState);");
        sb.AppendLine("END CATCH;");

        return sb.ToString();
    }

    /// <summary>
    /// Generates RFC 4180 compliant CSV bytes with UTF-8 BOM.
    /// </summary>
    public byte[] GenerateCsvBytes(CleanupPlanResult plan)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(true)))
        {
            writer.WriteLine("Step,Schema,Table Name,Tags,Estimated Rows,Depends On,Referenced By,Details");

            foreach (var step in plan.Steps)
            {
                var dependsOn = string.Join("; ", step.DependsOn);
                var refBy = string.Join("; ", step.ReferencedBy);
                var tagsList = new List<string>();
                if (step.IsAutoSelected)
                {
                    tagsList.Add("Auto-included");
                }
                if (step.DependentChildrenCount > 0)
                {
                    tagsList.Add(step.DependentChildrenCount == 1 ? "1 Child" : $"{step.DependentChildrenCount} Children");
                }
                var tags = tagsList.Count > 0 ? string.Join("; ", tagsList) : "None";

                writer.WriteLine(string.Join(",",
                    step.StepNumber,
                    EscapeCsv(step.Schema),
                    EscapeCsv(step.TableName),
                    EscapeCsv(tags),
                    step.EstimatedRows,
                    EscapeCsv(string.IsNullOrEmpty(dependsOn) ? "None" : dependsOn),
                    EscapeCsv(string.IsNullOrEmpty(refBy) ? "None" : refBy),
                    EscapeCsv(step.Explanation)
                ));
            }

            writer.Flush();
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Generates a self-contained, print/PDF-ready HTML report document.
    /// </summary>
    public string GenerateHtmlReport(CleanupPlanResult plan)
    {
        var sb = new StringBuilder();
        var dbName = string.IsNullOrWhiteSpace(plan.DatabaseName) ? "Current Database" : plan.DatabaseName;
        var strategyText = plan.Strategy == CleanupStrategy.ConstraintDisabling
            ? "Constraint Disabling"
            : "Topological Sort";

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"    <title>Cleanup Sequence Report — {EscapeHtml(dbName)}</title>");
        sb.AppendLine("    <style>");
        sb.AppendLine(@"
        :root {
            --bg-color: #f8fafc;
            --surface-color: #ffffff;
            --text-primary: #0f172a;
            --text-secondary: #475569;
            --border-color: #e2e8f0;
            --primary-color: #2563eb;
            --badge-leaf: #059669;
            --badge-leaf-bg: #ecfdf5;
            --badge-inter: #0284c7;
            --badge-inter-bg: #f0f9ff;
            --badge-root: #7c3aed;
            --badge-root-bg: #f5f3ff;
            --badge-warn: #d97706;
            --badge-warn-bg: #fffbeb;
            --badge-auto: #0891b2;
            --badge-auto-bg: #ecfeff;
            --badge-child: #d97706;
            --badge-child-bg: #fffbeb;
        }
        @media (prefers-color-scheme: dark) {
            :root {
                --bg-color: #0b1120;
                --surface-color: #1e293b;
                --text-primary: #f8fafc;
                --text-secondary: #94a3b8;
                --border-color: #334155;
                --primary-color: #38bdf8;
                --badge-leaf-bg: #064e3b;
                --badge-leaf: #6ee7b7;
                --badge-inter-bg: #0c4a6e;
                --badge-inter: #7dd3fc;
                --badge-root-bg: #3b0764;
                --badge-root: #c084fc;
                --badge-warn-bg: #451a03;
                --badge-warn: #fcd34d;
                --badge-auto-bg: #164e63;
                --badge-auto: #67e8f9;
                --badge-child-bg: #451a03;
                --badge-child: #fcd34d;
            }
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            background-color: var(--bg-color);
            color: var(--text-primary);
            line-height: 1.5;
            padding: 32px 24px;
        }
        .container { max-width: 1240px; margin: 0 auto; }
        .header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            margin-bottom: 24px;
            padding-bottom: 16px;
            border-bottom: 2px solid var(--border-color);
        }
        .header h1 { font-size: 1.6rem; font-weight: 700; }
        .header .meta { color: var(--text-secondary); font-size: 0.9rem; }
        .print-btn {
            background-color: var(--primary-color);
            color: #ffffff;
            border: none;
            padding: 8px 16px;
            border-radius: 6px;
            font-size: 0.9rem;
            font-weight: 600;
            cursor: pointer;
        }
        .print-btn:hover { opacity: 0.9; }
        .cards-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 16px;
            margin-bottom: 24px;
        }
        .card {
            background-color: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 16px;
        }
        .card .title { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.5px; color: var(--text-secondary); font-weight: 600; }
        .card .value { font-size: 1.5rem; font-weight: 700; margin-top: 4px; }
        .table-container {
            background-color: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            overflow-x: auto;
        }
        table { width: 100%; border-collapse: collapse; text-align: left; font-size: 0.9rem; }
        th {
            background-color: rgba(0,0,0,0.02);
            padding: 12px 16px;
            font-weight: 600;
            border-bottom: 1px solid var(--border-color);
            color: var(--text-secondary);
        }
        td {
            padding: 12px 16px;
            border-bottom: 1px solid var(--border-color);
            vertical-align: top;
        }
        tr:last-child td { border-bottom: none; }
        .badge {
            display: inline-block;
            padding: 2px 8px;
            border-radius: 12px;
            font-size: 0.75rem;
            font-weight: 600;
        }
        .badge-leaf { background-color: var(--badge-leaf-bg); color: var(--badge-leaf); }
        .badge-inter { background-color: var(--badge-inter-bg); color: var(--badge-inter); }
        .badge-root { background-color: var(--badge-root-bg); color: var(--badge-root); }
        .badge-warn { background-color: var(--badge-warn-bg); color: var(--badge-warn); }
        .badge-auto { background-color: var(--badge-auto-bg); color: var(--badge-auto); margin-right: 4px; }
        .badge-child { background-color: var(--badge-child-bg); color: var(--badge-child); }
        .table-name { font-family: 'Cascadia Code', Consolas, monospace; font-weight: 600; font-size: 0.92rem; }
        .th-rows { text-align: right; white-space: nowrap; }
        .rows-count { font-variant-numeric: tabular-nums; font-weight: 600; white-space: nowrap; }
        .footer {
            margin-top: 24px;
            font-size: 0.8rem;
            color: var(--text-secondary);
            text-align: center;
        }
        .footer a {
            color: var(--primary-color);
            text-decoration: none;
            font-weight: 500;
            display: inline-flex;
            align-items: center;
            gap: 4px;
        }
        .footer a:hover {
            text-decoration: underline;
        }
        @media print {
            body { background: #ffffff !important; color: #000000 !important; padding: 0; }
            .print-btn { display: none; }
            .card, .table-container { border: 1px solid #ccc !important; box-shadow: none !important; }
            th { background-color: #f1f5f9 !important; }
        }
        ");
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <div class=\"container\">");
        sb.AppendLine("        <div class=\"header\">");
        sb.AppendLine("            <div>");
        sb.AppendLine($"                <h1>Table Cleanup Sequence Report</h1>");
        sb.AppendLine($"                <div class=\"meta\">Database: <strong>{EscapeHtml(dbName)}</strong> &bull; Generated: {plan.GeneratedAt:MMMM dd, yyyy HH:mm}</div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("            <button class=\"print-btn\" onclick=\"window.print()\">Print / Save as PDF</button>");
        sb.AppendLine("        </div>");

        sb.AppendLine("        <div class=\"cards-grid\">");
        sb.AppendLine("            <div class=\"card\">");
        sb.AppendLine("                <div class=\"title\">Total Tables</div>");
        sb.AppendLine($"                <div class=\"value\">{plan.TotalTables}</div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("            <div class=\"card\">");
        sb.AppendLine("                <div class=\"title\">Estimated Rows to Wipe</div>");
        sb.AppendLine($"                <div class=\"value\">{plan.TotalEstimatedRows:N0}</div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("            <div class=\"card\">");
        sb.AppendLine("                <div class=\"title\">Execution Strategy</div>");
        sb.AppendLine($"                <div class=\"value\" style=\"font-size: 1rem; margin-top: 8px;\">{strategyText}</div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("            <div class=\"card\">");
        sb.AppendLine("                <div class=\"title\">Identities & Cycles</div>");
        sb.AppendLine($"                <div class=\"value\" style=\"font-size: 1rem; margin-top: 8px;\">");
        sb.AppendLine($"                    Reseed: {(plan.ReseedIdentities ? "Yes" : "No")}<br>");
        sb.AppendLine($"                    Cycles: {(plan.HasCycles ? "<span style='color: var(--badge-warn)'>Detected</span>" : "None")}");
        sb.AppendLine("                </div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("        </div>");

        sb.AppendLine("        <div class=\"table-container\">");
        sb.AppendLine("            <table>");
        sb.AppendLine("                <thead>");
        sb.AppendLine("                    <tr>");
        sb.AppendLine("                        <th style=\"width: 50px;\">#</th>");
        sb.AppendLine("                        <th>Table Name</th>");
        sb.AppendLine("                        <th>Tags</th>");
        sb.AppendLine("                        <th class=\"th-rows\" style=\"text-align: right; white-space: nowrap;\">Est. Rows</th>");
        sb.AppendLine("                        <th>Depends On</th>");
        sb.AppendLine("                        <th>Referenced By</th>");
        sb.AppendLine("                        <th>Dependency Details</th>");
        sb.AppendLine("                    </tr>");
        sb.AppendLine("                </thead>");
        sb.AppendLine("                <tbody>");

        foreach (var step in plan.Steps)
        {
            var tagsHtml = new StringBuilder();
            if (step.IsAutoSelected)
            {
                tagsHtml.Append("<span class=\"badge badge-auto\">Auto-included</span> ");
            }
            if (step.DependentChildrenCount > 0)
            {
                var childLabel = step.DependentChildrenCount == 1 ? "1 Child" : $"{step.DependentChildrenCount} Children";
                tagsHtml.Append($"<span class=\"badge badge-child\">{childLabel}</span>");
            }
            if (tagsHtml.Length == 0)
            {
                tagsHtml.Append("<span style=\"color: var(--text-secondary); opacity: 0.5;\">&mdash;</span>");
            }

            var dependsOnHtml = step.DependsOn.Count > 0
                ? string.Join(", ", step.DependsOn.Select(d => $"<span style=\"font-family: monospace; font-size: 0.85rem;\">{EscapeHtml(d)}</span>"))
                : "<span style=\"color: var(--text-secondary); opacity: 0.6;\">None</span>";

            var refByHtml = step.ReferencedBy.Count > 0
                ? string.Join(", ", step.ReferencedBy.Select(r => $"<span style=\"font-family: monospace; font-size: 0.85rem;\">{EscapeHtml(r)}</span>"))
                : "<span style=\"color: var(--text-secondary); opacity: 0.6;\">None</span>";

            sb.AppendLine("                    <tr>");
            sb.AppendLine($"                        <td><strong>{step.StepNumber}</strong></td>");
            sb.AppendLine($"                        <td class=\"table-name\">{EscapeHtml(step.FullName)}</td>");
            sb.AppendLine($"                        <td style=\"white-space: nowrap;\">{tagsHtml}</td>");
            sb.AppendLine($"                        <td style=\"text-align: right; white-space: nowrap;\" class=\"rows-count\">{step.EstimatedRows:N0}</td>");
            sb.AppendLine($"                        <td style=\"font-size: 0.85rem;\">{dependsOnHtml}</td>");
            sb.AppendLine($"                        <td style=\"font-size: 0.85rem;\">{refByHtml}</td>");
            sb.AppendLine($"                        <td style=\"color: var(--text-secondary); font-size: 0.85rem;\">{EscapeHtml(step.Explanation)}</td>");
            sb.AppendLine("                    </tr>");
        }

        sb.AppendLine("                </tbody>");
        sb.AppendLine("            </table>");
        sb.AppendLine("        </div>");

        sb.AppendLine("        <div class=\"footer\">");
        sb.AppendLine("            Generated by <a href=\"https://apps.microsoft.com/store/detail/9NF6M6NT09BM?cid=DevShareMCLPCS\" target=\"_blank\" rel=\"noopener noreferrer\">ExploreDB <svg width=\"11\" height=\"11\" viewBox=\"0 0 16 16\" fill=\"currentColor\" style=\"opacity: 0.75;\"><path fill-rule=\"evenodd\" d=\"M8.636 3.5a.5.5 0 0 0-.5-.5H1.5A1.5 1.5 0 0 0 0 4.5v10A1.5 1.5 0 0 0 1.5 16h10a1.5 1.5 0 0 0 1.5-1.5V7.864a.5.5 0 0 0-1 0V14.5a.5.5 0 0 1-.5.5h-10a.5.5 0 0 1-.5-.5v-10a.5.5 0 0 1 .5-.5h6.636a.5.5 0 0 0 .5-.5z\"/><path fill-rule=\"evenodd\" d=\"M16 .5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0 0 1h3.793L6.146 9.146a.5.5 0 1 0 .708.708L15 1.707V5.5a.5.5 0 0 0 1 0v-5z\"/></svg></a>");
        sb.AppendLine("        </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string EscapeCsv(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        bool needsQuotes = text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r');
        if (needsQuotes)
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return text;
    }

    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
