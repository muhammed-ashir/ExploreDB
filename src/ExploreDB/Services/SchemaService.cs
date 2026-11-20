using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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

    public List<string> PrimaryKeyColumns => Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
    public bool HasSinglePrimaryKey => PrimaryKeyColumns.Count == 1;
    
    // Views that reference this table
    public List<Dependency> ReferencedByViews { get; set; } = new();
    // Stored Procedures that reference this table
    public string? Definition { get; set; }
    public bool IsDefinitionLoaded { get; set; } = false;
    public List<Dependency> ReferencedBySPs { get; set; } = new();

    public bool AreDependenciesLoaded { get; set; } = false;

    public override string ToString() => FullName;
}

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsIdentity { get; set; } = false;
    public bool IsComputed { get; set; } = false;
    public bool IsPrimaryKey { get; set; } = false;
    public bool IsUnique { get; set; } = false;
    
    // Lineage info (for views)
    public string? SourceSchema { get; set; }
    public string? SourceTable { get; set; }
    public string? SourceColumn { get; set; }
}

public enum RelationshipType
{
    Physical,     // Database constraint from sys.foreign_keys
    Inferred,     // Automatically detected via naming & anchor heuristics
    UserDefined   // Manually specified or confirmed by the user
}

public class Relationship
{
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty; // The table being referenced
    public string ToColumn { get; set; } = string.Empty;
    public bool IsNullable { get; set; } = false; // Whether the FK column allows NULL
    public RelationshipType Type { get; set; } = RelationshipType.Physical;
    public int ConfidenceScore { get; set; } = 100; // 0-100
    public string? MatchReason { get; set; }
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
    public string? Definition { get; set; }
    public bool IsDefinitionLoaded { get; set; } = false;

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
    public string? Definition { get; set; }
    public bool IsDefinitionLoaded { get; set; } = false;

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
    public string? Definition { get; set; }
    public bool IsDefinitionLoaded { get; set; } = false;

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

public class SchemaCacheEntry
{
    public string ConnectionString { get; set; } = string.Empty;
    public List<TableInfo> Tables { get; set; } = new();
    public List<ViewInfo> Views { get; set; } = new();
    public List<SpInfo> StoredProcedures { get; set; } = new();
    public List<FunctionInfo> Functions { get; set; } = new();
    public List<TypeInfo> Types { get; set; } = new();
    public DateTime LastAccessed { get; set; } = DateTime.Now;
    public DateTime LastFetched { get; set; } = DateTime.Now;
}

public class SchemaService
{
    private readonly ConnectionService _connectionService;
    public List<TableInfo> Tables { get; private set; } = new();
    public List<ViewInfo> Views { get; private set; } = new();
    public List<SpInfo> StoredProcedures { get; private set; } = new();
    public List<FunctionInfo> Functions { get; private set; } = new();
    public List<TypeInfo> Types { get; private set; } = new();
    
    public bool IsLoadingTables { get; private set; } = false;
    public bool IsLoadingViews { get; private set; } = false;
    public bool IsLoadingFunctions { get; private set; } = false;
    public bool IsLoadingStoredProcedures { get; private set; } = false;
    public bool IsLoadingTypes { get; private set; } = false;
    
    public event Action? OnTablesLoaded;
    public event Action? OnViewsLoaded;
    public event Action? OnFunctionsLoaded;
    public event Action? OnStoredProceduresLoaded;
    public event Action? OnTypesLoaded;
    public event Action<string>? OnError;

    public bool AreTablesLoaded { get; private set; } = false;
    public bool AreViewsLoaded { get; private set; } = false;
    public bool AreFunctionsLoaded { get; private set; } = false;
    public bool AreStoredProceduresLoaded { get; private set; } = false;
    public bool AreTypesLoaded { get; private set; } = false;

    public string ActiveDatabase
    {
        get
        {
            if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return string.Empty;
            try
            {
                var builder = new SqlConnectionStringBuilder(_connectionService.ConnectionString);
                return builder.InitialCatalog ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public bool IsDatabaseConnected
    {
        get
        {
            if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return false;
            try
            {
                var builder = new SqlConnectionStringBuilder(_connectionService.ConnectionString);
                return !string.IsNullOrEmpty(builder.InitialCatalog);
            }
            catch
            {
                return false;
            }
        }
    }

    private readonly ILogger<SchemaService> _logger;
    private readonly IConfiguration _config;
    private readonly Dictionary<string, SchemaCacheEntry> _schemaCache = new();
    private readonly int _maxCacheSize = 10;

    public SchemaService(ConnectionService connectionService, ILogger<SchemaService> logger, IConfiguration config)
    {
        _connectionService = connectionService;
        _logger = logger;
        _config = config;
        _maxCacheSize = _config.GetValue<int>("MaxSchemaCacheCount", 10);
    }

    public (List<string> Tables, List<string> Views, List<string> Columns, List<string> Sps, List<string> Functions) GetCachedSchema(string connectionString)
    {
        if (!string.IsNullOrEmpty(connectionString) && _schemaCache.TryGetValue(connectionString, out var entry))
        {
            var cols = entry.Tables.SelectMany(t => t.Columns.Select(c => c.Name))
                .Union(entry.Views.SelectMany(v => v.Columns.Select(c => c.Name)))
                .Distinct()
                .ToList();
            
            return (
                entry.Tables.Select(t => t.FullName).ToList(),
                entry.Views.Select(v => v.FullName).ToList(),
                cols,
                entry.StoredProcedures.Select(sp => sp.FullName).ToList(),
                entry.Functions.Select(f => f.FullName).ToList()
            );
        }
        return (new(), new(), new(), new(), new());
    }

    public async Task<string?> GetStoredProcedureDefinitionAsync(string fullName)
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return null;

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT OBJECT_DEFINITION(OBJECT_ID(@FullName))";
            cmd.Parameters.AddWithValue("@FullName", fullName);
            var result = await cmd.ExecuteScalarAsync();
            return result as string;
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
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT OBJECT_DEFINITION(OBJECT_ID(@FullName))";
            cmd.Parameters.AddWithValue("@FullName", fullName);
            var result = await cmd.ExecuteScalarAsync();
            return result as string;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Function definition for {FullName}", fullName);
            return null;
        }
    }

    public Task LoadSchemaAsync(bool forceRefresh = false)
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString))
        {
            Tables = new(); Views = new(); Functions = new(); StoredProcedures = new(); Types = new();
            AreTablesLoaded = false; AreViewsLoaded = false; AreFunctionsLoaded = false; AreStoredProceduresLoaded = false; AreTypesLoaded = false;
            OnTablesLoaded?.Invoke();
            OnViewsLoaded?.Invoke();
            OnFunctionsLoaded?.Invoke();
            OnStoredProceduresLoaded?.Invoke();
            OnTypesLoaded?.Invoke();
            return Task.CompletedTask;
        }
        
