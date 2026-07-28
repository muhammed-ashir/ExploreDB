using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace ExploreDB.Services;

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
    // Tables, Views, and SPs this SP references
    public List<Dependency> Dependencies { get; set; } = new();
    // Stored Procedures that reference this SP
    public List<Dependency> ReferencedBySPs { get; set; } = new();

    public override string ToString() => FullName;
}

public class FunctionParameter
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsOutput { get; set; } = false;
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

public class FunctionInfo
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{Name}]";
    public string FunctionType { get; set; } = string.Empty; // e.g. Scalar, Inline Table, Multi-Statement Table
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public List<FunctionParameter> Parameters { get; set; } = new();
    public string? ReturnType { get; set; } // Only for Scalar functions
    public List<Dependency> Dependencies { get; set; } = new();
    public List<Dependency> ReferencedBy { get; set; } = new();

    public override string ToString() => FullName;
}

public class TypeInfo
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName => $"[{Schema}].[{Name}]";
    public bool IsTableType { get; set; } = false;
    public string? BaseType { get; set; } // For alias types
    public int MaxLength { get; set; } = -1;
    public int Precision { get; set; } = 0;
    public int Scale { get; set; } = 0;
    
    // For Table Types
    public List<ColumnInfo> Columns { get; set; } = new();

    public override string ToString() => FullName;
}

public class SchemaService
{
    private readonly ConnectionService _connectionService;
    public List<TableInfo> Tables { get; private set; } = new();
    public List<ViewInfo> Views { get; private set; } = new();
    public List<SpInfo> StoredProcedures { get; private set; } = new();
    public List<FunctionInfo> Functions { get; private set; } = new();
    public List<TypeInfo> Types { get; private set; } = new();
    public event Action? OnSchemaLoaded;

    private readonly ILogger<SchemaService> _logger;

    public SchemaService(ConnectionService connectionService, ILogger<SchemaService> logger)
    {
        _connectionService = connectionService;
        _logger = logger;
    }

    public async Task<string?> GetStoredProcedureDefinitionAsync(string fullName)
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return null;

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            var sql = "SELECT OBJECT_DEFINITION(OBJECT_ID(@FullName))";
            return await conn.QuerySingleOrDefaultAsync<string>(sql, new { FullName = fullName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching SP definition for {FullName}", fullName);
            return null;
        }
    }

    public async Task<string?> GetFunctionDefinitionAsync(string fullName)
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return null;

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            var sql = "SELECT OBJECT_DEFINITION(OBJECT_ID(@FullName))";
            return await conn.QuerySingleOrDefaultAsync<string>(sql, new { FullName = fullName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Function definition for {FullName}", fullName);
            return null;
        }
    }

    public async Task LoadSchemaAsync()
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();

            _logger.LogInformation("Executing Batch Schema Query...");
            
            var batchSql = @"
                -- 1. Tables
                SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS Name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE';

                -- 2. Views
                SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS Name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='VIEW';

                -- 3. Columns
                SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName, DATA_TYPE AS DataType FROM INFORMATION_SCHEMA.COLUMNS;

                -- 4. Foreign Keys
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
                INNER JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
                OPTION (FORCE ORDER);

                -- 5. View Dependencies
                SELECT 
                    OBJECT_SCHEMA_NAME(d.referencing_id) AS SourceSchema,
                    OBJECT_NAME(d.referencing_id) AS SourceName,
                    ISNULL(d.referenced_schema_name, SCHEMA_NAME(o.schema_id)) AS TargetSchema,
                    d.referenced_entity_name AS TargetName
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                WHERE o.type = 'V';

                -- 6. Stored Procedures
                SELECT 
                    s.name AS SchemaName, p.name AS ProcName, p.create_date AS CreatedDate, p.modify_date AS ModifiedDate
                FROM sys.procedures p
                JOIN sys.schemas s ON p.schema_id = s.schema_id
                WHERE p.is_ms_shipped = 0
                ORDER BY s.name, p.name;

