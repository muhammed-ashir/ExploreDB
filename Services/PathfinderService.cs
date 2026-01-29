namespace DbExplore.Services;

public class PathfinderService
{
    private readonly SchemaService _schemaService;

    public PathfinderService(SchemaService schemaService)
    {
        _schemaService = schemaService;
    }

    public string GenerateQuery(List<string> selectedTableNames, List<string> selectedColumns)
    {
        if (selectedTableNames.Count == 0) return "-- No tables selected";
        // Deduplicate
        selectedTableNames = selectedTableNames.Distinct().ToList();

        // Build Adjacency Graph (Undirected)
        var adj = new Dictionary<string, List<Edge>>();
        foreach (var t in _schemaService.Tables)
        {
            if (!adj.ContainsKey(t.FullName)) adj[t.FullName] = new List<Edge>();
            
            // Outgoing: T1 -> T2 (T1 has FK)
            foreach (var r in t.OutgoingKeys)
            {
                adj[t.FullName].Add(new Edge { To = r.ToTable, FromCol = r.FromColumn, ToCol = r.ToColumn });
                
                // Add Reverse: T2 -> T1
                if (!adj.ContainsKey(r.ToTable)) adj[r.ToTable] = new List<Edge>();
                adj[r.ToTable].Add(new Edge { To = r.FromTable, FromCol = r.ToColumn, ToCol = r.FromColumn });
            }
        }

        // Algo: Prim-like or iterative BFS to connect all selected nodes
        var connectedInfo = ConnectTables(selectedTableNames, adj);
        if (connectedInfo == null) return "-- Could not find a path connecting these tables.";

        // Assign Aliases
        var aliasMap = new Dictionary<string, string>();
        int aliasCounter = 1;
        
        string GetAlias(string table)
        {
            if (!aliasMap.ContainsKey(table))
                aliasMap[table] = $"T{aliasCounter++}";
            return aliasMap[table];
        }

        // Assign alias to root
        GetAlias(connectedInfo.Root);
        
        // Build Join Clauses and assign aliases to joined tables
        var joinSb = new System.Text.StringBuilder();
        foreach (var join in connectedInfo.Joins)
        {
            var sourceAlias = GetAlias(join.SourceTable);
            var targetAlias = GetAlias(join.TargetTable);
            
            joinSb.AppendLine($"JOIN {join.TargetTable} AS {targetAlias} ON {sourceAlias}.{join.SourceCol} = {targetAlias}.{join.TargetCol}");
        }

        // Generate SELECT list
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SELECT");
        if (selectedColumns == null || selectedColumns.Count == 0)
        {
            sb.AppendLine("    *");
        }
        else
        {
            var cols = new List<string>();
            foreach(var col in selectedColumns)
            {
                // col format: [Schema].[Table].Column
                // We need to match the table part to the alias
                // Simple parsing: iterate aliases to find prefix match?
                // Or just use the known table part if we passed structured data.
                // We receive full string "[s].[t].Col".
                // Let's find longest matching table name from aliasMap keys.
                
                string bestMatchTable = "";
                foreach(var t in aliasMap.Keys)
                {
                    if (col.StartsWith(t) && t.Length > bestMatchTable.Length)
                    {
                        // ensure it's followed by a dot
                        if (col.Length > t.Length && col[t.Length] == '.')
                        {
                            bestMatchTable = t;
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(bestMatchTable))
                {
                    var alias = aliasMap[bestMatchTable];
                    var colName = col.Substring(bestMatchTable.Length + 1); // remove table + dot
                    cols.Add($"    {alias}.{colName}");
                }
                else
                {
                    // Fallback
                    cols.Add($"    {col}");
                }
            }
            sb.AppendLine(string.Join(",\n", cols));
        }
        
        sb.AppendLine($"FROM {connectedInfo.Root} AS {aliasMap[connectedInfo.Root]}");
        sb.Append(joinSb.ToString());

        return sb.ToString();
    }

    private ConnectedResult? ConnectTables(List<string> tables, Dictionary<string, List<Edge>> adj)
    {
        // Start from first table
        var root = tables[0];
        
        var connectedSet = new HashSet<string>();
        connectedSet.Add(root);
        
        var joins = new List<JoinDef>();
        
        // Tables we still need to reach
        var remainingTargets = new HashSet<string>(tables.Skip(1));

        while (remainingTargets.Count > 0)
        {
            // Find shortest path from ANY node in connectedSet to ANY node in remainingTargets
            var path = FindShortestPath(connectedSet, remainingTargets, adj);
            
            if (path == null) 
            {
                return null;
            }
            
            // Add path to connectedSet and joins
            foreach (var step in path)
            {
                if (!connectedSet.Contains(step.Target))
                {
                    connectedSet.Add(step.Target);
                    joins.Add(new JoinDef 
                    { 
                        SourceTable = step.Source,
                        TargetTable = step.Target, 
                        SourceCol = step.SourceCol,
                        TargetCol = step.TargetCol
                    });
                     
                    if (remainingTargets.Contains(step.Target))
                    {
                        remainingTargets.Remove(step.Target);
                    }
                }
            }
        }
        
        return new ConnectedResult { Root = root, Joins = joins };

    }

    private List<PathStep>? FindShortestPath(HashSet<string> sources, HashSet<string> targets, Dictionary<string, List<Edge>> adj)
    {
        // BFS
        var queue = new Queue<string>();
        var parent = new Dictionary<string, PathStep>(); // ToNode -> Step
        
        foreach (var s in sources) queue.Enqueue(s);
        
        string? foundTarget = null;
        var visitedLocal = new HashSet<string>(sources); 

        while(queue.Count > 0)
        {
            var u = queue.Dequeue();
            
            if (targets.Contains(u) && !sources.Contains(u)) 
            {
                foundTarget = u;
                break;
            }

            if (adj.TryGetValue(u, out var edges))
            {
                foreach(var e in edges)
                {
                    if (!visitedLocal.Contains(e.To))
                    {
                        visitedLocal.Add(e.To);
                        parent[e.To] = new PathStep { Source = u, Target = e.To, SourceCol = e.FromCol, TargetCol = e.ToCol };
                        queue.Enqueue(e.To);
                    }
                }
            }
        }

        if (foundTarget == null) return null;

        // Backtrack
        var path = new List<PathStep>();
        var curr = foundTarget;
        
        while(!sources.Contains(curr))
        {
             if (!parent.ContainsKey(curr)) break; 
             var step = parent[curr];
             path.Add(step);
             curr = step.Source;
        }
        path.Reverse();
        return path;
    }

    private class Edge
    {
        public string To { get; set; } = string.Empty;
        public string FromCol { get; set; } = string.Empty;
        public string ToCol { get; set; } = string.Empty;
    }
    
    private class PathStep
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string SourceCol { get; set; } = string.Empty;
        public string TargetCol { get; set; } = string.Empty;
    }

    private class ConnectedResult
    {
        public string Root { get; set; } = string.Empty;
        public List<JoinDef> Joins { get; set; } = new();
    }

    private class JoinDef
    {
        public string SourceTable { get; set; } = string.Empty;
        public string TargetTable { get; set; } = string.Empty;
        public string SourceCol { get; set; } = string.Empty;
        public string TargetCol { get; set; } = string.Empty;
    }
}