        var builder = new SqlConnectionStringBuilder(_connectionService.ConnectionString);
        
        if (string.IsNullOrEmpty(builder.InitialCatalog))
        {
            Tables = new(); Views = new(); Functions = new(); StoredProcedures = new(); Types = new();
            AreTablesLoaded = false; AreViewsLoaded = false; AreFunctionsLoaded = false; AreStoredProceduresLoaded = false; AreTypesLoaded = false;
            OnTablesLoaded?.Invoke();
            OnViewsLoaded?.Invoke();
            OnFunctionsLoaded?.Invoke();
            OnStoredProceduresLoaded?.Invoke();
            OnTypesLoaded?.Invoke();
            return Task.CompletedTask;
        }

        string cacheKey = _connectionService.ConnectionString;

        if (!forceRefresh && _schemaCache.TryGetValue(cacheKey, out var cacheEntry))
        {
            var expirationHours = _config.GetValue<int>("SchemaCacheExpirationHours", 24);
            if ((DateTime.Now - cacheEntry.LastFetched).TotalHours >= expirationHours)
            {
                _logger.LogInformation("Schema cache expired for {ConnectionString}, forcing refresh.", cacheKey);
            }
            else
            {
                _logger.LogInformation("Loading schema from cache for {ConnectionString}", cacheKey);
                cacheEntry.LastAccessed = DateTime.Now;
                
                Tables = cacheEntry.Tables;
                Views = cacheEntry.Views;
                Functions = cacheEntry.Functions;
                StoredProcedures = cacheEntry.StoredProcedures;
                Types = cacheEntry.Types;

            AreTablesLoaded = true;
            AreViewsLoaded = true;
            AreFunctionsLoaded = true;
            AreStoredProceduresLoaded = true;
            AreTypesLoaded = true;

            OnTablesLoaded?.Invoke();
            OnViewsLoaded?.Invoke();
            OnFunctionsLoaded?.Invoke();
            OnStoredProceduresLoaded?.Invoke();
            OnTypesLoaded?.Invoke();
            return Task.CompletedTask;
            }
        }

        AreTablesLoaded = false;
        AreViewsLoaded = false;
        AreFunctionsLoaded = false;
        AreStoredProceduresLoaded = false;
        AreTypesLoaded = false;

        _logger.LogInformation("Kicking off Parallel Eager Schema Load Tasks...");
        
        _ = LoadAllAndCacheAsync(cacheKey);
        
