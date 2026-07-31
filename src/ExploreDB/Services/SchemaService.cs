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

    public bool AreDependenciesLoaded { get; set; } = false;

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
    // Stored Procedures and Functions that reference this view
    public List<Dependency> ReferencedBySPs { get; set; } = new();
    
    public bool AreDependenciesLoaded { get; set; } = false;

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

    public bool AreDependenciesLoaded { get; set; } = false;

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

    public bool AreDependenciesLoaded { get; set; } = false;

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
    
    // Dependencies
    public List<Dependency> UsedBy { get; set; } = new();
    public bool AreDependenciesLoaded { get; set; } = false;

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
    public event Action<string>? OnError;

    public bool AreStoredProceduresLoaded { get; private set; } = false;
    public bool AreTypesLoaded { get; private set; } = false;

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

            _logger.LogInformation("Executing Initial Batch Schema Query (Perfect Compromise Architecture)...");
            
            var batchSql = @"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

                -- 1. Tables
                SELECT s.name AS SchemaName, t.name AS Name
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id;

                -- 2. Views
                SELECT s.name AS SchemaName, v.name AS Name
                FROM sys.views v
                INNER JOIN sys.schemas s ON v.schema_id = s.schema_id;

                -- 3. Columns (For both Tables and Views)
                SELECT s.name AS SchemaName, o.name AS TableName, c.name AS ColumnName, ty.name AS DataType
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                WHERE o.type IN ('U', 'V');

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

                -- 5. Functions
                SELECT 
                    s.name AS SchemaName, o.name AS FuncName, o.type AS FuncType, o.create_date AS CreatedDate, o.modify_date AS ModifiedDate
                FROM sys.objects o
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE o.type IN ('FN', 'IF', 'TF') AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name;

                -- 6. Function Parameters
                SELECT 
                    s.name AS SchemaName, o.name AS FuncName, pm.name AS ParamName, t.name AS DataType,
                    pm.is_output AS IsOutput, pm.max_length AS MaxLength, pm.precision AS Precision, pm.scale AS Scale, pm.parameter_id AS ParamId
                FROM sys.objects o
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                JOIN sys.parameters pm ON pm.object_id = o.object_id
                JOIN sys.types t ON pm.user_type_id = t.user_type_id
                WHERE o.type IN ('FN', 'IF', 'TF') AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name, pm.parameter_id;
            ";

            using var multi = await conn.QueryMultipleAsync(batchSql, commandTimeout: 600);

            _logger.LogInformation("Processing Batch Results...");
            
            // 1. Tables
            var rawTables = await multi.ReadAsync<RawTable>();
            Tables = rawTables.Select(t => new TableInfo { Schema = t.SchemaName, Name = t.Name }).OrderBy(t => t.FullName).ToList();
            var tableDict = Tables.ToDictionary(t => t.FullName);

            // 2. Views
            var rawViews = await multi.ReadAsync<RawView>();
            Views = rawViews.Select(v => new ViewInfo { Schema = v.SchemaName, Name = v.Name }).OrderBy(v => v.FullName).ToList();
            var viewDict = Views.ToDictionary(v => v.FullName);

            // 3. Columns
            var rawCols = await multi.ReadAsync<RawColumn>();
            foreach(var col in rawCols)
            {
                var key = $"[{col.SchemaName}].[{col.TableName}]";
                if (tableDict.TryGetValue(key, out var table))
                {
                    table.Columns.Add(new ColumnInfo { Name = col.ColumnName, DataType = col.DataType });
                }
                else if (viewDict.TryGetValue(key, out var view))
                {
                    view.Columns.Add(new ColumnInfo { Name = col.ColumnName, DataType = col.DataType });
                }
            }

            // 4. Foreign Keys
            var fks = await multi.ReadAsync<RawFk>();
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
                    parent.OutgoingKeys.Add(rel);
                    reference.IncomingKeys.Add(rel);
                }
            }

            // 5. Functions
            var rawFns = await multi.ReadAsync<RawFunction>();
            Functions = rawFns.Select(r => new FunctionInfo
            {
                Schema = r.SchemaName,
                Name = r.FuncName,
                FunctionType = r.FuncType switch {
                    "FN" => "Scalar",
                    "IF" => "Inline Table",
                    "TF" => "Multi-Statement Table",
                    _ => r.FuncType
                },
                CreatedDate = r.CreatedDate,
                ModifiedDate = r.ModifiedDate
            }).ToList();
            var fnDict = Functions.ToDictionary(f => f.FullName);

            // 6. Function Parameters
            var rawFnParams = await multi.ReadAsync<RawFnParam>();
            foreach (var param in rawFnParams)
            {
                var fnFull = $"[{param.SchemaName}].[{param.FuncName}]";
                if (fnDict.TryGetValue(fnFull, out var fn))
                {
                    var rawLen = param.MaxLength;
                    var dataType = param.DataType.ToUpper();
                    int displayLen = rawLen;
                    if (dataType is "NVARCHAR" or "NCHAR")
                        displayLen = rawLen == -1 ? -1 : rawLen / 2;
                    
                    var paramId = param.ParamId;
                    var isOutput = param.IsOutput;
                    
                    if (paramId == 0)
                    {
                        var displayType = dataType;
                        if (displayType is "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR")
                            displayType = displayLen == -1 ? $"{dataType}(MAX)" : $"{dataType}({displayLen})";
                        else if (displayType is "DECIMAL" or "NUMERIC")
                            displayType = $"{dataType}({param.Precision},{param.Scale})";
                            
                        fn.ReturnType = displayType;
                    }
                    else
                    {
                        fn.Parameters.Add(new FunctionParameter
                        {
                            Name = string.IsNullOrEmpty(param.ParamName) ? $"@param{paramId}" : param.ParamName,
                            DataType = param.DataType,
                            IsOutput = isOutput,
                            MaxLength = displayLen,
                            Precision = param.Precision,
                            Scale = param.Scale
                        });
                    }
                }
            }

            // Reset lazy loading flags
            StoredProcedures.Clear();
            Types.Clear();
            AreStoredProceduresLoaded = false;
            AreTypesLoaded = false;

            OnSchemaLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema Load Error"); 
            OnError?.Invoke($"Error loading schema: {ex.Message}");
        }
    }

    public async Task LoadStoredProceduresAsync()
    {
        if (AreStoredProceduresLoaded || string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();
            
            var batchSql = @"
                -- 1. Stored Procedures
                SELECT 
                    s.name AS SchemaName, p.name AS ProcName, p.create_date AS CreatedDate, p.modify_date AS ModifiedDate
                FROM sys.procedures p
                JOIN sys.schemas s ON p.schema_id = s.schema_id
                WHERE p.is_ms_shipped = 0
                ORDER BY s.name, p.name;

                -- 2. SP Parameters
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
            ";

            using var multi = await conn.QueryMultipleAsync(batchSql, commandTimeout: 300);

            var rawSps = await multi.ReadAsync<dynamic>();
            StoredProcedures = rawSps.Select(r => new SpInfo
            {
                Schema = (string)r.SchemaName,
                Name = (string)r.ProcName,
                CreatedDate = (DateTime?)r.CreatedDate,
                ModifiedDate = (DateTime?)r.ModifiedDate
            }).ToList();
            var spDict = StoredProcedures.ToDictionary(sp => sp.FullName);

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

            AreStoredProceduresLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading stored procedures lazily");
            OnError?.Invoke($"Error loading stored procedures: {ex.Message}");
        }
    }

    public async Task LoadTypesAsync()
    {
        if (AreTypesLoaded || string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();
            
            var batchSql = @"
                -- 1. Types
                SELECT 
                    s.name AS SchemaName, t.name AS TypeName, t.is_table_type AS IsTableType,
                    bt.name AS BaseTypeName, t.max_length AS MaxLength, t.precision AS Precision, t.scale AS Scale, tt.type_table_object_id AS TypeTableObjId
                FROM sys.types t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN sys.types bt ON t.system_type_id = bt.user_type_id AND bt.is_user_defined = 0
                LEFT JOIN sys.table_types tt ON t.user_type_id = tt.user_type_id
                WHERE t.is_user_defined = 1 AND t.is_assembly_type = 0
                ORDER BY s.name, t.name;

                -- 2. Type Columns
                SELECT 
                    c.object_id AS ObjectId, c.name AS ColumnName, t.name AS DataType
                FROM sys.columns c
                JOIN sys.types t ON c.user_type_id = t.user_type_id
                JOIN sys.table_types tt ON c.object_id = tt.type_table_object_id;
            ";

            using var multi = await conn.QueryMultipleAsync(batchSql, commandTimeout: 300);

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

            AreTypesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading types lazily");
            OnError?.Invoke($"Error loading types: {ex.Message}");
        }
    }

    public async Task LoadTypeDependenciesAsync(string fullName)
    {
        var typeInfo = Types.FirstOrDefault(t => t.FullName == fullName);
        if (typeInfo == null || typeInfo.AreDependenciesLoaded || string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionService.ConnectionString);
            var sql = @"
                DECLARE @TypeId INT;
                SELECT @TypeId = user_type_id FROM sys.types WHERE name = @Name AND schema_id = SCHEMA_ID(@Schema);

                IF @TypeId IS NOT NULL
                BEGIN
                    SELECT DISTINCT SchemaName, ObjectName, ObjectType
                    FROM (
                        SELECT s.name AS SchemaName, o.name AS ObjectName, 
                               CASE WHEN o.type = 'V' THEN 'View' ELSE 'Table' END AS ObjectType
                        FROM sys.columns c
                        JOIN sys.objects o ON c.object_id = o.object_id
                        JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE c.user_type_id = @TypeId AND o.type IN ('U', 'V')
                        
                        UNION ALL
                        
                        SELECT s.name AS SchemaName, o.name AS ObjectName, 
                               CASE WHEN o.type IN ('P', 'PC') THEN 'SP' ELSE 'Function' END AS ObjectType
                        FROM sys.parameters p
                        JOIN sys.objects o ON p.object_id = o.object_id
                        JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE p.user_type_id = @TypeId
                        
                        UNION ALL
                        
                        SELECT s.name AS SchemaName, o.name AS ObjectName, 
                               CASE WHEN o.type IN ('P', 'PC') THEN 'SP'
                                    WHEN o.type = 'V' THEN 'View'
                                    WHEN o.type IN ('FN', 'IF', 'TF') THEN 'Function'
                                    ELSE 'Other' END AS ObjectType
                        FROM sys.sql_expression_dependencies d
                        JOIN sys.objects o ON d.referencing_id = o.object_id
                        JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE d.referenced_class = 6 AND d.referenced_id = @TypeId
                    ) AS results
                    ORDER BY ObjectType, SchemaName, ObjectName;
                END
            ";

            var results = await conn.QueryAsync<dynamic>(sql, new { Schema = typeInfo.Schema, Name = typeInfo.Name }, commandTimeout: 300);
            
            typeInfo.UsedBy = results.Select(r => new Dependency
            {
                Schema = (string)r.SchemaName,
                Name = (string)r.ObjectName,
                Type = (string)r.ObjectType
            }).ToList();

            typeInfo.AreDependenciesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dependencies for type {Type}", fullName);
            OnError?.Invoke($"Error fetching dependencies for Type {fullName}: {ex.Message}");
        }
    }

    public async Task LoadTableDependenciesAsync(string fullName)
    {
        var table = Tables.FirstOrDefault(t => t.FullName == fullName);
        if (table == null || table.AreDependenciesLoaded || string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            
            var sql = @"
                DECLARE @RefId INT = OBJECT_ID(@FullName);

                SELECT DISTINCT
                    s.name AS SourceSchema,
                    o.name AS SourceName,
                    o.type AS SourceType
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE d.referenced_id = @RefId
                OPTION (RECOMPILE)";

            var deps = await conn.QueryAsync<dynamic>(sql, new { FullName = fullName }, commandTimeout: 300);
            
            table.ReferencedByViews.Clear();
            table.ReferencedBySPs.Clear();

            foreach(var dep in deps)
            {
                var schema = dep.SourceSchema as string;
                var name = dep.SourceName as string;
                var type = (dep.SourceType as string)?.Trim();

                if (type == "V")
                    table.ReferencedByViews.Add(new Dependency { Schema = schema, Name = name, Type = "View" });
                else if (type == "P")
                    table.ReferencedBySPs.Add(new Dependency { Schema = schema, Name = name, Type = "Procedure" });
                else if (type == "FN" || type == "IF" || type == "TF")
                    table.ReferencedBySPs.Add(new Dependency { Schema = schema, Name = name, Type = "Function" });
            }

            table.AreDependenciesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dependencies for table {FullName}", fullName);
            OnError?.Invoke($"Error fetching dependencies for table {fullName}: {ex.Message}");
        }
    }

    public async Task LoadViewLineageAndDependenciesAsync(string fullName)
    {
        var view = Views.FirstOrDefault(v => v.FullName == fullName);
        if (view == null || view.AreDependenciesLoaded || string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            
            // 1. Column Lineage
            var lineageSql = $@"
                SELECT 
                    ISNULL(d.referenced_schema_name, 'dbo') AS SourceSchema,
                    d.referenced_entity_name AS SourceTable,
                    d.referenced_minor_name AS SourceColumn
                FROM sys.dm_sql_referenced_entities('{view.FullName}', 'OBJECT') d
                WHERE d.referenced_minor_name IS NOT NULL";

            var lineage = await conn.QueryAsync<dynamic>(lineageSql, commandTimeout: 300);
            var colDict = view.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            
            foreach(var item in lineage)
            {
                var sourceColName = (string)item.SourceColumn;
                if (colDict.TryGetValue(sourceColName, out var col) && string.IsNullOrEmpty(col.SourceTable))
                {
                    col.SourceSchema = item.SourceSchema;
                    col.SourceTable = item.SourceTable;
                    col.SourceColumn = item.SourceColumn;
                }
            }

            // 2. Dependencies (What this view references, and what references this view)
            var depsSql = @"
                DECLARE @RefId INT = OBJECT_ID(@FullName);

                -- What it references (Parents)
                SELECT DISTINCT
                    ISNULL(d.referenced_schema_name, s.name) AS TargetSchema,
                    d.referenced_entity_name AS TargetName,
                    ro.type AS TargetType
                FROM sys.sql_expression_dependencies d
                LEFT JOIN sys.objects ro ON d.referenced_id = ro.object_id
                LEFT JOIN sys.schemas s ON ro.schema_id = s.schema_id
                WHERE d.referencing_id = @RefId
                OPTION (RECOMPILE);

                -- What references it (Children/ReferencedBySPs)
                SELECT DISTINCT
                    s.name AS SourceSchema,
                    o.name AS SourceName,
                    o.type AS SourceType
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE d.referenced_id = @RefId
                OPTION (RECOMPILE);
            ";

            using var multi = await conn.QueryMultipleAsync(depsSql, new { FullName = fullName }, commandTimeout: 300);
            
            var parents = await multi.ReadAsync<dynamic>();
            view.Parents.Clear();
            foreach(var p in parents)
            {
                var tSchema = p.TargetSchema as string;
                var tName = (string)p.TargetName;
                var tType = (string)p.TargetType;
                tType = tType?.Trim();
                if (!string.IsNullOrEmpty(tSchema))
                {
                    string depType = tType == "V" ? "View" : (tType == "U" ? "Table" : "Function");
                    view.Parents.Add(new Dependency { Schema = tSchema, Name = tName, Type = depType });
                }
            }

            var children = await multi.ReadAsync<dynamic>();
            view.Children.Clear();
            view.ReferencedBySPs.Clear();
            foreach(var c in children)
            {
                var sSchema = (string)c.SourceSchema;
                var sName = (string)c.SourceName;
                var sType = (string)c.SourceType;
                sType = sType?.Trim();

                if (sType == "V")
                    view.Children.Add(new Dependency { Schema = sSchema, Name = sName, Type = "View" });
                else if (sType == "P")
                    view.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Procedure" });
                else if (sType is "FN" or "IF" or "TF")
                    view.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Function" });
            }

            view.AreDependenciesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching lineage and dependencies for view {FullName}", fullName);
            OnError?.Invoke($"Error fetching dependencies for view {fullName}: {ex.Message}");
        }
    }

    public async Task LoadSpDependenciesAsync(string fullName)
    {
        var sp = StoredProcedures.FirstOrDefault(s => s.FullName == fullName);
        if (sp == null || sp.AreDependenciesLoaded || string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            
            var depsSql = @"
                DECLARE @RefId INT = OBJECT_ID(@FullName);

                -- What it references (Dependencies)
                SELECT DISTINCT
                    ISNULL(d.referenced_schema_name, s.name) AS TargetSchema,
                    d.referenced_entity_name AS TargetName,
                    ro.type AS TargetType,
                    d.referenced_class AS ReferencedClass
                FROM sys.sql_expression_dependencies d
                LEFT JOIN sys.objects ro ON d.referenced_id = ro.object_id
                LEFT JOIN sys.schemas s ON ro.schema_id = s.schema_id
                WHERE d.referencing_id = @RefId
                OPTION (RECOMPILE);

                -- What references it (ReferencedBySPs)
                SELECT DISTINCT
                    s.name AS SourceSchema,
                    o.name AS SourceName,
                    o.type AS SourceType
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE d.referenced_id = @RefId
                OPTION (RECOMPILE);
            ";

            using var multi = await conn.QueryMultipleAsync(depsSql, new { FullName = fullName }, commandTimeout: 300);
            
            var deps = await multi.ReadAsync<dynamic>();
            sp.Dependencies.Clear();
            foreach(var p in deps)
            {
                var tSchema = p.TargetSchema as string;
                var tName = p.TargetName as string;
                var tType = (p.TargetType as string)?.Trim();
                var refClass = p.ReferencedClass != null ? (byte)p.ReferencedClass : (byte)1;
                
                if (!string.IsNullOrEmpty(tSchema))
                {
                    string depType = "Function";
                    if (refClass == 6)
                        depType = "Type";
                    else if (tType == "V")
                        depType = "View";
                    else if (tType == "U")
                        depType = "Table";
                    else if (tType == "P")
                        depType = "Procedure";

                    sp.Dependencies.Add(new Dependency { Schema = tSchema, Name = tName, Type = depType });
                }
            }

            var refs = await multi.ReadAsync<dynamic>();
            sp.ReferencedBySPs.Clear();
            foreach(var c in refs)
            {
                var sSchema = c.SourceSchema as string;
                var sName = c.SourceName as string;
                var sType = (c.SourceType as string)?.Trim();

                if (sType == "P")
                    sp.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Procedure" });
                else if (sType == "FN" || sType == "IF" || sType == "TF")
                    sp.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Function" });
            }

            sp.AreDependenciesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dependencies for SP {FullName}", fullName);
            OnError?.Invoke($"Error fetching dependencies for SP {fullName}: {ex.Message}");
        }
    }

    public async Task LoadFunctionDependenciesAsync(string fullName)
    {
        var fn = Functions.FirstOrDefault(f => f.FullName == fullName);
        if (fn == null || fn.AreDependenciesLoaded || string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            
            var depsSql = @"
                DECLARE @RefId INT = OBJECT_ID(@FullName);

                -- What it references (Dependencies)
                SELECT DISTINCT
                    ISNULL(d.referenced_schema_name, s.name) AS TargetSchema,
                    d.referenced_entity_name AS TargetName,
                    ro.type AS TargetType,
                    d.referenced_class AS ReferencedClass
                FROM sys.sql_expression_dependencies d
                LEFT JOIN sys.objects ro ON d.referenced_id = ro.object_id
                LEFT JOIN sys.schemas s ON ro.schema_id = s.schema_id
                WHERE d.referencing_id = @RefId
                OPTION (RECOMPILE);

                -- What references it (ReferencedBy)
                SELECT DISTINCT
                    s.name AS SourceSchema,
                    o.name AS SourceName,
                    o.type AS SourceType
                FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON d.referencing_id = o.object_id
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE d.referenced_id = @RefId
                OPTION (RECOMPILE);
            ";

            using var multi = await conn.QueryMultipleAsync(depsSql, new { FullName = fullName }, commandTimeout: 300);
            
            var deps = await multi.ReadAsync<dynamic>();
            fn.Dependencies.Clear();
            foreach(var p in deps)
            {
                var tSchema = p.TargetSchema as string;
                var tName = p.TargetName as string;
                var tType = (p.TargetType as string)?.Trim();
                var refClass = p.ReferencedClass != null ? (byte)p.ReferencedClass : (byte)1;
                
                if (!string.IsNullOrEmpty(tSchema))
                {
                    string depType = "Function";
                    if (refClass == 6)
                        depType = "Type";
                    else if (tType == "V")
                        depType = "View";
                    else if (tType == "U")
                        depType = "Table";
                    else if (tType == "P")
                        depType = "Procedure";

                    fn.Dependencies.Add(new Dependency { Schema = tSchema, Name = tName, Type = depType });
                }
            }

            var refs = await multi.ReadAsync<dynamic>();
            fn.ReferencedBy.Clear();
            foreach(var c in refs)
            {
                var sSchema = c.SourceSchema as string;
                var sName = c.SourceName as string;
                var sType = (c.SourceType as string)?.Trim();

                string depType = sType == "V" ? "View" : (sType == "P" ? "Procedure" : "Function");
                fn.ReferencedBy.Add(new Dependency { Schema = sSchema, Name = sName, Type = depType });
            }

            fn.AreDependenciesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dependencies for Function {FullName}", fullName);
            OnError?.Invoke($"Error fetching dependencies for Function {fullName}: {ex.Message}");
        }
    }

    private class RawTable { public string SchemaName { get; set; } public string Name { get; set; } }
    private class RawView { public string SchemaName { get; set; } public string Name { get; set; } }
    private class RawColumn { public string SchemaName { get; set; } public string TableName { get; set; } public string ColumnName { get; set; } public string DataType { get; set; } }
    private class RawFk { public string ParentTable { get; set; } public string ParentColumn { get; set; } public bool IsNullable { get; set; } public string RefTable { get; set; } public string RefColumn { get; set; } public string ParentSchema { get; set; } public string RefSchema { get; set; } }
    private class RawFunction { public string SchemaName { get; set; } public string FuncName { get; set; } public string FuncType { get; set; } public DateTime? CreatedDate { get; set; } public DateTime? ModifiedDate { get; set; } }
    private class RawFnParam { public string SchemaName { get; set; } public string FuncName { get; set; } public string ParamName { get; set; } public string DataType { get; set; } public bool IsOutput { get; set; } public short MaxLength { get; set; } public byte Precision { get; set; } public byte Scale { get; set; } public int ParamId { get; set; } }
}
