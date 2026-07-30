namespace ExploreDB.Services;

public sealed class PathfinderService
{
    private readonly SchemaService _schemaService;

    public PathfinderService(SchemaService schemaService)
    {
        _schemaService = schemaService;
    }

    public PathfinderQueryResult GenerateQuery(List<string> selectedTableNames, List<string> selectedColumns, IReadOnlySet<string>? ignoredRoutingTables = null, IReadOnlySet<string>? ignoredRoutingEdges = null)
    {
        if (selectedTableNames.Count == 0)
        {
            return new PathfinderQueryResult
            {
                Sql = "-- Select columns on the left to generate a query."
            };
        }

        var joinPlan = GenerateJoinPlan(selectedTableNames, ignoredRoutingTables: ignoredRoutingTables, ignoredRoutingEdges: ignoredRoutingEdges);
        if (!string.IsNullOrWhiteSpace(joinPlan.ErrorMessage))
        {
            return new PathfinderQueryResult
            {
                Sql = $"-- {joinPlan.ErrorMessage}",
                JoinPlan = joinPlan
            };
        }

        if (joinPlan.UnreachableTables.Count > 0)
        {
            return new PathfinderQueryResult
            {
                Sql = $"-- Could not connect these tables automatically: {string.Join(", ", joinPlan.UnreachableTables.Select(FormatShortName))}.",
                JoinPlan = joinPlan
            };
        }

        var projectedColumns = selectedColumns
            .Select(TryParseSelectedColumn)
            .Where(column => column != null)
            .Cast<SelectedColumn>()
            .ToList();

        if (projectedColumns.Count == 0)
        {
            return new PathfinderQueryResult
            {
                Sql = "-- Select columns on the left to generate a query.",
                JoinPlan = joinPlan
            };
        }

        var missingAliases = projectedColumns
            .Where(column => joinPlan.GetAliasForTable(column.TableFullName) == null)
            .Select(column => column.TableFullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingAliases.Count > 0)
        {
            return new PathfinderQueryResult
            {
                Sql = $"-- Could not resolve aliases for these tables: {string.Join(", ", missingAliases.Select(FormatShortName))}.",
                JoinPlan = joinPlan
            };
        }

        var newLine = Environment.NewLine;
        var selectClause = string.Join(
            $",{newLine}",
            projectedColumns.Select(column =>
            {
                var alias = joinPlan.GetAliasForTable(column.TableFullName)!;
                return $"    {alias}.{QuoteIdentifier(column.ColumnName)} AS {QuoteIdentifier(BuildColumnAlias(column.TableFullName, column.ColumnName))}";
            }));

        var sql =
            $"SELECT {(joinPlan.HasOneToMany ? "DISTINCT " : string.Empty)}{newLine}" +
            $"{selectClause}{newLine}" +
            $"FROM {joinPlan.RootTable} {joinPlan.RootAlias}";

        if (joinPlan.Steps.Count > 0)
        {
            var joinLines = string.Join(
                newLine,
                joinPlan.Steps.Select(step => $"{step.JoinType} {step.FullName} {step.Alias} ON {step.OnClause}"));

            sql += $"{newLine}{joinLines}";
        }

        sql += $"{newLine};";

        return new PathfinderQueryResult
        {
            Sql = sql,
            HasOneToMany = joinPlan.HasOneToMany,
            JoinPlan = joinPlan
        };
    }