        return Task.CompletedTask;
    }

    private async Task LoadAllAndCacheAsync(string cacheKey)
    {
        await Task.WhenAll(
            LoadTablesAsync(),
            LoadViewsAsync(),
            LoadFunctionsAsync(),
            LoadStoredProceduresAsync(),
            LoadTypesAsync()
        );

        if (AreTablesLoaded && AreViewsLoaded && AreFunctionsLoaded && AreStoredProceduresLoaded && AreTypesLoaded)
        {
            var newEntry = new SchemaCacheEntry
            {
                ConnectionString = cacheKey,
                Tables = Tables,
                Views = Views,
                Functions = Functions,
                StoredProcedures = StoredProcedures,
                Types = Types,
                LastAccessed = DateTime.Now
            };

            lock (_schemaCache)
            {
                if (_schemaCache.Count >= _maxCacheSize && !_schemaCache.ContainsKey(cacheKey))
                {
                    var oldest = _schemaCache.Values.OrderBy(x => x.LastAccessed).First();
                    _schemaCache.Remove(oldest.ConnectionString);
                }
                _schemaCache[cacheKey] = newEntry;
            }
        }
    }

    public async Task LoadTablesAsync()
    {
        if (AreTablesLoaded || !IsDatabaseConnected) return;
        
        IsLoadingTables = true;
        Tables = new List<TableInfo>();
        OnTablesLoaded?.Invoke();

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();

            var batchSql = @"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

                SELECT s.name AS SchemaName, t.name AS Name
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id;

                SELECT s.name AS SchemaName, o.name AS TableName, c.name AS ColumnName,
                    CASE 
                        WHEN ty.name LIKE 'System.%' OR ty.name LIKE '%.%' THEN ISNULL(sty.name, ty.name)
                        ELSE ty.name 
                    END AS DataType,
                    c.is_identity AS IsIdentity,
                    c.is_computed AS IsComputed
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                LEFT JOIN sys.types sty ON c.system_type_id = sty.user_type_id AND sty.is_user_defined = 0
                WHERE o.type = 'U';

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

                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    c.name AS ColumnName,
                    i.is_primary_key AS IsPrimaryKey,
                    i.is_unique AS IsUnique
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE i.is_primary_key = 1 OR (i.is_unique = 1 AND i.is_unique_constraint = 1);
            ";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = batchSql;
            cmd.CommandTimeout = 600;

            using var reader = await cmd.ExecuteReaderAsync();
            
            var newTables = new List<TableInfo>();
            while (await reader.ReadAsync())
            {
                newTables.Add(new TableInfo 
                { 
                    Schema = reader.GetString(0), 
                    Name = reader.GetString(1) 
                });
            }
            newTables = newTables.OrderBy(t => t.FullName).ToList();
            var tableDict = newTables.ToDictionary(t => t.FullName);

            int yieldCount = 0;
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (++yieldCount % 5000 == 0) await Task.Delay(1);
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    var colName = reader.GetString(2);
                    var dataType = reader.GetString(3);
                    var isIdentity = !reader.IsDBNull(4) && reader.GetBoolean(4);
                    var isComputed = !reader.IsDBNull(5) && reader.GetBoolean(5);
                    
                    var key = $"[{schema}].[{table}]";
                    if (tableDict.TryGetValue(key, out var t))
                    {
                        t.Columns.Add(new ColumnInfo 
                        { 
                            Name = colName, 
                            DataType = NormalizeDataType(dataType),
                            IsIdentity = isIdentity,
                            IsComputed = isComputed
                        });
                    }
                }
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (++yieldCount % 5000 == 0) await Task.Delay(1);
                    
                    var parentFull = $"[{reader.GetString(5)}].[{reader.GetString(0)}]";
                    var refFull = $"[{reader.GetString(6)}].[{reader.GetString(3)}]";

                    if (tableDict.TryGetValue(parentFull, out var parent) && tableDict.TryGetValue(refFull, out var reference))
                    {
                        var rel = new Relationship 
                        { 
                            FromTable = parentFull, 
                            FromColumn = reader.GetString(1),
                            ToTable = refFull, 
                            ToColumn = reader.GetString(4),
                            IsNullable = reader.GetBoolean(2),
                            Type = RelationshipType.Physical,
                            ConfidenceScore = 100
                        };
                        parent.OutgoingKeys.Add(rel);
                        reference.IncomingKeys.Add(rel);
                    }
                }
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (++yieldCount % 5000 == 0) await Task.Delay(1);
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    var colName = reader.GetString(2);
                    var isPk = reader.GetBoolean(3);
                    var isUq = reader.GetBoolean(4);

                    var key = $"[{schema}].[{table}]";
                    if (tableDict.TryGetValue(key, out var t))
                    {
                        var col = t.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
                        if (col != null)
                        {
                            if (isPk) col.IsPrimaryKey = true;
                            if (isUq) col.IsUnique = true;
                        }
                    }
                }
            }

            Tables = newTables;
            AreTablesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Tables"); 
            OnError?.Invoke($"Error loading tables: {ex.Message}");
        }
        finally
        {
            IsLoadingTables = false;
            OnTablesLoaded?.Invoke();
        }
    }

    public event Action? OnRelationshipsUpdated;

    public void ApplyVirtualRelationships(IEnumerable<Relationship> virtualRelationships)
    {
        var tableDict = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Tables)
        {
            tableDict[t.FullName] = t;
            var clean = t.FullName.Replace("[", "").Replace("]", "").Trim();
            tableDict[clean] = t;
            if (clean.Contains('.'))
            {
                tableDict[clean.Substring(clean.LastIndexOf('.') + 1)] = t;
            }
        }

        // Remove existing non-physical relationships first
        foreach (var table in Tables)
        {
            table.OutgoingKeys.RemoveAll(r => r.Type != RelationshipType.Physical);
            table.IncomingKeys.RemoveAll(r => r.Type != RelationshipType.Physical);
        }

        foreach (var rel in virtualRelationships)
        {
            var cleanFrom = rel.FromTable.Replace("[", "").Replace("]", "").Trim();
            var cleanTo = rel.ToTable.Replace("[", "").Replace("]", "").Trim();

            if ((tableDict.TryGetValue(rel.FromTable, out var parent) || tableDict.TryGetValue(cleanFrom, out parent)) &&
                (tableDict.TryGetValue(rel.ToTable, out var reference) || tableDict.TryGetValue(cleanTo, out reference)))
            {
                // Disallow virtual relationship if this column already has a physical foreign key constraint
                bool hasPhysicalFk = parent.OutgoingKeys.Any(k =>
                    k.Type == RelationshipType.Physical &&
                    string.Equals(k.FromColumn, rel.FromColumn, StringComparison.OrdinalIgnoreCase));

                if (hasPhysicalFk) continue;

                // Avoid duplicate if relationship already exists on the same columns
                bool exists = parent.OutgoingKeys.Any(k =>
                    string.Equals(k.ToTable.Replace("[", "").Replace("]", ""), cleanTo, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(k.FromColumn, rel.FromColumn, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(k.ToColumn, rel.ToColumn, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    parent.OutgoingKeys.Add(rel);
                    reference.IncomingKeys.Add(rel);
                }
            }
        }

        OnRelationshipsUpdated?.Invoke();
    }

    public async Task LoadViewsAsync()
    {
        if (AreViewsLoaded || !IsDatabaseConnected) return;
        
        IsLoadingViews = true;
        Views = new List<ViewInfo>();
        OnViewsLoaded?.Invoke();

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();

            var batchSql = @"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

                SELECT s.name AS SchemaName, v.name AS Name
                FROM sys.views v
                INNER JOIN sys.schemas s ON v.schema_id = s.schema_id;

                SELECT s.name AS SchemaName, o.name AS TableName, c.name AS ColumnName,
                    CASE 
                        WHEN ty.name LIKE 'System.%' OR ty.name LIKE '%.%' THEN ISNULL(sty.name, ty.name)
                        ELSE ty.name 
                    END AS DataType
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                LEFT JOIN sys.types sty ON c.system_type_id = sty.user_type_id AND sty.is_user_defined = 0
                WHERE o.type = 'V';
            ";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = batchSql;
            cmd.CommandTimeout = 600;

            using var reader = await cmd.ExecuteReaderAsync();
            
            var newViews = new List<ViewInfo>();
            while (await reader.ReadAsync())
            {
                newViews.Add(new ViewInfo 
                { 
                    Schema = reader.GetString(0), 
                    Name = reader.GetString(1) 
                });
            }
            newViews = newViews.OrderBy(v => v.FullName).ToList();
            var viewDict = newViews.ToDictionary(v => v.FullName);

            int yieldCount = 0;
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (++yieldCount % 5000 == 0) await Task.Delay(1);
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    var colName = reader.GetString(2);
                    var dataType = reader.GetString(3);
                    
                    var key = $"[{schema}].[{table}]";
                    if (viewDict.TryGetValue(key, out var view))
                    {
                        view.Columns.Add(new ColumnInfo { Name = colName, DataType = NormalizeDataType(dataType) });
                    }
                }
            }

            Views = newViews;
            AreViewsLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Views"); 
            OnError?.Invoke($"Error loading views: {ex.Message}");
        }
        finally
        {
            IsLoadingViews = false;
            OnViewsLoaded?.Invoke();
        }
    }

    public async Task LoadFunctionsAsync()
    {
        if (AreFunctionsLoaded || !IsDatabaseConnected) return;
        
        IsLoadingFunctions = true;
        Functions = new List<FunctionInfo>();
        OnFunctionsLoaded?.Invoke();

        try 
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();

            var batchSql = @"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

                SELECT 
                    s.name AS SchemaName, o.name AS FuncName, o.type AS FuncType, o.create_date AS CreatedDate, o.modify_date AS ModifiedDate
                FROM sys.objects o
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE o.type IN ('FN', 'IF', 'TF') AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name;

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

            using var cmd = conn.CreateCommand();
            cmd.CommandText = batchSql;
            cmd.CommandTimeout = 600;

            using var reader = await cmd.ExecuteReaderAsync();
            
            var newFunctions = new List<FunctionInfo>();
            while (await reader.ReadAsync())
            {
                var typeCode = reader.GetString(2).Trim();
                newFunctions.Add(new FunctionInfo 
                { 
                    Schema = reader.GetString(0), 
                    Name = reader.GetString(1),
                    FunctionType = typeCode switch {
                        "FN" => "Scalar",
                        "IF" => "Inline Table",
                        "TF" => "Multi-Statement Table",
                        _ => typeCode
                    },
                    CreatedDate = await reader.IsDBNullAsync(3) ? null : reader.GetDateTime(3),
                    ModifiedDate = await reader.IsDBNullAsync(4) ? null : reader.GetDateTime(4)
                });
            }
            var fnDict = newFunctions.ToDictionary(f => f.FullName);

            int yieldCount = 0;
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (++yieldCount % 5000 == 0) await Task.Delay(1);
                    var schema = reader.GetString(0);
                    var funcName = reader.GetString(1);
                    var paramName = await reader.IsDBNullAsync(2) ? "" : reader.GetString(2);
                    var dataType = reader.GetString(3);
                    var isOutput = reader.GetBoolean(4);
                    var maxLen = reader.GetInt16(5);
                    var prec = reader.GetByte(6);
                    var scale = reader.GetByte(7);
                    var paramId = reader.GetInt32(8);

                    var fnFull = $"[{schema}].[{funcName}]";
                    if (fnDict.TryGetValue(fnFull, out var fn))
                    {
                        var rawLen = maxLen;
                        var upperType = dataType.ToUpper();
                        int displayLen = rawLen;
                        if (upperType is "NVARCHAR" or "NCHAR")
                            displayLen = rawLen == -1 ? -1 : rawLen / 2;
                        
                        if (paramId == 0)
                        {
                            var displayType = upperType;
                            if (displayType is "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR")
                                displayType = displayLen == -1 ? $"{upperType}(MAX)" : $"{upperType}({displayLen})";
                            else if (displayType is "DECIMAL" or "NUMERIC")
                                displayType = $"{upperType}({prec},{scale})";

                            fn.ReturnType = displayType;
                        }
                        else
                        {
                            fn.Parameters.Add(new FunctionParameter
                            {
                                Name = paramName,
                                DataType = dataType,
                                MaxLength = displayLen,
                                Precision = prec,
                                Scale = scale,
                                IsOutput = isOutput
                            });
                        }
                    }
                }
            }

            Functions = newFunctions;
            AreFunctionsLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Functions"); 
            OnError?.Invoke($"Error loading functions: {ex.Message}");
        }
        finally
        {
            IsLoadingFunctions = false;
            OnFunctionsLoaded?.Invoke();
        }
    }

    public async Task LoadStoredProceduresAsync()
    {
        if (AreStoredProceduresLoaded || !IsDatabaseConnected) return;

        IsLoadingStoredProcedures = true;
        StoredProcedures = new List<SpInfo>();
        OnStoredProceduresLoaded?.Invoke();

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();
            
            var batchSql = @"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

                SELECT 
                    s.name AS SchemaName, p.name AS ProcName, p.create_date AS CreatedDate, p.modify_date AS ModifiedDate
                FROM sys.procedures p
                JOIN sys.schemas s ON p.schema_id = s.schema_id
                WHERE p.is_ms_shipped = 0
                ORDER BY s.name, p.name;

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

            using var cmd = conn.CreateCommand();
            cmd.CommandText = batchSql;
            cmd.CommandTimeout = 600;

            using var reader = await cmd.ExecuteReaderAsync();

            var newSps = new List<SpInfo>();
            while (await reader.ReadAsync())
            {
                newSps.Add(new SpInfo
                {
                    Schema = reader.GetString(0),
                    Name = reader.GetString(1),
                    CreatedDate = await reader.IsDBNullAsync(2) ? null : reader.GetDateTime(2),
                    ModifiedDate = await reader.IsDBNullAsync(3) ? null : reader.GetDateTime(3)
                });
            }
            var spDict = newSps.ToDictionary(sp => sp.FullName);

            int yieldCount = 0;
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (++yieldCount % 5000 == 0) await Task.Delay(1);
                    var schema = reader.GetString(0);
                    var procName = reader.GetString(1);
                    var paramName = reader.GetString(2);
                    var dataType = reader.GetString(3);
                    var isOutput = reader.GetBoolean(4);
                    var hasDefault = reader.GetBoolean(5);
                    var defaultValue = await reader.IsDBNullAsync(6) ? null : reader.GetString(6);
                    var maxLen = reader.GetInt16(7);
                    var prec = reader.GetByte(8);
                    var scale = reader.GetByte(9);

                    var spFull = $"[{schema}].[{procName}]";
                    if (spDict.TryGetValue(spFull, out var sp))
                    {
                        var rawLen = maxLen;
                        var upperType = dataType.ToUpper();
                        int displayLen = rawLen;
                        if (upperType is "NVARCHAR" or "NCHAR")
                            displayLen = rawLen == -1 ? -1 : rawLen / 2;

                        sp.Parameters.Add(new SpParameter
                        {
                            Name = paramName,
                            DataType = dataType,
                            IsOutput = isOutput,
                            HasDefault = hasDefault,
                            DefaultValue = defaultValue,
                            MaxLength = displayLen,
                            Precision = prec,
                            Scale = scale
                        });
                    }
                }
            }

            StoredProcedures = newSps;
            AreStoredProceduresLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Stored Procedures");
            OnError?.Invoke($"Error loading stored procedures: {ex.Message}");
        }
        finally
        {
            IsLoadingStoredProcedures = false;
            OnStoredProceduresLoaded?.Invoke();
        }
    }

    public async Task LoadTypesAsync()
    {
        if (AreTypesLoaded || !IsDatabaseConnected) return;

        IsLoadingTypes = true;
        Types = new List<TypeInfo>();
        OnTypesLoaded?.Invoke();

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();
            
            var batchSql = @"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

                SELECT 
                    s.name AS SchemaName, t.name AS TypeName, t.is_table_type AS IsTableType,
                    bt.name AS BaseTypeName, t.max_length AS MaxLength, t.precision AS Precision, t.scale AS Scale, tt.type_table_object_id AS TypeTableObjId
                FROM sys.types t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN sys.types bt ON t.system_type_id = bt.user_type_id AND bt.is_user_defined = 0
                LEFT JOIN sys.table_types tt ON t.user_type_id = tt.user_type_id
                WHERE t.is_user_defined = 1 AND t.is_assembly_type = 0
                ORDER BY s.name, t.name;

                SELECT 
                    c.object_id AS ObjectId, c.name AS ColumnName, t.name AS DataType
                FROM sys.columns c
                JOIN sys.types t ON c.user_type_id = t.user_type_id
                JOIN sys.table_types tt ON c.object_id = tt.type_table_object_id;
            ";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = batchSql;
            cmd.CommandTimeout = 600;

            using var reader = await cmd.ExecuteReaderAsync();

            var typeTableMap = new Dictionary<int, TypeInfo>();
            
            var newTypes = new List<TypeInfo>();
            while (await reader.ReadAsync())
            {
                var isTableType = reader.GetBoolean(2);
                var t = new TypeInfo
                {
                    Schema = reader.GetString(0),
                    Name = reader.GetString(1),
                    IsTableType = isTableType,
                    BaseType = await reader.IsDBNullAsync(3) ? null : reader.GetString(3),
                    MaxLength = reader.GetInt16(4),
                    Precision = reader.GetByte(5),
                    Scale = reader.GetByte(6)
                };
                
                if (isTableType && !await reader.IsDBNullAsync(7))
                {
                    int typeObjId = reader.GetInt32(7);
                    typeTableMap[typeObjId] = t;
                }
                
                newTypes.Add(t);
            }

            int yieldCount = 0;
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (++yieldCount % 5000 == 0) await Task.Delay(1);
                    int objId = reader.GetInt32(0);
                    if (typeTableMap.TryGetValue(objId, out var typeInfo))
                    {
                        typeInfo.Columns.Add(new ColumnInfo
                        {
                            Name = reader.GetString(1),
                            DataType = reader.GetString(2)
                        });
                    }
                }
            }

            Types = newTypes;
            AreTypesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Types");
            OnError?.Invoke($"Error loading types: {ex.Message}");
        }
        finally
        {
            IsLoadingTypes = false;
            OnTypesLoaded?.Invoke();
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

                        SELECT s.name AS SchemaName, tt.name AS ObjectName, 
                               'Type' AS ObjectType
                        FROM sys.columns c
                        JOIN sys.table_types tt ON c.object_id = tt.type_table_object_id
                        JOIN sys.schemas s ON tt.schema_id = s.schema_id
                        WHERE c.user_type_id = @TypeId
                        
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

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@Schema", typeInfo.Schema);
            cmd.Parameters.AddWithValue("@Name", typeInfo.Name);
            cmd.CommandTimeout = 300;

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            
            var usedBy = new List<Dependency>();
            while (await reader.ReadAsync())
            {
                usedBy.Add(new Dependency
                {
                    Schema = await reader.IsDBNullAsync(0) ? string.Empty : reader.GetString(0),
                    Name = await reader.IsDBNullAsync(1) ? string.Empty : reader.GetString(1),
                    Type = await reader.IsDBNullAsync(2) ? string.Empty : reader.GetString(2)
                });
            }
            
            typeInfo.UsedBy = usedBy;
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

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.CommandTimeout = 300;

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            
            table.ReferencedByViews.Clear();
            table.ReferencedBySPs.Clear();

            while (await reader.ReadAsync())
            {
                var schema = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
                var name = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                var type = await reader.IsDBNullAsync(2) ? null : reader.GetString(2)?.Trim();

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

    private static string NormalizeDataType(string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return dataType;
        return dataType switch
        {
            "System.Byte[]" => "rowversion",
            "System.String" => "nvarchar",
            "System.Int32"  => "int",
            "System.Int64"  => "bigint",
            "System.Int16"  => "smallint",
            "System.Byte"   => "tinyint",
            "System.Boolean" => "bit",
            "System.DateTime" => "datetime",
            "System.Decimal" => "decimal",
            "System.Double"  => "float",
            "System.Single"  => "real",
            "System.Guid"    => "uniqueidentifier",
            _ => dataType.Contains('.') ? dataType.Split('.').Last() : dataType
        };
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

            using var cmdLineage = conn.CreateCommand();
            cmdLineage.CommandText = lineageSql;
            cmdLineage.CommandTimeout = 300;
            
            await conn.OpenAsync();
            using var readerLineage = await cmdLineage.ExecuteReaderAsync();
            var colDict = view.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            
            while (await readerLineage.ReadAsync())
            {
                var sourceSchema = readerLineage.GetString(0);
                var sourceTable = readerLineage.GetString(1);
                var sourceColName = readerLineage.GetString(2);
                if (colDict.TryGetValue(sourceColName, out var col) && string.IsNullOrEmpty(col.SourceTable))
                {
                    col.SourceSchema = sourceSchema;
                    col.SourceTable = sourceTable;
                    col.SourceColumn = sourceColName;
                }
            }
            await readerLineage.CloseAsync();

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

            using var cmd = conn.CreateCommand();
            cmd.CommandText = depsSql;
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.CommandTimeout = 300;

            using var reader = await cmd.ExecuteReaderAsync();
            
            view.Parents.Clear();
            while (await reader.ReadAsync())
            {
                var tSchema = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
                var tName = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                var tType = await reader.IsDBNullAsync(2) ? null : reader.GetString(2)?.Trim();
                if (!string.IsNullOrEmpty(tSchema))
                {
                    string depType = tType == "V" ? "View" : (tType == "U" ? "Table" : "Function");
                    view.Parents.Add(new Dependency { Schema = tSchema, Name = tName, Type = depType });
                }
            }

            view.Children.Clear();
            view.ReferencedBySPs.Clear();
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    var sSchema = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
                    var sName = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                    var sType = await reader.IsDBNullAsync(2) ? null : reader.GetString(2)?.Trim();

                    if (sType == "V")
                        view.Children.Add(new Dependency { Schema = sSchema, Name = sName, Type = "View" });
                    else if (sType == "P")
                        view.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Procedure" });
                    else if (sType is "FN" or "IF" or "TF")
                        view.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Function" });
                }
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

            using var cmd = conn.CreateCommand();
            cmd.CommandText = depsSql;
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.CommandTimeout = 300;

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            
            sp.Dependencies.Clear();
            while (await reader.ReadAsync())
            {
                var tSchema = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
                var tName = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                var tType = await reader.IsDBNullAsync(2) ? null : reader.GetString(2)?.Trim();
                var refClass = await reader.IsDBNullAsync(3) ? (byte)1 : reader.GetByte(3);
                
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

            sp.ReferencedBySPs.Clear();
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    var sSchema = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
                    var sName = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                    var sType = await reader.IsDBNullAsync(2) ? null : reader.GetString(2)?.Trim();

                    if (sType == "P")
                        sp.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Procedure" });
                    else if (sType == "FN" || sType == "IF" || sType == "TF")
                        sp.ReferencedBySPs.Add(new Dependency { Schema = sSchema, Name = sName, Type = "Function" });
                }
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

            using var cmd = conn.CreateCommand();
            cmd.CommandText = depsSql;
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.CommandTimeout = 300;

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            
            fn.Dependencies.Clear();
            while (await reader.ReadAsync())
            {
                var tSchema = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
                var tName = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                var tType = await reader.IsDBNullAsync(2) ? null : reader.GetString(2)?.Trim();
                var refClass = await reader.IsDBNullAsync(3) ? (byte)1 : reader.GetByte(3);
                
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

            fn.ReferencedBy.Clear();
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    var sSchema = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
                    var sName = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                    var sType = await reader.IsDBNullAsync(2) ? null : reader.GetString(2)?.Trim();

                    string depType = sType == "V" ? "View" : (sType == "P" ? "Procedure" : "Function");
                    fn.ReferencedBy.Add(new Dependency { Schema = sSchema, Name = sName, Type = depType });
                }
            }

            fn.AreDependenciesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dependencies for Function {FullName}", fullName);
            OnError?.Invoke($"Error fetching dependencies for Function {fullName}: {ex.Message}");
        }
    }

    public async Task<string> GenerateTableDdlAsync(string fullName)
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return string.Empty;

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
-- 1. Columns & Defaults & Computed & Identities
SELECT 
    c.name AS ColumnName,
    c.column_id AS ColumnId,
    t.name AS TypeName,
    c.max_length AS MaxLength,
    c.precision AS Precision,
    c.scale AS Scale,
    c.is_nullable AS IsNullable,
    c.is_identity AS IsIdentity,
    ISNULL(CAST(ic.seed_value AS VARCHAR(50)), '1') AS IdentitySeed,
    ISNULL(CAST(ic.increment_value AS VARCHAR(50)), '1') AS IdentityIncrement,
    c.is_computed AS IsComputed,
    ISNULL(cc.definition, '') AS ComputedDefinition,
    ISNULL(cc.is_persisted, 0) AS IsPersisted,
    ISNULL(dc.name, '') AS DefaultConstraintName,
    ISNULL(dc.definition, '') AS DefaultDefinition,
    ISNULL(c.default_object_id, 0) AS DefaultObjectId,
    ISNULL(OBJECT_SCHEMA_NAME(c.default_object_id), '') AS BoundDefaultSchema,
    ISNULL(OBJECT_NAME(c.default_object_id), '') AS BoundDefaultName