                -- 7. SP Parameters
                SELECT 
                    s.name AS SchemaName, p.name AS ProcName, pm.name AS ParamName, t.name AS DataType,
                    pm.is_output AS IsOutput, pm.has_default_value AS HasDefault,
                    CAST(pm.default_value AS NVARCHAR(256)) AS DefaultValue,
                    pm.max_length AS MaxLength, pm.precision AS Precision, pm.scale AS Scale
                FROM sys.procedures p
                JOIN sys.schemas s ON p.schema_id = s.schema_id
                JOIN sys.parameters pm ON pm.object_id = p.object_id
                JOIN sys.types t ON pm.user_type_id = t.user_type_id
                WHERE p.is_ms_shipped = 0
                ORDER BY s.name, p.name, pm.parameter_id;

                -- 8. SP Dependencies
                SELECT 
                    OBJECT_SCHEMA_NAME(d.referencing_id) AS SourceSchema,
                    OBJECT_NAME(d.referencing_id) AS SourceName,
                    ISNULL(d.referenced_schema_name, SCHEMA_NAME(o.schema_id)) AS TargetSchema,
                    d.referenced_entity_name AS TargetName
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                WHERE o.type = 'P';

                -- 9. Functions
                SELECT 
                    s.name AS SchemaName, o.name AS FuncName, o.type AS FuncType, o.create_date AS CreatedDate, o.modify_date AS ModifiedDate
                FROM sys.objects o
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE o.type IN ('FN', 'IF', 'TF') AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name;

                -- 10. Function Parameters
                SELECT 
                    s.name AS SchemaName, o.name AS FuncName, pm.name AS ParamName, t.name AS DataType,
                    pm.is_output AS IsOutput, pm.max_length AS MaxLength, pm.precision AS Precision, pm.scale AS Scale, pm.parameter_id AS ParamId
                FROM sys.objects o
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                JOIN sys.parameters pm ON pm.object_id = o.object_id
                JOIN sys.types t ON pm.user_type_id = t.user_type_id
                WHERE o.type IN ('FN', 'IF', 'TF') AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name, pm.parameter_id;

                -- 11. Function Dependencies
                SELECT 
                    OBJECT_SCHEMA_NAME(d.referencing_id) AS SourceSchema,
                    OBJECT_NAME(d.referencing_id) AS SourceName,
                    ISNULL(d.referenced_schema_name, SCHEMA_NAME(o.schema_id)) AS TargetSchema,
                    d.referenced_entity_name AS TargetName
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                WHERE o.type IN ('FN', 'IF', 'TF');

                -- 12. View/SP to Function Refs
                SELECT 
                    OBJECT_SCHEMA_NAME(d.referencing_id) AS SourceSchema,
                    OBJECT_NAME(d.referencing_id) AS SourceName,
                    o.type AS SourceObjType,
                    ISNULL(d.referenced_schema_name, SCHEMA_NAME(ro.schema_id)) AS TargetSchema,
                    d.referenced_entity_name AS TargetName
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                LEFT JOIN sys.objects ro ON d.referenced_id = ro.object_id
                WHERE ro.type IN ('FN', 'IF', 'TF') AND o.type IN ('V', 'P');

                -- 13. Types
                SELECT 
                    s.name AS SchemaName, t.name AS TypeName, t.is_table_type AS IsTableType,
                    bt.name AS BaseTypeName, t.max_length AS MaxLength, t.precision AS Precision, t.scale AS Scale, tt.type_table_object_id AS TypeTableObjId
                FROM sys.types t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN sys.types bt ON t.system_type_id = bt.user_type_id AND bt.is_user_defined = 0
                LEFT JOIN sys.table_types tt ON t.user_type_id = tt.user_type_id
                WHERE t.is_user_defined = 1
                ORDER BY s.name, t.name;

                -- 14. Type Columns
                SELECT 
                    c.object_id AS ObjectId, c.name AS ColumnName, t.name AS DataType
                FROM sys.columns c
                JOIN sys.types t ON c.user_type_id = t.user_type_id
                JOIN sys.table_types tt ON c.object_id = tt.type_table_object_id;
            ";

