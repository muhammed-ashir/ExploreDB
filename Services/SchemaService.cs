using Dapper;
using Microsoft.Data.SqlClient;

namespace DbExplore.Services;

public class TableInfo
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{Name}]";
    public List<ColumnInfo> Columns { get; set; } = new();
    // Relations where this table has the FK (points to parent)
    public List<Relationship> OutgoingKeys { get; set; } = new();
    // Relations where this table is referenced by another (is parent of child)
    public List<Relationship> IncomingKeys { get; set; } = new();
    
    // Views that reference this table
    public List<Dependency> ReferencedByViews { get; set; } = new();
    // Stored Procedures that reference this table
    public List<Dependency> ReferencedBySPs { get; set; } = new();

    public override string ToString() => FullName;
}

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    
    // Lineage info (for views)
    public string? SourceSchema { get; set; }
    public string? SourceTable { get; set; }
    public string? SourceColumn { get; set; }
}

public class Relationship
{
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty; // The table being referenced
    public string ToColumn { get; set; } = string.Empty;
    public bool IsNullable { get; set; } = false; // Whether the FK column allows NULL
}



public class Dependency
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{Name}]";
    public string Type { get; set; } = "Unknown"; // 'Table', 'View'
    
    public override string ToString() => FullName;
}

public class ViewInfo
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{Name}]";
    public List<ColumnInfo> Columns { get; set; } = new();
    
    public List<Dependency> Parents { get; set; } = new(); // What I select from
    public List<Dependency> Children { get; set; } = new(); // What selects from me
    // Stored Procedures that reference this view
    public List<Dependency> ReferencedBySPs { get; set; } = new();
    
    public override string ToString() => FullName;
}

public class SpParameter
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsOutput { get; set; } = false;
    public bool HasDefault { get; set; } = false;
    public string? DefaultValue { get; set; }
    public int MaxLength { get; set; } = -1;
    public int Precision { get; set; } = 0;
    public int Scale { get; set; } = 0;

    public string DisplayType
    {
        get
        {
            var type = DataType.ToUpper();
            if (type is "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR")
                return MaxLength == -1 ? $"{DataType}(MAX)" : $"{DataType}({MaxLength})";
            if (type is "DECIMAL" or "NUMERIC")
                return $"{DataType}({Precision},{Scale})";
            return DataType;
        }
    }
}

public class SpInfo
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{Name}]";
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public List<SpParameter> Parameters { get; set; } = new();
    // Tables and Views this SP references
    public List<Dependency> Dependencies { get; set; } = new();

    public override string ToString() => FullName;
}

public class SchemaService
{
    private readonly ConnectionService _connectionService;
    public List<TableInfo> Tables { get; private set; } = new();
    public List<ViewInfo> Views { get; private set; } = new();
    public List<SpInfo> StoredProcedures { get; private set; } = new();
    public event Action? OnSchemaLoaded;