    public PathfinderQueryResult GenerateJoinedUpdateQuery(PathfinderJoinPlan joinPlan, IReadOnlyDictionary<string, string?> columnMappings)
    {
        if (!string.IsNullOrWhiteSpace(joinPlan.ErrorMessage))
        {
            return new PathfinderQueryResult
            {
                Sql = $"-- {joinPlan.ErrorMessage}",
                JoinPlan = joinPlan
            };
        }

        if (joinPlan.UnreachableTables.Count > 0)
        {
            return new PathfinderQueryResult
            {
                Sql = $"-- Could not connect these tables automatically: {string.Join(", ", joinPlan.UnreachableTables.Select(FormatShortName))}.",
                JoinPlan = joinPlan
            };
        }

        var newLine = Environment.NewLine;
        
        string setLines;
        if (columnMappings.Count == 0)
        {
            setLines = "    -- TODO: Map your columns here";
        }
        else
        {
            var setList = new List<string>();
            foreach (var kvp in columnMappings)
            {
                var targetCol = kvp.Key;
                var sourceVal = kvp.Value;
                
                if (string.IsNullOrEmpty(sourceVal))
                {
                    setList.Add($"    {joinPlan.RootAlias}.{QuoteIdentifier(targetCol)} = @set_{targetCol}");
                    continue;
                }
                
                // sourceVal is expected to be like "[dbo].[OtherTable].ColumnName"
                var lastDot = sourceVal.LastIndexOf('.');
                if (lastDot > -1)
                {
                    var sourceTableName = sourceVal.Substring(0, lastDot);
                    var sourceColName = sourceVal.Substring(lastDot + 1);
                    var alias = joinPlan.GetAliasForTable(sourceTableName);
                    
                    if (alias != null)
                    {
                        setList.Add($"    {joinPlan.RootAlias}.{QuoteIdentifier(targetCol)} = {alias}.{QuoteIdentifier(sourceColName)}");
                        continue;
                    }
                }
                
                // Fallback if we couldn't resolve the alias perfectly
                setList.Add($"    {joinPlan.RootAlias}.{QuoteIdentifier(targetCol)} = {sourceVal}");
            }
            setLines = string.Join($",{newLine}", setList);
        }

        var sql =
            $"UPDATE {joinPlan.RootAlias}{newLine}" +
            $"SET{newLine}" +
            $"{setLines}{newLine}" +
            $"FROM {joinPlan.RootTable} {joinPlan.RootAlias}";

        if (joinPlan.Steps.Count > 0)
        {
            var joinLines = string.Join(
                newLine,
                joinPlan.Steps.Select(step => $"{step.JoinType} {step.FullName} {step.Alias} ON {step.OnClause}"));

            sql += $"{newLine}{joinLines}";
        }

        sql += $"{newLine}WHERE <add_condition_here>;";

        return new PathfinderQueryResult
        {
            Sql = sql,
            HasOneToMany = joinPlan.HasOneToMany,
            JoinPlan = joinPlan
        };
    }

    public PathfinderQueryResult GenerateJoinedDeleteQuery(PathfinderJoinPlan joinPlan)
    {
        if (!string.IsNullOrWhiteSpace(joinPlan.ErrorMessage))
        {
            return new PathfinderQueryResult
            {
                Sql = $"-- {joinPlan.ErrorMessage}",
                JoinPlan = joinPlan
            };
        }

        if (joinPlan.UnreachableTables.Count > 0)
        {
            return new PathfinderQueryResult
            {
                Sql = $"-- Could not connect these tables automatically: {string.Join(", ", joinPlan.UnreachableTables.Select(FormatShortName))}.",
                JoinPlan = joinPlan
            };
        }

        var newLine = Environment.NewLine;

        var sql =
            $"DELETE {joinPlan.RootAlias}{newLine}" +
            $"FROM {joinPlan.RootTable} {joinPlan.RootAlias}";

        if (joinPlan.Steps.Count > 0)
        {
            var joinLines = string.Join(
                newLine,
                joinPlan.Steps.Select(step => $"{step.JoinType} {step.FullName} {step.Alias} ON {step.OnClause}"));

            sql += $"{newLine}{joinLines}";
        }

        sql += $"{newLine}WHERE <add_condition_here>;";

        return new PathfinderQueryResult
        {
            Sql = sql,
            HasOneToMany = joinPlan.HasOneToMany,
            JoinPlan = joinPlan
        };
    }

    public PathfinderJoinPlan GenerateJoinPlan(
        IReadOnlyList<string> selectedTableNames,
        string? rootTable = null,
        string rootAlias = "t0",
        string joinedAliasPrefix = "j",
        IReadOnlySet<string>? ignoredRoutingTables = null,
        IReadOnlySet<string>? ignoredRoutingEdges = null)
    {
        if (_schemaService.Tables.Count == 0)
        {
            return PathfinderJoinPlan.CreateError("Load a schema before generating joins.");
        }

        var targets = selectedTableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolvedRoot = !string.IsNullOrWhiteSpace(rootTable)
            ? rootTable
            : targets.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            return PathfinderJoinPlan.CreateError("Select at least one table to generate a join path.");
        }

        if (!_schemaService.Tables.Any(table => string.Equals(table.FullName, resolvedRoot, StringComparison.OrdinalIgnoreCase)))
        {
            return PathfinderJoinPlan.CreateError($"The root table {FormatShortName(resolvedRoot)} could not be found in the loaded schema.");
        }