            using var multi = await conn.QueryMultipleAsync(batchSql, commandTimeout: 600);

            _logger.LogInformation("Processing Batch Results...");
            
            // 1. Tables
            var rawTables = await multi.ReadAsync<dynamic>();
            Tables = rawTables.Select(t => new TableInfo { Schema = (string)t.SchemaName, Name = (string)t.Name }).OrderBy(t => t.FullName).ToList();
            var tableDict = Tables.ToDictionary(t => t.FullName);

            // 2. Views
            var rawViews = await multi.ReadAsync<dynamic>();
            Views = rawViews.Select(v => new ViewInfo { Schema = (string)v.SchemaName, Name = (string)v.Name }).OrderBy(v => v.FullName).ToList();
            var viewDict = Views.ToDictionary(v => v.FullName);

            // 3. Columns
            var rawCols = await multi.ReadAsync<dynamic>();
            foreach(var col in rawCols)
            {
                var key = $"[{(string)col.SchemaName}].[{(string)col.TableName}]";
                if (tableDict.TryGetValue(key, out var table))
                {
                    table.Columns.Add(new ColumnInfo { Name = (string)col.ColumnName, DataType = (string)col.DataType });
                }
                else if (viewDict.TryGetValue(key, out var view))
                {
                    view.Columns.Add(new ColumnInfo { Name = (string)col.ColumnName, DataType = (string)col.DataType });
                }
            }

            // 4. Foreign Keys
            var fks = await multi.ReadAsync<dynamic>();
            foreach(var fk in fks)
            {
                var parentFull = $"[{(string)fk.ParentSchema}].[{(string)fk.ParentTable}]";
                var refFull = $"[{(string)fk.RefSchema}].[{(string)fk.RefTable}]";

                if (tableDict.TryGetValue(parentFull, out var parent) && tableDict.TryGetValue(refFull, out var reference))
                {
                    var rel = new Relationship 
                    { 
                        FromTable = parentFull, FromColumn = (string)fk.ParentColumn,
                        ToTable = refFull, ToColumn = (string)fk.RefColumn,
                        IsNullable = (bool)fk.IsNullable
                    };
                    parent.OutgoingKeys.Add(rel);
                    reference.IncomingKeys.Add(rel);
                }
            }

