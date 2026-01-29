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

    public override string ToString() => FullName;
}

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
}

public class Relationship
{
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty; // The table being referenced
    public string ToColumn { get; set; } = string.Empty;
}

public class SchemaService
{
    private readonly ConnectionService _connectionService;
    public List<TableInfo> Tables { get; private set; } = new();
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

            Console.WriteLine("Fetching Columns...");
            // 2. Get Columns
            var colSql = "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS";
            var rawCols = await conn.QueryAsync<(string Schema, string Table, string Column, string Type)>(colSql);

            foreach(var col in rawCols)
            {
                var key = $"[{col.Schema}].[{col.Table}]";
                if (tableDict.TryGetValue(key, out var table))
                {
                    table.Columns.Add(new ColumnInfo { Name = col.Column, DataType = col.Type });
                }
            }

            Console.WriteLine("Fetching Keys...");
            // 3. Get Foreign Keys
            var fkSql = @"
                SELECT 
                    tp.name AS ParentTable, cp.name AS ParentColumn,
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
                        ToTable = refFull, ToColumn = fk.RefColumn 
                    };
                    
                    // Parent table has the foreign key pointing to Reference table
                    parent.OutgoingKeys.Add(rel);
                    
                    // Reference table is pointed to by Parent table
                    reference.IncomingKeys.Add(rel);
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
