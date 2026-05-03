using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DbExplore.Services;

public class CompareTableResult
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{Name}]";
    
    public bool IsComparable { get; set; }
    public string NonComparableReason { get; set; } = string.Empty;

    public long SourceRowCount { get; set; }
    public long TargetRowCount { get; set; }
    
    public bool IsLargeTable => SourceRowCount > 1000000 || TargetRowCount > 1000000;
    public string Status { get; set; } = "Pending"; // Identical, Differences Found, Pending
}

public class DataCompareResult
{
    public List<CompareTableResult> ComparableTables { get; set; } = new();
    public List<CompareTableResult> NonComparableTables { get; set; } = new();
}

public class DataDiffRow
{
    public string RowStatus { get; set; } = string.Empty; // Added, Deleted, Modified
    public Dictionary<string, object?> SourceData { get; set; } = new();
    public Dictionary<string, object?> TargetData { get; set; } = new();
}

public class DataCompareService
{
    private readonly string[] UnsupportedTypes = { "VARBINARY", "IMAGE", "XML", "GEOMETRY", "GEOGRAPHY", "TEXT", "NTEXT" };

    public bool AreOnSameServer(string connString1, string connString2)
    {
        try
        {
            var b1 = new SqlConnectionStringBuilder(connString1);
            var b2 = new SqlConnectionStringBuilder(connString2);
            return string.Equals(b1.DataSource, b2.DataSource, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<DataCompareResult> AnalyzeDatabasesAsync(string sourceConn, string targetConn)
    {
        var result = new DataCompareResult();
        
        var sourceBuilder = new SqlConnectionStringBuilder(sourceConn);
        var targetBuilder = new SqlConnectionStringBuilder(targetConn);
        
        var sourceDb = sourceBuilder.InitialCatalog;
        var targetDb = targetBuilder.InitialCatalog;

        using var conn = new SqlConnection(sourceConn);
        await conn.OpenAsync();

        // 1. Get Tables and Columns for Source
        var sourceSchemaSql = $@"
            SELECT 
                t.TABLE_SCHEMA AS SchemaName, 
                t.TABLE_NAME AS TableName, 
                c.COLUMN_NAME AS ColumnName, 
                c.DATA_TYPE AS DataType 
            FROM [{sourceDb}].INFORMATION_SCHEMA.TABLES t
            JOIN [{sourceDb}].INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
            WHERE t.TABLE_TYPE = 'BASE TABLE'";
            
        // 2. Get Tables and Columns for Target
        var targetSchemaSql = $@"
            SELECT 
                t.TABLE_SCHEMA AS SchemaName, 
                t.TABLE_NAME AS TableName, 
                c.COLUMN_NAME AS ColumnName, 
                c.DATA_TYPE AS DataType 
            FROM [{targetDb}].INFORMATION_SCHEMA.TABLES t
            JOIN [{targetDb}].INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
            WHERE t.TABLE_TYPE = 'BASE TABLE'";

        var sourceCols = await conn.QueryAsync<dynamic>(sourceSchemaSql);
        var targetCols = await conn.QueryAsync<dynamic>(targetSchemaSql);

        // Group by Table
        var sourceTables = sourceCols.GroupBy(c => $"[{(string)c.SchemaName}].[{(string)c.TableName}]").ToDictionary(g => g.Key, g => g.ToList());
        var targetTables = targetCols.GroupBy(c => $"[{(string)c.SchemaName}].[{(string)c.TableName}]").ToDictionary(g => g.Key, g => g.ToList());

        // Get Primary Keys
        var pkSql = $@"
            SELECT 
                s.name AS SchemaName,
                t.name AS TableName,
                c.name AS ColumnName
            FROM [{sourceDb}].sys.indexes i
            JOIN [{sourceDb}].sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN [{sourceDb}].sys.tables t ON i.object_id = t.object_id
            JOIN [{sourceDb}].sys.schemas s ON t.schema_id = s.schema_id
            JOIN [{sourceDb}].sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.is_primary_key = 1";
            
        var pkRaw = await conn.QueryAsync<dynamic>(pkSql);
        var sourcePks = pkRaw.GroupBy(p => $"[{(string)p.SchemaName}].[{(string)p.TableName}]")
                             .ToDictionary(g => g.Key, g => g.Select(p => (string)p.ColumnName).ToList());

        // We also need row counts
        var countSql = $@"
            SELECT 
                s.Name AS SchemaName,
                t.Name AS TableName,
                p.rows AS RowCounts
            FROM [{sourceDb}].sys.tables t
            JOIN [{sourceDb}].sys.schemas s ON t.schema_id = s.schema_id
            JOIN [{sourceDb}].sys.partitions p ON t.object_id = p.object_id
            WHERE p.index_id IN (0, 1)
            GROUP BY s.Name, t.Name, p.rows";
            
        var sourceCounts = (await conn.QueryAsync<dynamic>(countSql))
            .ToDictionary(r => $"[{(string)r.SchemaName}].[{(string)r.TableName}]", r => (long)r.RowCounts);
            
        var targetCountSql = countSql.Replace($"[{sourceDb}]", $"[{targetDb}]");
        var targetCounts = (await conn.QueryAsync<dynamic>(targetCountSql))
            .ToDictionary(r => $"[{(string)r.SchemaName}].[{(string)r.TableName}]", r => (long)r.RowCounts);


        // Now, evaluate each table
        foreach (var kvp in sourceTables)
        {
            var tableName = kvp.Key;
            var schema = (string)kvp.Value.First().SchemaName;
            var name = (string)kvp.Value.First().TableName;

            var tableRes = new CompareTableResult 
            { 
                Schema = schema, 
                Name = name,
                SourceRowCount = sourceCounts.ContainsKey(tableName) ? sourceCounts[tableName] : 0,
                TargetRowCount = targetCounts.ContainsKey(tableName) ? targetCounts[tableName] : 0
            };

            if (!targetTables.ContainsKey(tableName))
            {
                tableRes.IsComparable = false;
                tableRes.NonComparableReason = "Table missing in Target DB";
                result.NonComparableTables.Add(tableRes);
                continue;
            }

            if (!sourcePks.ContainsKey(tableName))
            {
                tableRes.IsComparable = false;
                tableRes.NonComparableReason = "Missing Primary Key";
                result.NonComparableTables.Add(tableRes);
                continue;
            }

            var sCols = kvp.Value;
            var tCols = targetTables[tableName];

            // Check schema match
            var sColNames = sCols.Select(c => (string)c.ColumnName).OrderBy(c => c).ToList();
            var tColNames = tCols.Select(c => (string)c.ColumnName).OrderBy(c => c).ToList();

            if (!sColNames.SequenceEqual(tColNames))
            {
                tableRes.IsComparable = false;
                tableRes.NonComparableReason = "Schema Mismatch (Columns differ)";
                result.NonComparableTables.Add(tableRes);
                continue;
            }

            // Check data types
            bool typeMismatch = false;
            bool hasUnsupported = false;
            foreach(var sc in sCols)
            {
                var tc = tCols.First(c => (string)c.ColumnName == (string)sc.ColumnName);
                if ((string)sc.DataType != (string)tc.DataType)
                {
                    typeMismatch = true;
                    break;
                }

                if (UnsupportedTypes.Contains(((string)sc.DataType).ToUpper()))
                {
                    hasUnsupported = true;
                    break;
                }
            }

            if (typeMismatch)
            {
                tableRes.IsComparable = false;
                tableRes.NonComparableReason = "Data Type Mismatch";
                result.NonComparableTables.Add(tableRes);
                continue;
            }

            if (hasUnsupported)
            {
                tableRes.IsComparable = false;
                tableRes.NonComparableReason = "Contains Unsupported Data Types";
                result.NonComparableTables.Add(tableRes);
                continue;
            }

            // It's comparable
            tableRes.IsComparable = true;
            result.ComparableTables.Add(tableRes);
        }

        // Add tables that exist in target but not source
        foreach (var kvp in targetTables)
        {
            var tableName = kvp.Key;
            if (!sourceTables.ContainsKey(tableName))
            {
                var schema = (string)kvp.Value.First().SchemaName;
                var name = (string)kvp.Value.First().TableName;

                var tableRes = new CompareTableResult 
                { 
                    Schema = schema, 
                    Name = name,
                    SourceRowCount = 0,
                    TargetRowCount = targetCounts.ContainsKey(tableName) ? targetCounts[tableName] : 0,
                    IsComparable = false,
                    NonComparableReason = "Table missing in Source DB"
                };
                result.NonComparableTables.Add(tableRes);
            }
        }

        // Check if identical using simple count first, but we want to actually check data differences status.
        // Doing full EXCEPT for status might be slow for all tables, so we could just do EXCEPT TOP 1 for checking IF there are diffs
        foreach (var t in result.ComparableTables)
        {
            if (t.SourceRowCount == 0 && t.TargetRowCount == 0)
            {
                t.Status = "Identical";
                continue;
            }

            var sCols = sourceTables[t.FullName].Select(c => $"[{c.ColumnName}]");
            var colList = string.Join(", ", sCols);

            var checkSql = $@"
                IF EXISTS (
                    (SELECT {colList} FROM [{sourceDb}].{t.FullName}
                     EXCEPT
                     SELECT {colList} FROM [{targetDb}].{t.FullName})
                    UNION ALL
                    (SELECT {colList} FROM [{targetDb}].{t.FullName}
                     EXCEPT
                     SELECT {colList} FROM [{sourceDb}].{t.FullName})
                )
                SELECT 1 ELSE SELECT 0";

            try
            {
                var hasDiffs = await conn.ExecuteScalarAsync<int>(checkSql) == 1;
                t.Status = hasDiffs ? "Differences Found" : "Identical";
            }
            catch(Exception ex)
            {
                 Console.WriteLine($"Error comparing {t.FullName}: {ex.Message}");
                 t.Status = "Error comparing";
            }
        }

        return result;
    }

    public async Task<List<DataDiffRow>> GetTableDifferencesAsync(string sourceConn, string targetConn, string schema, string tableName)
    {
        var diffs = new List<DataDiffRow>();

        var sourceBuilder = new SqlConnectionStringBuilder(sourceConn);
        var targetBuilder = new SqlConnectionStringBuilder(targetConn);
        
        var sourceDb = sourceBuilder.InitialCatalog;
        var targetDb = targetBuilder.InitialCatalog;
        var fullName = $"[{schema}].[{tableName}]";

        using var conn = new SqlConnection(sourceConn);
        await conn.OpenAsync();

        // Get columns
        var schemaSql = $@"
            SELECT COLUMN_NAME 
            FROM [{sourceDb}].INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table";
            
        var columns = (await conn.QueryAsync<string>(schemaSql, new { Schema = schema, Table = tableName })).ToList();
        var colList = string.Join(", ", columns.Select(c => $"[{c}]"));

        // Get PK
        var pkSql = $@"
            SELECT c.name
            FROM [{sourceDb}].sys.indexes i
            JOIN [{sourceDb}].sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN [{sourceDb}].sys.tables t ON i.object_id = t.object_id
            JOIN [{sourceDb}].sys.schemas s ON t.schema_id = s.schema_id
            JOIN [{sourceDb}].sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.is_primary_key = 1 AND s.name = @Schema AND t.name = @Table";
            
        var pkColumns = (await conn.QueryAsync<string>(pkSql, new { Schema = schema, Table = tableName })).ToList();
        
        if (!pkColumns.Any()) return diffs; // Should not happen due to prior checks

        // Instead of doing a full EXCEPT and fetching everything to client, we can do a FULL OUTER JOIN based on PK
        // But doing a FULL OUTER JOIN with ALL columns compared requires a lot of OR conditions for NULLs etc.
        // Wait, EXCEPT is easier to write, let's just get the rows that differ.

        var diffQuerySql = $@"
            SELECT 'SourceOnly' AS RowStatus, {colList} FROM (
                SELECT {colList} FROM [{sourceDb}].{fullName}
                EXCEPT
                SELECT {colList} FROM [{targetDb}].{fullName}
            ) s
            UNION ALL
            SELECT 'TargetOnly' AS RowStatus, {colList} FROM (
                SELECT {colList} FROM [{targetDb}].{fullName}
                EXCEPT
                SELECT {colList} FROM [{sourceDb}].{fullName}
            ) t
        ";

        var reader = await conn.ExecuteReaderAsync(diffQuerySql);
        
        // Read differences
        var rawDiffs = new List<dynamic>();
        while (reader.Read())
        {
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i);
                dict[reader.GetName(i)] = val == DBNull.Value ? null : val;
            }
            rawDiffs.Add(dict);
        }

        // Now pair them up by PK to detect "Modified" vs "Added/Deleted"
        // A row is "Modified" if the PK exists in both SourceOnly and TargetOnly
        var sourceOnly = rawDiffs.Where(d => (string)d["RowStatus"] == "SourceOnly").ToList();
        var targetOnly = rawDiffs.Where(d => (string)d["RowStatus"] == "TargetOnly").ToList();

        string GetPkKey(Dictionary<string, object?> row)
        {
            return string.Join("|", pkColumns.Select(c => row[c]?.ToString() ?? "NULL"));
        }

        var sourceDict = sourceOnly.ToDictionary(r => GetPkKey(r), r => r);
        var targetDict = targetOnly.ToDictionary(r => GetPkKey(r), r => r);

        foreach (var sRow in sourceOnly)
        {
            var key = GetPkKey(sRow);
            if (targetDict.ContainsKey(key))
            {
                // Modified
                var tRow = targetDict[key];
                diffs.Add(new DataDiffRow
                {
                    RowStatus = "Modified",
                    SourceData = sRow,
                    TargetData = tRow
                });
                targetDict.Remove(key); // Processed
            }
            else
            {
                // Added (Exists in Source, not in Target)
                diffs.Add(new DataDiffRow
                {
                    RowStatus = "Added",
                    SourceData = sRow,
                    TargetData = new Dictionary<string, object?>()
                });
            }
        }

        foreach (var tRow in targetDict.Values)
        {
            // Deleted (Exists in Target, not in Source)
            diffs.Add(new DataDiffRow
            {
                RowStatus = "Deleted",
                SourceData = new Dictionary<string, object?>(),
                TargetData = tRow
            });
        }

        return diffs;
    }
}