FROM sys.columns c
JOIN sys.types t ON c.user_type_id = t.user_type_id
LEFT JOIN sys.identity_columns ic ON c.object_id = ic.object_id AND c.column_id = ic.column_id
LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
LEFT JOIN sys.default_constraints dc ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE c.object_id = OBJECT_ID(@FullName)
ORDER BY c.column_id;

-- 2. Primary Key & Unique Constraints
SELECT 
    kc.name AS ConstraintName,
    kc.type AS ConstraintType,
    i.type_desc AS IndexTypeDesc,
    c.name AS ColumnName,
    ic.key_ordinal AS KeyOrdinal,
    ic.is_descending_key AS IsDescending
FROM sys.key_constraints kc
JOIN sys.indexes i ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE kc.parent_object_id = OBJECT_ID(@FullName)
ORDER BY kc.name, ic.key_ordinal;

-- 3. Foreign Key Constraints
SELECT 
    fk.name AS ForeignKeyName,
    SCHEMA_NAME(pt.schema_id) AS ParentSchema,
    pt.name AS ParentTable,
    pc.name AS ParentColumn,
    SCHEMA_NAME(rt.schema_id) AS ReferencedSchema,
    rt.name AS ReferencedTable,
    rc.name AS ReferencedColumn,
    fkc.constraint_column_id AS ColumnOrdinal,
    fk.delete_referential_action_desc AS DeleteAction,
    fk.update_referential_action_desc AS UpdateAction