            // 5. View Dependencies
            var viewDeps = await multi.ReadAsync<dynamic>();
            foreach(var dep in viewDeps)
            {
                var sourceSchema = (string)dep.SourceSchema;
                var sourceName = (string)dep.SourceName;
                var targetSchema = (string)dep.TargetSchema;
                var targetName = (string)dep.TargetName;

                if (string.IsNullOrEmpty(targetSchema)) continue; 

                var sourceFull = $"[{sourceSchema}].[{sourceName}]";
                var targetFull = $"[{targetSchema}].[{targetName}]";

                if (viewDict.TryGetValue(sourceFull, out var sourceView))
                {
                    if (tableDict.TryGetValue(targetFull, out var targetTable))
                    {
                        sourceView.Parents.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Table" });
                        targetTable.ReferencedByViews.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "View" }); 
                    }
                    else if (viewDict.TryGetValue(targetFull, out var targetView))
                    {
                        sourceView.Parents.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "View" });
                        targetView.Children.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "View" });
                    }
                }
            }

            // 6. Stored Procedures
            var rawSps = await multi.ReadAsync<dynamic>();
            StoredProcedures = rawSps.Select(r => new SpInfo
            {
                Schema = (string)r.SchemaName,
                Name = (string)r.ProcName,
                CreatedDate = (DateTime?)r.CreatedDate,
                ModifiedDate = (DateTime?)r.ModifiedDate
            }).ToList();
            var spDict = StoredProcedures.ToDictionary(sp => sp.FullName);

            // 7. SP Parameters
            var rawParams = await multi.ReadAsync<dynamic>();
            foreach (var param in rawParams)
            {
                var spFull = $"[{(string)param.SchemaName}].[{(string)param.ProcName}]";
                if (spDict.TryGetValue(spFull, out var sp))
                {
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

            // 8. SP Dependencies
            var spDeps = await multi.ReadAsync<dynamic>();
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
                if (sourceSp.Dependencies.Any(d => d.FullName == targetFull)) continue;

                if (tableDict.TryGetValue(targetFull, out var depTable))
                {
                    sourceSp.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Table" });
                    depTable.ReferencedBySPs.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "SP" });
                }
                else if (viewDict.TryGetValue(targetFull, out var depView))
                {
                    sourceSp.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "View" });
                    depView.ReferencedBySPs.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "Procedure" });
                }
                else if (spDict.TryGetValue(targetFull, out var depSp))
                {
                    sourceSp.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Procedure" });
                    depSp.ReferencedBySPs.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "Procedure" });
                }
            }

            // 9. Functions
            var rawFns = await multi.ReadAsync<dynamic>();
            Functions = rawFns.Select(r => new FunctionInfo
            {
                Schema = (string)r.SchemaName,
                Name = (string)r.FuncName,
                FunctionType = (string)r.FuncType switch {
                    "FN" => "Scalar",
                    "IF" => "Inline Table",
                    "TF" => "Multi-Statement Table",
                    _ => (string)r.FuncType
                },
                CreatedDate = (DateTime?)r.CreatedDate,
                ModifiedDate = (DateTime?)r.ModifiedDate
            }).ToList();
            var fnDict = Functions.ToDictionary(f => f.FullName);

            // 10. Function Parameters
            var rawFnParams = await multi.ReadAsync<dynamic>();
            foreach (var param in rawFnParams)
            {
                var fnFull = $"[{(string)param.SchemaName}].[{(string)param.FuncName}]";
                if (fnDict.TryGetValue(fnFull, out var fn))
                {
                    var rawLen = (short)param.MaxLength;
                    var dataType = ((string)param.DataType).ToUpper();
                    int displayLen = rawLen;
                    if (dataType is "NVARCHAR" or "NCHAR")
                        displayLen = rawLen == -1 ? -1 : rawLen / 2;
                    
                    var paramId = (int)param.ParamId;
                    var isOutput = (bool)param.IsOutput;
                    
                    if (paramId == 0)
                    {
                        var displayType = dataType;
                        if (displayType is "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR")
                            displayType = displayLen == -1 ? $"{dataType}(MAX)" : $"{dataType}({displayLen})";
                        else if (displayType is "DECIMAL" or "NUMERIC")
                            displayType = $"{dataType}({(byte)param.Precision},{(byte)param.Scale})";
                            
                        fn.ReturnType = displayType;
                    }
                    else
                    {
                        fn.Parameters.Add(new FunctionParameter
                        {
                            Name = string.IsNullOrEmpty(param.ParamName as string) ? $"@param{paramId}" : (string)param.ParamName,
                            DataType = (string)param.DataType,
                            IsOutput = isOutput,
                            MaxLength = displayLen,
                            Precision = (byte)param.Precision,
                            Scale = (byte)param.Scale
                        });
                    }
                }
            }

            // 11. Function Dependencies
            var fnDeps = await multi.ReadAsync<dynamic>();
            foreach (var dep in fnDeps)
            {
                var sourceSchema = (string)dep.SourceSchema;
                var sourceName = (string)dep.SourceName;
                var targetSchema = dep.TargetSchema as string;
                var targetName = (string)dep.TargetName;

                if (string.IsNullOrEmpty(targetSchema)) continue;
                var sourceFull = $"[{sourceSchema}].[{sourceName}]";
                var targetFull = $"[{targetSchema}].[{targetName}]";

                if (!fnDict.TryGetValue(sourceFull, out var sourceFn)) continue;
                if (sourceFn.Dependencies.Any(d => d.FullName == targetFull)) continue;

                if (tableDict.TryGetValue(targetFull, out var depTable))
                    sourceFn.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Table" });
                else if (viewDict.TryGetValue(targetFull, out var depView))
                    sourceFn.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "View" });
                else if (spDict.TryGetValue(targetFull, out var depSp))
                    sourceFn.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Procedure" });
                else if (fnDict.TryGetValue(targetFull, out var depFn))
                {
                    sourceFn.Dependencies.Add(new Dependency { Schema = targetSchema, Name = targetName, Type = "Function" });
                    depFn.ReferencedBy.Add(new Dependency { Schema = sourceSchema, Name = sourceName, Type = "Function" });
                }
            }

            // 12. View/SP to Function Refs
            var refs = await multi.ReadAsync<dynamic>();
            foreach(var r in refs)
            {
                var srcType = (string)r.SourceObjType == "V" ? "View" : "Procedure";
                var tFull = $"[{(string)r.TargetSchema}].[{(string)r.TargetName}]";
                if(fnDict.TryGetValue(tFull, out var targetFn))
                {
                    var sourceFullName = $"[{(string)r.SourceSchema}].[{(string)r.SourceName}]";
                    if(!targetFn.ReferencedBy.Any(d => d.FullName == sourceFullName))
                    {
                        targetFn.ReferencedBy.Add(new Dependency { Schema = (string)r.SourceSchema, Name = (string)r.SourceName, Type = srcType });
                    }
                }
            }

            // 13. Types
            var rawTypes = await multi.ReadAsync<dynamic>();
            var typeTableMap = new Dictionary<int, TypeInfo>();
            
            Types = new List<TypeInfo>();
            foreach (var r in rawTypes)
            {
                var t = new TypeInfo
                {
                    Schema = (string)r.SchemaName,
                    Name = (string)r.TypeName,
                    IsTableType = (bool)r.IsTableType,
                    BaseType = r.BaseTypeName as string,
                    MaxLength = (short)r.MaxLength,
                    Precision = (byte)r.Precision,
                    Scale = (byte)r.Scale
                };
                
                if (t.IsTableType)
                {
                    int typeObjId = (int)r.TypeTableObjId;
                    typeTableMap[typeObjId] = t;
                }
                
                Types.Add(t);
            }

            // 14. Type Columns
            var typeCols = await multi.ReadAsync<dynamic>();
            foreach (var col in typeCols)
            {
                int objId = (int)col.ObjectId;
                if (typeTableMap.TryGetValue(objId, out var typeInfo))
                {
                    typeInfo.Columns.Add(new ColumnInfo
                    {
                        Name = (string)col.ColumnName,
                        DataType = (string)col.DataType
                    });
                }
            }

            // Parallel View Lineage Fetching
            _logger.LogInformation("Fetching View Column Lineage in Parallel...");
            
            // Limit concurrency so we don't overwhelm the DB pool
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
            
            await Parallel.ForEachAsync(Views, parallelOptions, async (view, cancellationToken) =>
            {
                try 
                {
                    // Need a separate connection for each concurrent task
                    using var viewConn = new SqlConnection(_connectionService.ConnectionString);
                    
                    var lineageSql = $@"
                        SELECT 
                            ISNULL(d.referenced_schema_name, 'dbo') AS SourceSchema,
                            d.referenced_entity_name AS SourceTable,
                            d.referenced_minor_name AS SourceColumn
                        FROM sys.dm_sql_referenced_entities('{view.FullName}', 'OBJECT') d
                        WHERE d.referenced_minor_name IS NOT NULL";

                    var lineage = await viewConn.QueryAsync<dynamic>(lineageSql, commandTimeout: 300);
                    
                    var colDict = view.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
                    
                    foreach(var item in lineage)
                    {
                        var sourceColName = (string)item.SourceColumn;
                        if (colDict.TryGetValue(sourceColName, out var col))
                        {
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
                    _logger.LogWarning(ex, "Error fetching lineage for {ViewFullName}", view.FullName);
                }
            });

            OnSchemaLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema Load Error"); 
        }
    }
}