    public SchemaService(ConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    public async Task LoadSchemaAsync()
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();

            Console.WriteLine("Fetching Tables...");
            // 1. Get Tables
            var tableSql = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'";
            var rawTables = await conn.QueryAsync<(string Schema, string Name)>(tableSql);
            
            Tables = rawTables.Select(t => new TableInfo { Schema = t.Schema, Name = t.Name }).OrderBy(t => t.FullName).ToList();
            var tableDict = Tables.ToDictionary(t => t.FullName);

            Console.WriteLine("Fetching Views...");
            // 2. Get Views
            var viewSql = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='VIEW'";
            var rawViews = await conn.QueryAsync<(string Schema, string Name)>(viewSql);
            
            Views = rawViews.Select(v => new ViewInfo { Schema = v.Schema, Name = v.Name }).OrderBy(v => v.FullName).ToList();
            var viewDict = Views.ToDictionary(v => v.FullName);

            Console.WriteLine("Fetching Columns...");
            // 3. Get Columns
            var colSql = "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS";
            var rawCols = await conn.QueryAsync<(string Schema, string Table, string Column, string Type)>(colSql);

            foreach(var col in rawCols)
            {
                var key = $"[{col.Schema}].[{col.Table}]";
                if (tableDict.TryGetValue(key, out var table))
                {
                    table.Columns.Add(new ColumnInfo { Name = col.Column, DataType = col.Type });
                }
                else if (viewDict.TryGetValue(key, out var view))
                {
                    view.Columns.Add(new ColumnInfo { Name = col.Column, DataType = col.Type });
                }
            }

            // 4. Get Foreign Keys with nullability
            var fkSql = @"
                SELECT 
                    tp.name AS ParentTable, cp.name AS ParentColumn, cp.is_nullable AS IsNullable,
                    tr.name AS RefTable, cr.name AS RefColumn,
                    sp.name AS ParentSchema, sr.name AS RefSchema
                FROM sys.foreign_keys fk
                INNER JOIN sys.tables tp ON fk.parent_object_id = tp.object_id
                INNER JOIN sys.schemas sp ON tp.schema_id = sp.schema_id
                INNER JOIN sys.tables tr ON fk.referenced_object_id = tr.object_id
                INNER JOIN sys.schemas sr ON tr.schema_id = sr.schema_id
                INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                INNER JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
                INNER JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id";

            var fks = await conn.QueryAsync<dynamic>(fkSql);

            foreach(var fk in fks)
            {
                var parentFull = $"[{fk.ParentSchema}].[{fk.ParentTable}]";
                var refFull = $"[{fk.RefSchema}].[{fk.RefTable}]";

                if (tableDict.TryGetValue(parentFull, out var parent) && tableDict.TryGetValue(refFull, out var reference))
                {
                    var rel = new Relationship 
                    { 
                        FromTable = parentFull, FromColumn = fk.ParentColumn,
                        ToTable = refFull, ToColumn = fk.RefColumn,
                        IsNullable = fk.IsNullable
                    };
                    
                    // Parent table has the foreign key pointing to Reference table
                    parent.OutgoingKeys.Add(rel);
                    
                    // Reference table is pointed to by Parent table
                    reference.IncomingKeys.Add(rel);
                }
            }

            // 5. Get View Dependencies
            var depSql = @"
                SELECT 
                    OBJECT_SCHEMA_NAME(d.referencing_id) AS SourceSchema,
                    OBJECT_NAME(d.referencing_id) AS SourceName,
                    ISNULL(d.referenced_schema_name, SCHEMA_NAME(o.schema_id)) AS TargetSchema,
                    d.referenced_entity_name AS TargetName
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                WHERE o.type = 'V'";

            var deps = await conn.QueryAsync<dynamic>(depSql);
            
            foreach(var dep in deps)
            {
                var sourceSchema = (string)dep.SourceSchema;
                var sourceName = (string)dep.SourceName;
                var targetSchema = (string)dep.TargetSchema; // Can be null if cross-db, but we use ISNULL above for basic cases
                var targetName = (string)dep.TargetName;

                if (string.IsNullOrEmpty(targetSchema)) continue; 

                var sourceFull = $"[{sourceSchema}].[{sourceName}]";
                var targetFull = $"[{targetSchema}].[{targetName}]";

                // Resolve Source (View)
                if (viewDict.TryGetValue(sourceFull, out var sourceView))
                {
                    // Target could be Table or View
                    if (tableDict.TryGetValue(targetFull, out var targetTable))
                    {
                        // View depends on Table
                        sourceView.Parents.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Table" });
                        // Table referenced by View
                        targetTable.ReferencedByViews.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "View" }); 
                    }
                    else if (viewDict.TryGetValue(targetFull, out var targetView))
                    {
                        // View depends on View
                        sourceView.Parents.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "View" });
                        targetView.Children.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "View" });
                    }
                }
            }

            // 6. Get Column Lineage for Views (Best Effort)
            // This can be slow for many views, so we'll do it sequentially or parrallel? 
            // Let's do it simply for now.
            Console.WriteLine("Fetching View Column Lineage...");
            foreach (var view in Views)
            {
                try 
                {
                    var lineageSql = $@"
                        SELECT 
                            c.name AS ViewColumn,
                            d.referenced_schema_name AS SourceSchema,
                            d.referenced_entity_name AS SourceTable,
                            d.referenced_minor_name AS SourceColumn
                        FROM sys.dm_sql_referenced_entities('{view.FullName}', 'OBJECT') d
                        JOIN sys.columns c ON c.object_id = d.referencing_id AND c.column_id = d.referencing_minor_id
                        WHERE d.referenced_minor_name IS NOT NULL";

                    var lineage = await conn.QueryAsync<dynamic>(lineageSql);
                    
                    var colDict = view.Columns.ToDictionary(c => c.Name);
                    
                    foreach(var item in lineage)
                    {
                        var colName = (string)item.ViewColumn;
                        if (colDict.TryGetValue(colName, out var col))
                        {
                            // If multiple sources map to one column (e.g. concatenation), this might overwrite or we could append.
                            // For now, let's just take the first one or formatted string.
                            if (string.IsNullOrEmpty(col.SourceTable))
                            {
                                col.SourceSchema = item.SourceSchema;
                                col.SourceTable = item.SourceTable;
                                col.SourceColumn = item.SourceColumn;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // View might be invalid or permissions issue
                    Console.WriteLine($"Error fetching lineage for {view.FullName}: {ex.Message}");
                }
            }

            // 7. Get Stored Procedures
            Console.WriteLine("Fetching Stored Procedures...");
            var spSql = @"
                SELECT 
                    s.name AS SchemaName,
                    p.name AS ProcName,
                    p.create_date AS CreatedDate,
                    p.modify_date AS ModifiedDate
                FROM sys.procedures p
                JOIN sys.schemas s ON p.schema_id = s.schema_id
                ORDER BY s.name, p.name";

            var rawSps = await conn.QueryAsync<dynamic>(spSql);
            StoredProcedures = rawSps.Select(r => new SpInfo
            {
                Schema = (string)r.SchemaName,
                Name = (string)r.ProcName,
                CreatedDate = (DateTime?)r.CreatedDate,
                ModifiedDate = (DateTime?)r.ModifiedDate
            }).ToList();
            var spDict = StoredProcedures.ToDictionary(sp => sp.FullName);

            // 8. Get SP Parameters
            Console.WriteLine("Fetching SP Parameters...");
            var paramSql = @"
                SELECT 
                    s.name AS SchemaName,
                    p.name AS ProcName,
                    pm.name AS ParamName,
                    t.name AS DataType,
                    pm.is_output AS IsOutput,
                    pm.has_default_value AS HasDefault,
                    CAST(pm.default_value AS NVARCHAR(256)) AS DefaultValue,
                    pm.max_length AS MaxLength,
                    pm.precision AS Precision,
                    pm.scale AS Scale
                FROM sys.procedures p
                JOIN sys.schemas s ON p.schema_id = s.schema_id
                JOIN sys.parameters pm ON pm.object_id = p.object_id
                JOIN sys.types t ON pm.user_type_id = t.user_type_id
                ORDER BY s.name, p.name, pm.parameter_id";

            var rawParams = await conn.QueryAsync<dynamic>(paramSql);
            foreach (var param in rawParams)
            {
                var spFull = $"[{(string)param.SchemaName}].[{(string)param.ProcName}]";
                if (spDict.TryGetValue(spFull, out var sp))
                {
                    // NVARCHAR max_length is stored in bytes (2 bytes/char), divide by 2
                    var rawLen = (short)param.MaxLength;
                    var dataType = ((string)param.DataType).ToUpper();
                    int displayLen = rawLen;
                    if (dataType is "NVARCHAR" or "NCHAR")
                        displayLen = rawLen == -1 ? -1 : rawLen / 2;

                    sp.Parameters.Add(new SpParameter
                    {
                        Name = (string)param.ParamName,
                        DataType = (string)param.DataType,
                        IsOutput = (bool)param.IsOutput,
                        HasDefault = (bool)param.HasDefault,
                        DefaultValue = param.DefaultValue as string,
                        MaxLength = displayLen,
                        Precision = (byte)param.Precision,
                        Scale = (byte)param.Scale
                    });
                }
            }

            // 9. Get SP Dependencies
            Console.WriteLine("Fetching SP Dependencies...");
            var spDepSql = @"
                SELECT 
                    OBJECT_SCHEMA_NAME(d.referencing_id) AS SourceSchema,
                    OBJECT_NAME(d.referencing_id) AS SourceName,
                    ISNULL(d.referenced_schema_name, SCHEMA_NAME(o.schema_id)) AS TargetSchema,
                    d.referenced_entity_name AS TargetName
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                WHERE o.type = 'P'";

            var spDeps = await conn.QueryAsync<dynamic>(spDepSql);
            foreach (var dep in spDeps)
            {
                var sourceSchema = (string)dep.SourceSchema;
                var sourceName = (string)dep.SourceName;
                var targetSchema = dep.TargetSchema as string;
                var targetName = (string)dep.TargetName;

                if (string.IsNullOrEmpty(targetSchema)) continue;

                var sourceFull = $"[{sourceSchema}].[{sourceName}]";
                var targetFull = $"[{targetSchema}].[{targetName}]";

                if (!spDict.TryGetValue(sourceFull, out var sourceSp)) continue;

                // Avoid duplicate dependencies
                if (sourceSp.Dependencies.Any(d => d.FullName == targetFull)) continue;

                if (tableDict.TryGetValue(targetFull, out var depTable))
                {
                    sourceSp.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Table" });
                    depTable.ReferencedBySPs.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "SP" });
                }
                else if (viewDict.TryGetValue(targetFull, out var depView))
                {
                    sourceSp.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "View" });
                    depView.ReferencedBySPs.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "SP" });
                }
            }

            OnSchemaLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Schema Load Error: {ex.Message}");
            // Handle error (maybe invoke an error event)
        }
    }
}
