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

        // Deduplicate while preserving insertion order (first table = root)
        selectedTableNames = selectedTableNames.Distinct().ToList();

        // Build Adjacency Graph with directional edge weights:
        //   Forward FK (Child -> Parent) = cost 1  (preferred, non-expanding)
        //   Reverse    (Parent -> Child) = cost 2  (may multiply rows)
        var adj = new Dictionary<string, List<Edge>>();
        foreach (var t in _schemaService.Tables)
        {
            if (!adj.ContainsKey(t.FullName)) adj[t.FullName] = new List<Edge>();

            foreach (var r in t.OutgoingKeys)
            {
                // Forward: source has FK pointing to target (Child -> Parent)
                adj[t.FullName].Add(new Edge
                {
                    To = r.ToTable, FromCol = r.FromColumn, ToCol = r.ToColumn,
                    IsForwardFK = true
                });

                // Reverse: traversing back from Parent to Child
                if (!adj.ContainsKey(r.ToTable)) adj[r.ToTable] = new List<Edge>();
                adj[r.ToTable].Add(new Edge
                {
                    To = r.FromTable, FromCol = r.ToColumn, ToCol = r.FromColumn,
                    IsForwardFK = false
                });
            }
        }

        var connectedInfo = ConnectTables(selectedTableNames, adj);
        if (connectedInfo == null) return "-- Could not find a path connecting these tables.";

        // ── Alias Assignment: meaningful abbreviations (e.g. OrderDetails → od) ──
        var aliasMap = new Dictionary<string, string>();

        string MakeBaseAlias(string fullTableName)
        {
            // Extract table name from [schema].[Table]
            var parts = fullTableName.Split(new[] { '[', ']', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var tableName = parts.LastOrDefault() ?? fullTableName;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < tableName.Length; i++)
            {
                char c = tableName[i];
                if (i == 0 && char.IsLetter(c))
                {
                    sb.Append(char.ToLower(c));
                }
                else if (char.IsUpper(c) && i > 0)
                {
                    // PascalCase boundary
                    sb.Append(char.ToLower(c));
                }
                else if ((c == '_' || c == ' ') && i + 1 < tableName.Length && char.IsLetter(tableName[i + 1]))
                {
                    // underscore_case: take the next letter
                    sb.Append(char.ToLower(tableName[i + 1]));
                }
            }

            var alias = sb.ToString();
            return string.IsNullOrEmpty(alias) ? "t" : alias;
        }

        string GetAlias(string table)
        {
            if (!aliasMap.ContainsKey(table))
            {
                var baseAlias = MakeBaseAlias(table);
                var alias = baseAlias;
                int counter = 2;
                while (aliasMap.Values.Contains(alias))
                    alias = baseAlias + counter++;
                aliasMap[table] = alias;
            }
            return aliasMap[table];
        }

        // Assign root alias first so it gets the "clean" abbreviation
        GetAlias(connectedInfo.Root);

        // ── Detect one-to-many joins ──
        bool hasOneToMany = connectedInfo.Joins.Any(j => j.IsOneToMany);

        // ── Build JOIN clauses ──
        var joinSb = new System.Text.StringBuilder();
        foreach (var join in connectedInfo.Joins)
        {
            var srcAlias = GetAlias(join.SourceTable);
            var tgtAlias = GetAlias(join.TargetTable);
            var otmNote = join.IsOneToMany ? " -- ⚠️ One-to-Many: rows may multiply" : "";
            joinSb.AppendLine($"    {join.JoinType} {join.TargetTable} AS {tgtAlias}" +
                              $" ON {srcAlias}.{join.SourceCol} = {tgtAlias}.{join.TargetCol}{otmNote}");
        }

        // ── Build SELECT ──
        var sb = new System.Text.StringBuilder();

        if (hasOneToMany)
            sb.AppendLine("-- ⚠️ Warning: One-to-Many join detected. DISTINCT applied to suppress duplicate rows.");

        sb.AppendLine(hasOneToMany ? "SELECT DISTINCT" : "SELECT");

        if (selectedColumns == null || selectedColumns.Count == 0)
        {
            sb.AppendLine("    *");
        }
        else
        {
            var cols = new List<string>();
            foreach (var col in selectedColumns)
            {
                // Find longest matching table key (handles schema.table prefix)
                string bestMatch = "";
                foreach (var t in aliasMap.Keys)
                {
                    if (col.StartsWith(t) && t.Length > bestMatch.Length)
                    {
                        if (col.Length > t.Length && col[t.Length] == '.')
                            bestMatch = t;
                    }
                }

                if (!string.IsNullOrEmpty(bestMatch))
                {
                    var alias = aliasMap[bestMatch];
                    var colName = col.Substring(bestMatch.Length + 1);
                    cols.Add($"    {alias}.{colName}");
                }
                else
                {
                    cols.Add($"    {col}");
                }
            }
            sb.AppendLine(string.Join(",\n", cols));
        }

        sb.AppendLine($"FROM {connectedInfo.Root} AS {aliasMap[connectedInfo.Root]}");
        sb.Append(joinSb);
        sb.AppendLine("-- WHERE");
        sb.Append("-- ORDER BY");

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Connect all selected tables using Dijkstra-weighted paths
    // ─────────────────────────────────────────────────────────────────────────
    private ConnectedResult? ConnectTables(List<string> tables, Dictionary<string, List<Edge>> adj)
    {
        var root = tables[0];
        var connectedSet = new HashSet<string> { root };
        var joins = new List<JoinDef>();
        var remaining = new HashSet<string>(tables.Skip(1));

        while (remaining.Count > 0)
        {
            var path = FindShortestPath(connectedSet, remaining, adj);
            if (path == null) return null;

            foreach (var step in path)
            {
                if (!connectedSet.Contains(step.Target))
                {
                    connectedSet.Add(step.Target);

                    var (joinType, isOneToMany) = DetermineJoinType(step);
                    joins.Add(new JoinDef
                    {
                        SourceTable = step.Source,
                        TargetTable = step.Target,
                        SourceCol = step.SourceCol,
                        TargetCol = step.TargetCol,
                        JoinType = joinType,
                        IsOneToMany = isOneToMany
                    });

                    remaining.Remove(step.Target);
                }
            }
        }

        return new ConnectedResult { Root = root, Joins = joins };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dijkstra: forward FK edges cost 1 (Child→Parent), reverse cost 2 (Parent→Child)
    // This steers the pathfinder toward non-expanding, FK-natural join paths.
    // ─────────────────────────────────────────────────────────────────────────
    private List<PathStep>? FindShortestPath(
        HashSet<string> sources, HashSet<string> targets, Dictionary<string, List<Edge>> adj)
    {
        var dist = new Dictionary<string, int>();
        var parent = new Dictionary<string, PathStep>();

        // SortedSet as a priority queue: (cost, node)
        var pq = new SortedSet<(int Cost, string Node)>(
            Comparer<(int Cost, string Node)>.Create((a, b) =>
            {
                int c = a.Cost.CompareTo(b.Cost);
                return c != 0 ? c : string.Compare(a.Node, b.Node, StringComparison.Ordinal);
            }));

        foreach (var s in sources)
        {
            dist[s] = 0;
            pq.Add((0, s));
        }

        string? foundTarget = null;

        while (pq.Count > 0)
        {
            var (cost, u) = pq.Min;
            pq.Remove(pq.Min);

            if (targets.Contains(u) && !sources.Contains(u))
            {
                foundTarget = u;
                break;
            }

            // Stale entry — skip
            if (dist.TryGetValue(u, out var bestSoFar) && cost > bestSoFar) continue;

            if (!adj.TryGetValue(u, out var edges)) continue;

            foreach (var e in edges)
            {
                var edgeCost = e.IsForwardFK ? 1 : 2;
                var newCost = cost + edgeCost;

                if (!dist.ContainsKey(e.To) || newCost < dist[e.To])
                {
                    dist[e.To] = newCost;
                    parent[e.To] = new PathStep
                    {
                        Source = u, Target = e.To,
                        SourceCol = e.FromCol, TargetCol = e.ToCol,
                        IsForwardFK = e.IsForwardFK
                    };
                    pq.Add((newCost, e.To));
                }
            }
        }

        if (foundTarget == null) return null;

        // Backtrack path
        var path = new List<PathStep>();
        var curr = foundTarget;
        while (!sources.Contains(curr))
        {
            if (!parent.ContainsKey(curr)) break;
            var step = parent[curr];
            path.Add(step);
            curr = step.Source;
        }
        path.Reverse();
        return path;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Determine JOIN type and whether the join is one-to-many
    // ─────────────────────────────────────────────────────────────────────────
    private (string JoinType, bool IsOneToMany) DetermineJoinType(PathStep step)
    {
        var sourceTable = _schemaService.Tables.FirstOrDefault(t => t.FullName == step.Source);
        if (sourceTable == null) return ("INNER JOIN", false);

        // Source has FK pointing to Target → Child→Parent (Many-to-One)
        var outgoing = sourceTable.OutgoingKeys.FirstOrDefault(r =>
            r.ToTable == step.Target && r.FromColumn == step.SourceCol);

        if (outgoing != null)
        {
            var joinType = outgoing.IsNullable ? "LEFT JOIN" : "INNER JOIN";
            return (joinType, false); // Many-to-One: no row multiplication
        }

        // Target has FK pointing to Source → Parent→Child (One-to-Many: rows may multiply!)
        var targetTable = _schemaService.Tables.FirstOrDefault(t => t.FullName == step.Target);
        var incoming = targetTable?.OutgoingKeys.FirstOrDefault(r =>
            r.ToTable == step.Source && r.ToColumn == step.SourceCol);

        if (incoming != null)
            return ("LEFT JOIN", true); // One-to-Many: flag it

        return ("INNER JOIN", false);
    }

    // ── Internal model classes ────────────────────────────────────────────────

    private class Edge
    {
        public string To { get; set; } = string.Empty;
        public string FromCol { get; set; } = string.Empty;
        public string ToCol { get; set; } = string.Empty;
        public bool IsForwardFK { get; set; } = false; // true = Child→Parent (preferred)
    }

    private class PathStep
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string SourceCol { get; set; } = string.Empty;
        public string TargetCol { get; set; } = string.Empty;
        public bool IsForwardFK { get; set; } = false;
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
        public string JoinType { get; set; } = "INNER JOIN";
        public bool IsOneToMany { get; set; } = false;
    }
}