FROM sys.foreign_keys fk
JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
WHERE fk.parent_object_id = OBJECT_ID(@FullName)
ORDER BY fk.name, fkc.constraint_column_id;

-- 4. Check Constraints
SELECT 
    ck.name AS ConstraintName,
    ck.definition AS CheckDefinition
FROM sys.check_constraints ck
WHERE ck.parent_object_id = OBJECT_ID(@FullName);

-- 5. Secondary Indexes (Non-PK, Non-UQ)
SELECT 
    i.name AS IndexName,
    i.is_unique AS IsUnique,
    i.type_desc AS IndexType,
    ISNULL(i.filter_definition, '') AS FilterDefinition,
    c.name AS ColumnName,
    ic.key_ordinal AS KeyOrdinal,
    ic.is_descending_key AS IsDescending,
    ic.is_included_column AS IsIncluded
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID(@FullName)
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
  AND i.type > 0
ORDER BY i.name, ic.is_included_column, ic.key_ordinal;
";
            cmd.Parameters.AddWithValue("@FullName", fullName);

            using var reader = await cmd.ExecuteReaderAsync();

            var colDefinitions = new List<string>();
            var boundDefaults = new List<(string Schema, string Name, string ColumnName)>();

            // 1. Process Columns
            while (await reader.ReadAsync())
            {
                string colName = reader["ColumnName"].ToString()!;
                string typeName = reader["TypeName"].ToString()!;
                short maxLength = Convert.ToInt16(reader["MaxLength"]);
                byte precision = Convert.ToByte(reader["Precision"]);
                byte scale = Convert.ToByte(reader["Scale"]);
                bool isNullable = Convert.ToBoolean(reader["IsNullable"]);
                bool isIdentity = Convert.ToBoolean(reader["IsIdentity"]);
                string seed = reader["IdentitySeed"].ToString()!;
                string inc = reader["IdentityIncrement"].ToString()!;
                bool isComputed = Convert.ToBoolean(reader["IsComputed"]);
                string compDef = reader["ComputedDefinition"].ToString()!;
                bool isPersisted = Convert.ToBoolean(reader["IsPersisted"]);
                string defConstName = reader["DefaultConstraintName"].ToString()!;
                string defVal = reader["DefaultDefinition"].ToString()!;
                int defaultObjId = Convert.ToInt32(reader["DefaultObjectId"]);
                string boundDefSchema = reader["BoundDefaultSchema"].ToString()!;
                string boundDefName = reader["BoundDefaultName"].ToString()!;

                if (isComputed)
                {
                    string persistedStr = isPersisted ? " PERSISTED" : "";
                    colDefinitions.Add($"    [{colName}] AS {compDef}{persistedStr}");
                    continue;
                }

                string formattedType = FormatSqlType(typeName, maxLength, precision, scale);
                var parts = new List<string> { $"    [{colName}] {formattedType}" };

                if (isIdentity)
                {
                    parts.Add($"IDENTITY({seed},{inc})");
                }

                if (!string.IsNullOrEmpty(defVal))
                {
                    if (!string.IsNullOrEmpty(defConstName))
                    {
                        parts.Add($"CONSTRAINT [{defConstName}] DEFAULT {defVal}");
                    }
                    else
                    {
                        parts.Add($"DEFAULT {defVal}");
                    }
                }
                else if (defaultObjId != 0 && !string.IsNullOrEmpty(boundDefName))
                {
                    boundDefaults.Add((boundDefSchema, boundDefName, colName));
                }

                parts.Add(isNullable ? "NULL" : "NOT NULL");
                colDefinitions.Add(string.Join(" ", parts));
            }

            // 2. Primary Key & Unique Constraints
            await reader.NextResultAsync();
            var keyConstraints = new Dictionary<string, (string Type, string IndexDesc, List<(string Col, bool IsDesc)> Cols)>();
            while (await reader.ReadAsync())
            {
                string name = reader["ConstraintName"].ToString()!;
                string type = reader["ConstraintType"].ToString()!;
                string idxDesc = reader["IndexTypeDesc"].ToString()!;
                string col = reader["ColumnName"].ToString()!;
                bool isDesc = Convert.ToBoolean(reader["IsDescending"]);

                if (!keyConstraints.TryGetValue(name, out var entry))
                {
                    entry = (type, idxDesc, new List<(string Col, bool IsDesc)>());
                    keyConstraints[name] = entry;
                }
                entry.Cols.Add((col, isDesc));
            }

            foreach (var (name, entry) in keyConstraints)
            {
                string typeLabel = entry.Type.Trim().Equals("PK", StringComparison.OrdinalIgnoreCase) ? "PRIMARY KEY" : "UNIQUE";
                string colsStr = string.Join(", ", entry.Cols.Select(c => $"[{c.Col}]" + (c.IsDesc ? " DESC" : " ASC")));
                colDefinitions.Add($"    CONSTRAINT [{name}] {typeLabel} {entry.IndexDesc} ({colsStr})");
            }

            // 3. Foreign Key Constraints
            await reader.NextResultAsync();
            var fks = new Dictionary<string, (string ParentSchema, string ParentTable, string RefSchema, string RefTable, string DelAction, string UpdAction, List<(string FromCol, string ToCol)> Cols)>();
            while (await reader.ReadAsync())
            {
                string fkName = reader["ForeignKeyName"].ToString()!;
                string parentSchema = reader["ParentSchema"].ToString()!;
                string parentTable = reader["ParentTable"].ToString()!;
                string fromCol = reader["ParentColumn"].ToString()!;
                string refSchema = reader["ReferencedSchema"].ToString()!;
                string refTable = reader["ReferencedTable"].ToString()!;
                string toCol = reader["ReferencedColumn"].ToString()!;
                string delAction = reader["DeleteAction"].ToString()!;
                string updAction = reader["UpdateAction"].ToString()!;

                if (!fks.TryGetValue(fkName, out var entry))
                {
                    entry = (parentSchema, parentTable, refSchema, refTable, delAction, updAction, new List<(string, string)>());
                    fks[fkName] = entry;
                }
                entry.Cols.Add((fromCol, toCol));
            }

            foreach (var (fkName, entry) in fks)
            {
                string fromCols = string.Join(", ", entry.Cols.Select(c => $"[{c.FromCol}]"));
                string toCols = string.Join(", ", entry.Cols.Select(c => $"[{c.ToCol}]"));
                var sbFk = new StringBuilder();
                sbFk.Append($"    CONSTRAINT [{fkName}] FOREIGN KEY ({fromCols}) REFERENCES [{entry.RefSchema}].[{entry.RefTable}] ({toCols})");
                if (!string.Equals(entry.DelAction, "NO_ACTION", StringComparison.OrdinalIgnoreCase))
                {
                    sbFk.Append($" ON DELETE {entry.DelAction.Replace("_", " ")}");
                }
                if (!string.Equals(entry.UpdAction, "NO_ACTION", StringComparison.OrdinalIgnoreCase))
                {
                    sbFk.Append($" ON UPDATE {entry.UpdAction.Replace("_", " ")}");
                }
                colDefinitions.Add(sbFk.ToString());
            }

            // 4. Check Constraints
            await reader.NextResultAsync();
            while (await reader.ReadAsync())
            {
                string name = reader["ConstraintName"].ToString()!;
                string def = reader["CheckDefinition"].ToString()!;
                colDefinitions.Add($"    CONSTRAINT [{name}] CHECK {def}");
            }

            // 5. Secondary Indexes
            await reader.NextResultAsync();
            var indexes = new Dictionary<string, (bool IsUnique, string IndexType, string Filter, List<(string Col, bool IsDesc)> Keys, List<string> Includes)>();
            while (await reader.ReadAsync())
            {
                string idxName = reader["IndexName"].ToString()!;
                bool isUnique = Convert.ToBoolean(reader["IsUnique"]);
                string idxType = reader["IndexType"].ToString()!;
                string filter = reader["FilterDefinition"].ToString()!;
                string col = reader["ColumnName"].ToString()!;
                bool isDesc = Convert.ToBoolean(reader["IsDescending"]);
                bool isInc = Convert.ToBoolean(reader["IsIncluded"]);

                if (!indexes.TryGetValue(idxName, out var entry))
                {
                    entry = (isUnique, idxType, filter, new List<(string, bool)>(), new List<string>());
                    indexes[idxName] = entry;
                }
                if (isInc)
                    entry.Includes.Add(col);
                else
                    entry.Keys.Add((col, isDesc));
            }

            if (colDefinitions.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();



            sb.AppendLine($"CREATE TABLE {fullName} (");
            sb.AppendLine(string.Join(",\r\n", colDefinitions));
            sb.AppendLine(");");
            sb.AppendLine("GO");

            if (indexes.Count > 0)
            {
    
                sb.AppendLine();
                foreach (var (idxName, entry) in indexes)
                {
                    string uqStr = entry.IsUnique ? "UNIQUE " : "";
                    string keyCols = string.Join(", ", entry.Keys.Select(k => $"[{k.Col}]" + (k.IsDesc ? " DESC" : " ASC")));
                    sb.Append($"CREATE {uqStr}{entry.IndexType} INDEX [{idxName}] ON {fullName} ({keyCols})");
                    if (entry.Includes.Count > 0)
                    {
                        sb.Append($" INCLUDE ({string.Join(", ", entry.Includes.Select(c => $"[{c}]"))})");
                    }
                    if (!string.IsNullOrEmpty(entry.Filter))
                    {
                        sb.Append($" WHERE {entry.Filter}");
                    }
                    sb.AppendLine(";");
                    sb.AppendLine("GO");
                }
            }

            if (boundDefaults.Count > 0)
            {
                sb.AppendLine();
                foreach (var (bSchema, bName, col) in boundDefaults)
                {
                    string defFull = string.IsNullOrEmpty(bSchema) ? $"[{bName}]" : $"[{bSchema}].[{bName}]";
                    sb.AppendLine($"EXEC sp_bindefault N'{defFull}', N'{fullName}.[{col}]';");
                    sb.AppendLine("GO");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Table DDL for {FullName}", fullName);
            return $"-- Error generating Table DDL: {ex.Message}";
        }
    }

    private static string FormatSqlType(string typeName, short maxLength, byte precision, byte scale)
    {
        string lower = typeName.ToLowerInvariant();
        switch (lower)
        {
            case "varchar":
            case "varbinary":
                return maxLength == -1 ? $"{typeName.ToUpperInvariant()}(MAX)" : $"{typeName.ToUpperInvariant()}({maxLength})";
            case "nvarchar":
                return maxLength == -1 ? $"{typeName.ToUpperInvariant()}(MAX)" : $"{typeName.ToUpperInvariant()}({maxLength / 2})";
            case "char":
            case "binary":
                return $"{typeName.ToUpperInvariant()}({maxLength})";
            case "nchar":
                return $"{typeName.ToUpperInvariant()}({maxLength / 2})";
            case "decimal":
            case "numeric":
                return $"{typeName.ToUpperInvariant()}({precision}, {scale})";
            case "datetime2":
            case "time":
            case "datetimeoffset":
                return $"{typeName.ToUpperInvariant()}({scale})";
            default:
                return typeName.ToUpperInvariant();
        }
    }

    public async Task LoadDefinitionAsync(string fullName)
    {
        if (string.IsNullOrEmpty(_connectionService.ConnectionString)) return;

        // Try to match against table first (generate DDL)
        var table = Tables.FirstOrDefault(t => t.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));
        if (table != null)
        {
            table.Definition = await GenerateTableDdlAsync(fullName);
            table.IsDefinitionLoaded = true;
            return;
        }

        try
        {
            using var conn = new SqlConnection(_connectionService.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT OBJECT_DEFINITION(OBJECT_ID(@FullName)) AS Def";
            cmd.Parameters.AddWithValue("@FullName", fullName);
            var result = await cmd.ExecuteScalarAsync() as string;

            // Try to match against view, sp, or function
            var view = Views.FirstOrDefault(v => v.FullName == fullName);
            if (view != null) { view.Definition = result; view.IsDefinitionLoaded = true; return; }

            var sp = StoredProcedures.FirstOrDefault(s => s.FullName == fullName);
            if (sp != null) { sp.Definition = result; sp.IsDefinitionLoaded = true; return; }

            var fn = Functions.FirstOrDefault(f => f.FullName == fullName);
            if (fn != null) { fn.Definition = result; fn.IsDefinitionLoaded = true; }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching definition for {FullName}", fullName);
            OnError?.Invoke($"Error fetching definition for {fullName}: {ex.Message}");
        }
    }
}