        targets = targets
            .Where(name => !string.Equals(name, resolvedRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var adjacency = BuildGraph();
        if (!adjacency.ContainsKey(resolvedRoot))
        {
            adjacency[resolvedRoot] = new List<GraphEdge>();
        }

        var aliasByTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [resolvedRoot] = rootAlias
        };
        var steps = new List<PathfinderJoinStep>();
        var unreachableTables = new List<string>();
        int nextAliasIndex = 1;

        foreach (var target in targets)
        {
            var path = FindShortestPath(adjacency, resolvedRoot, target, ignoredRoutingTables, ignoredRoutingEdges);
            if (path == null)
            {
                unreachableTables.Add(target);
                continue;
            }

            foreach (var edge in path)
            {
                if (aliasByTable.ContainsKey(edge.TargetTable))
                {
                    continue;
                }

                if (!aliasByTable.TryGetValue(edge.SourceTable, out var sourceAlias))
                {
                    unreachableTables.Add(target);
                    break;
                }

                var targetAlias = $"{joinedAliasPrefix}{nextAliasIndex++}";
                aliasByTable[edge.TargetTable] = targetAlias;

                steps.Add(new PathfinderJoinStep
                {
                    Key = BuildStepKey(edge),
                    FullName = edge.TargetTable,
                    Alias = targetAlias,
                    DisplayName = FormatShortName(edge.TargetTable),
                    ConnectedFromTable = edge.SourceTable,
                    ConnectedFromAlias = sourceAlias,
                    DirectionLabel = edge.DirectionLabel,
                    OnClause = $"{sourceAlias}.{QuoteIdentifier(edge.SourceColumn)} = {targetAlias}.{QuoteIdentifier(edge.TargetColumn)}",
                    JoinType = edge.DefaultJoinType,
                    DefaultJoinType = edge.DefaultJoinType,
                    IsOneToMany = edge.IsOneToMany
                });
            }
        }

        var requestedTableSet = targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            if (requestedTableSet.Contains(step.FullName))
            {
                step.IsSelectedTable = true;
            }
        }

        if (targets.Count > 0 && steps.Count == 0 && unreachableTables.Count == targets.Count)
        {
            return PathfinderJoinPlan.CreateError($"Could not discover a join path from {FormatShortName(resolvedRoot)} to the selected tables.");
        }

        return new PathfinderJoinPlan
        {
            RootTable = resolvedRoot,
            RootAlias = rootAlias,
            Steps = steps,
            UnreachableTables = unreachableTables,
            HasOneToMany = steps.Any(step => step.IsOneToMany)
        };
    }

    private Dictionary<string, List<GraphEdge>> BuildGraph()
    {
        var graph = new Dictionary<string, List<GraphEdge>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in _schemaService.Tables)
        {
            graph.TryAdd(table.FullName, new List<GraphEdge>());

            foreach (var relationship in table.OutgoingKeys)
            {
                graph.TryAdd(relationship.ToTable, new List<GraphEdge>());

                graph[relationship.FromTable].Add(new GraphEdge
                {
                    SourceTable = relationship.FromTable,
                    TargetTable = relationship.ToTable,
                    SourceColumn = relationship.FromColumn,
                    TargetColumn = relationship.ToColumn,
                    IsOneToMany = false,
                    DefaultJoinType = relationship.IsNullable ? "LEFT JOIN" : "INNER JOIN",
                    Weight = relationship.IsNullable ? 2 : 1,
                    DirectionLabel = $"Parent hop on {relationship.FromColumn} -> {relationship.ToColumn}"
                });

                graph[relationship.ToTable].Add(new GraphEdge
                {
                    SourceTable = relationship.ToTable,
                    TargetTable = relationship.FromTable,
                    SourceColumn = relationship.ToColumn,
                    TargetColumn = relationship.FromColumn,
                    IsOneToMany = true,
                    DefaultJoinType = "LEFT JOIN",
                    Weight = 5,
                    DirectionLabel = $"Child hop on {relationship.ToColumn} -> {relationship.FromColumn}"
                });
            }
        }

        return graph;
    }

    private static List<GraphEdge>? FindShortestPath(
        IReadOnlyDictionary<string, List<GraphEdge>> adjacency,
        string rootTable,
        string targetTable,
        IReadOnlySet<string>? ignoredRoutingTables,
        IReadOnlySet<string>? ignoredRoutingEdges)
    {
        if (string.Equals(rootTable, targetTable, StringComparison.OrdinalIgnoreCase))
        {
            return new List<GraphEdge>();
        }

        var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [rootTable] = 0
        };
        var previous = new Dictionary<string, GraphEdge>(StringComparer.OrdinalIgnoreCase);
        var queue = new PriorityQueue<string, int>();
        queue.Enqueue(rootTable, 0);

        while (queue.TryDequeue(out var current, out var currentDistance))
        {
            if (string.Equals(current, targetTable, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!adjacency.TryGetValue(current, out var edges))
            {
                continue;
            }

            if (distances.TryGetValue(current, out var bestDistance) && currentDistance > bestDistance)
            {
                continue;
            }

            foreach (var edge in edges)
            {
                if (ignoredRoutingTables != null && 
                    ignoredRoutingTables.Contains(edge.TargetTable) && 
                    !string.Equals(edge.TargetTable, targetTable, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ignoredRoutingEdges != null)
                {
                    var edgeKey = BuildStepKey(edge);
                    var reverseEdgeKey = $"{edge.TargetTable}|{edge.SourceTable}|{edge.TargetColumn}|{edge.SourceColumn}";
                    if (ignoredRoutingEdges.Contains(edgeKey) || ignoredRoutingEdges.Contains(reverseEdgeKey))
                    {
                        continue;
                    }
                }

                var nextDistance = currentDistance + edge.Weight;
                if (distances.TryGetValue(edge.TargetTable, out var knownDistance) && nextDistance >= knownDistance)
                {
                    continue;
                }

                distances[edge.TargetTable] = nextDistance;
                previous[edge.TargetTable] = edge;
                queue.Enqueue(edge.TargetTable, nextDistance);
            }
        }

        if (!previous.ContainsKey(targetTable))
        {
            return null;
        }

        var path = new List<GraphEdge>();
        var currentTable = targetTable;

        while (!string.Equals(currentTable, rootTable, StringComparison.OrdinalIgnoreCase))
        {
            if (!previous.TryGetValue(currentTable, out var edge))
            {
                return null;
            }

            path.Add(edge);
            currentTable = edge.SourceTable;
        }

        path.Reverse();
        return path;
    }

    private static SelectedColumn? TryParseSelectedColumn(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var separatorIndex = key.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= key.Length - 1)
        {
            return null;
        }

        return new SelectedColumn
        {
            TableFullName = key[..separatorIndex],
            ColumnName = key[(separatorIndex + 1)..]
        };
    }

    private static string BuildColumnAlias(string tableFullName, string columnName)
    {
        var tableName = GetObjectName(tableFullName);
        var alias = $"{tableName}_{columnName}";
        return new string(alias.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
    }

    private static string GetObjectName(string fullName)
    {
        var parts = fullName.Split('.', 2);
        var lastPart = parts.Length == 2 ? parts[1] : fullName;
        return lastPart.Replace("[", "").Replace("]", "");
    }

    private static string FormatShortName(string fullName)
    {
        return fullName.Replace("[", "").Replace("]", "");
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }

    private static string BuildStepKey(GraphEdge edge)
    {
        return $"{edge.SourceTable}|{edge.TargetTable}|{edge.SourceColumn}|{edge.TargetColumn}";
    }

    private sealed class GraphEdge
    {
        public string SourceTable { get; init; } = string.Empty;
        public string TargetTable { get; init; } = string.Empty;
        public string SourceColumn { get; init; } = string.Empty;
        public string TargetColumn { get; init; } = string.Empty;
        public string DefaultJoinType { get; init; } = "INNER JOIN";
        public string DirectionLabel { get; init; } = string.Empty;
        public int Weight { get; init; }
        public bool IsOneToMany { get; init; }
    }

    private sealed class SelectedColumn
    {
        public string TableFullName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
    }
}

public sealed class PathfinderQueryResult
{
    public string Sql { get; init; } = string.Empty;
    public bool HasOneToMany { get; init; }
    public PathfinderJoinPlan? JoinPlan { get; init; }
}

public sealed class PathfinderJoinPlan
{
    public string RootTable { get; init; } = string.Empty;
    public string RootAlias { get; init; } = "t0";
    public List<PathfinderJoinStep> Steps { get; init; } = new();
    public List<string> UnreachableTables { get; init; } = new();
    public bool HasOneToMany { get; init; }
    public string? ErrorMessage { get; init; }

    public string? GetAliasForTable(string tableFullName)
    {
        if (string.Equals(RootTable, tableFullName, StringComparison.OrdinalIgnoreCase))
        {
            return RootAlias;
        }

        return Steps.FirstOrDefault(step => string.Equals(step.FullName, tableFullName, StringComparison.OrdinalIgnoreCase))?.Alias;
    }

    public static PathfinderJoinPlan CreateError(string message)
    {
        return new PathfinderJoinPlan
        {
            ErrorMessage = message
        };
    }
}

public sealed class PathfinderJoinStep
{
    public string Key { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ConnectedFromTable { get; init; } = string.Empty;
    public string ConnectedFromAlias { get; init; } = string.Empty;
    public string DirectionLabel { get; init; } = string.Empty;
    public string OnClause { get; init; } = string.Empty;
    public string JoinType { get; set; } = "INNER JOIN";
    public string DefaultJoinType { get; init; } = "INNER JOIN";
    public bool IsOneToMany { get; init; }
    public bool IsSelectedTable { get; set; }
}
