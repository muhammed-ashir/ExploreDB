namespace ExploreDB.Services.SqlCompare;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExploreDB.Services.SqlCompare.Models;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac.Compare;
using Microsoft.SqlServer.Dac.Model;

public interface ISchemaComparisonService
{
    Task PreWarmEngineAsync();

    Task<ComparisonEstimate> EstimateDurationAsync(
        DatabaseEndpoint source,
        DatabaseEndpoint target,
        CancellationToken cancellationToken = default);

    bool IsEngineWarm { get; }

    Task<SchemaComparisonResultModel> CompareAsync(
        DatabaseEndpoint source,
        DatabaseEndpoint target,
        ComparisonOptions options,
        IProgress<ComparisonProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateSynchronizationScriptAsync(
        SchemaComparisonResultModel resultModel,
        CancellationToken cancellationToken = default);
}

public class SchemaComparisonService : ISchemaComparisonService
{
    private static bool _isDacFxInitialized = false;
    private static readonly SemaphoreSlim _warmUpLock = new(1, 1);

    public bool IsEngineWarm => _isDacFxInitialized;

    public async Task PreWarmEngineAsync()
    {
        if (_isDacFxInitialized) return;

        await Task.Run(() =>
        {
            if (_isDacFxInitialized) return;
            if (!_warmUpLock.Wait(0)) return;

            try
            {
                // Reference DacFx compare and endpoint types to trigger assembly loading and JIT compilation
                _ = typeof(SchemaComparison).Assembly;
                _ = typeof(SchemaCompareDatabaseEndpoint).Assembly;

                // Build a minimal in-memory TSqlModel to exercise DacFx metadata schema rules and T-SQL parser
                using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
                model.AddObjects("CREATE TABLE #_explore_db_prewarm (id INT);");

                _isDacFxInitialized = true;
                Debug.WriteLine("[SchemaComparisonService] Background DacFx engine pre-warming completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchemaComparisonService] Background pre-warm notice: {ex.Message}");
            }
            finally
            {
                _warmUpLock.Release();
            }
        });
    }

    public async Task<ComparisonEstimate> EstimateDurationAsync(
        DatabaseEndpoint source,
        DatabaseEndpoint target,
        CancellationToken cancellationToken = default)
    {
        var estimate = new ComparisonEstimate
        {
            IsColdStart = !_isDacFxInitialized
        };

        int totalSeconds = 0;

        // DacFx JIT initialization overhead on first run
        if (estimate.IsColdStart)
        {
            totalSeconds += 30;
        }

        Task<int>? sourceTask = null;
        Task<int>? targetTask = null;

        // Source endpoint
        if (source.Type == EndpointType.LiveDatabase)
        {
            sourceTask = EstimateLiveEndpointSecondsAsync(source, count => estimate.SourceObjectCount = count, cancellationToken);
        }
        else
        {
            totalSeconds += 5; // Dacpac parsing
        }

        // Target endpoint
        if (target.Type == EndpointType.LiveDatabase)
        {
            targetTask = EstimateLiveEndpointSecondsAsync(target, count => estimate.TargetObjectCount = count, cancellationToken);
        }
        else
        {
            totalSeconds += 5; // Dacpac parsing
        }

        // Probe endpoints concurrently to minimize pre-flight wait time
        if (sourceTask != null && targetTask != null)
        {
            var results = await Task.WhenAll(sourceTask, targetTask);
            totalSeconds += results[0] + results[1];
        }
        else if (sourceTask != null)
        {
            totalSeconds += await sourceTask;
        }
        else if (targetTask != null)
        {
            totalSeconds += await targetTask;
        }

        estimate.EstimatedSeconds = Math.Max(10, totalSeconds);
        return estimate;
    }

    private static async Task<int> EstimateLiveEndpointSecondsAsync(
        DatabaseEndpoint endpoint,
        Action<int> setObjectCount,
        CancellationToken cancellationToken)
    {
        double endpointSeconds = 25.0; // Base live reverse engineering time
        try
        {
            var probe = await QuickProbeEndpointAsync(endpoint.BuildConnectionString(), endpoint.Server, cancellationToken);
            setObjectCount(probe.ObjectCount);

            if (probe.ObjectCount > 300)
            {
                endpointSeconds += (probe.ObjectCount - 300) / 200.0 * 4.0;
            }

            double multiplier = CalculateNetworkMultiplier(probe.LatencyMs, probe.IsLocal);
            endpointSeconds *= multiplier;
        }
        catch
        {
            // Fallback: if server is known local, use local multiplier
            if (IsLocalServer(endpoint.Server))
            {
                endpointSeconds *= 0.65;
            }
        }

        return (int)Math.Round(endpointSeconds);
    }

    private readonly record struct EndpointProbeResult(int ObjectCount, double LatencyMs, bool IsLocal);

    private static bool IsLocalServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return false;
        var trimmed = server.Trim().ToLowerInvariant();
        return trimmed == "." ||
               trimmed == "(local)" ||
               trimmed == "localhost" ||
               trimmed == "127.0.0.1" ||
               trimmed == "::1" ||
               trimmed.StartsWith(".\\") ||
               trimmed.StartsWith("(local)\\") ||
               trimmed.StartsWith("localhost\\") ||
               trimmed.StartsWith("127.0.0.1\\");
    }

    private static double CalculateNetworkMultiplier(double latencyMs, bool isLocal)
    {
        if (isLocal)
        {
            // Localhost / Shared Memory IPC has near-zero network transit overhead
            return 0.65;
        }

        if (latencyMs <= 5.0)
        {
            // Ultra-fast LAN / high-speed on-premise network
            return 0.85;
        }
        else if (latencyMs <= 20.0)
        {
            // Standard LAN baseline
            return 1.0;
        }
        else if (latencyMs <= 60.0)
        {
            // Moderate remote / nearby cloud database (e.g. Azure in same region)
            return 1.0 + ((latencyMs - 20.0) / 40.0 * 0.5); // 1.0x -> 1.5x
        }
        else if (latencyMs <= 150.0)
        {
            // Remote cloud / VPN / cross-region database
            return 1.5 + ((latencyMs - 60.0) / 90.0 * 0.7); // 1.5x -> 2.2x
        }
        else
        {
            // High latency / congested VPN (> 150ms), cap at 3.0x
            double excess = Math.Min(latencyMs - 150.0, 300.0);
            return 2.2 + (excess / 300.0 * 0.8); // 2.2x -> 3.0x
        }
    }

    private static async Task<EndpointProbeResult> QuickProbeEndpointAsync(
        string connectionString,
        string? server,
        CancellationToken cancellationToken)
    {
        bool isLocal = IsLocalServer(server);
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = 4
        };

        using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Ping query to measure post-handshake network round-trip time (RTT)
        using var pingCmd = new SqlCommand("SELECT 1", conn) { CommandTimeout = 3 };
        var pingSw = Stopwatch.StartNew();
        await pingCmd.ExecuteScalarAsync(cancellationToken);
        pingSw.Stop();
        double latencyMs = pingSw.Elapsed.TotalMilliseconds;

        // Count non-system database objects to measure schema scale
        using var countCmd = new SqlCommand(
            "SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0 AND type NOT IN ('S', 'IT', 'X')",
            conn)
        {
            CommandTimeout = 3
        };
        var countResult = await countCmd.ExecuteScalarAsync(cancellationToken);
        int count = Convert.ToInt32(countResult);

        return new EndpointProbeResult(count, latencyMs, isLocal);
    }
    public async Task<SchemaComparisonResultModel> CompareAsync(
        DatabaseEndpoint source,
        DatabaseEndpoint target,
        ComparisonOptions options,
        IProgress<ComparisonProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        progress?.Report(new ComparisonProgress("Initializing comparison endpoints...", 10));

        return await Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new SchemaComparisonResultModel { IsCancelled = true, SourceEndpoint = source, TargetEndpoint = target };
            }

            try
            {
                var sourceEndpoint = source.ToDacCompareEndpoint();
                var targetEndpoint = target.ToDacCompareEndpoint();

                var comparison = new SchemaComparison(sourceEndpoint, targetEndpoint);
                options.ApplyTo(comparison.Options);

                progress?.Report(new ComparisonProgress("Comparing database schemas and analyzing dependencies...", 30));

                if (cancellationToken.IsCancellationRequested)
                {
                    return new SchemaComparisonResultModel { IsCancelled = true, SourceEndpoint = source, TargetEndpoint = target };
                }

                var dacResult = comparison.Compare(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return new SchemaComparisonResultModel { IsCancelled = true, SourceEndpoint = source, TargetEndpoint = target };
                }

                if (dacResult == null || !dacResult.IsValid)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return new SchemaComparisonResultModel { IsCancelled = true, SourceEndpoint = source, TargetEndpoint = target };
                    }

                    var errors = new List<string>();
                    try
                    {
                        if (dacResult != null)
                        {
                            var errList = dacResult.GetErrors();
                            if (errList != null)
                            {
                                foreach (var err in errList)
                                {
                                    if (err != null)
                                    {
                                        errors.Add(err.ToString() ?? "Unknown schema comparison error");
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    string errorDetails = errors.Any()
                        ? string.Join("; ", errors)
                        : "The schema comparison operation could not populate the database model. Ensure the database is accessible, resources are available, and query execution has not timed out.";

                    throw new InvalidOperationException(errorDetails);
                }

                progress?.Report(new ComparisonProgress("Processing differences and extracting object definitions...", 70));

                var diffItems = new List<SchemaDifferenceItem>();

                int identicalCount = 0;
                int addedCount = 0;
                int deletedCount = 0;
                int modifiedCount = 0;

                if (dacResult?.Differences != null)
                {
                    foreach (var diff in dacResult.Differences)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return new SchemaComparisonResultModel { IsCancelled = true, SourceEndpoint = source, TargetEndpoint = target };
                        }

                        var item = MapDacDifference(diff);
                        diffItems.Add(item);

                        switch (item.ChangeType)
                        {
                            case ChangeType.Added:
                                addedCount++;
                                break;
                            case ChangeType.Deleted:
                                deletedCount++;
                                break;
                            case ChangeType.Modified:
                                modifiedCount++;
                                break;
                            default:
                                identicalCount++;
                                break;
                        }
                    }
                }

                stopwatch.Stop();
                _isDacFxInitialized = true;
                progress?.Report(new ComparisonProgress("Comparison complete.", 100));

                var summary = new ComparisonSummary
                {
                    SourceDescription = source.ToString(),
                    TargetDescription = target.ToString(),
                    ComparisonTimestamp = DateTime.UtcNow,
                    ElapsedTime = stopwatch.Elapsed,
                    TotalObjectsCompared = diffItems.Count,
                    IdenticalCount = identicalCount,
                    AddedCount = addedCount,
                    DeletedCount = deletedCount,
                    ModifiedCount = modifiedCount
                };

                _isDacFxInitialized = true;

                return new SchemaComparisonResultModel
                {
                    Differences = diffItems,
                    Summary = summary,
                    Options = options,
                    SourceEndpoint = source,
                    TargetEndpoint = target,
                    RawDacResult = dacResult
                };
            }
            catch (OperationCanceledException)
            {
                return new SchemaComparisonResultModel { IsCancelled = true, SourceEndpoint = source, TargetEndpoint = target };
            }
        });
    }

    public Task<string> GenerateSynchronizationScriptAsync(
        SchemaComparisonResultModel resultModel,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (resultModel.RawDacResult == null)
            {
                return "-- No comparison result available to generate script.";
            }

            // Sync included state back to DacFx differences
            foreach (var diff in resultModel.RawDacResult.Differences)
            {
                var matchingItem = resultModel.Differences.FirstOrDefault(d => d.Id == diff.GetHashCode().ToString());
                if (matchingItem != null)
                {
                    if (matchingItem.IsSelected)
                    {
                        resultModel.RawDacResult.Include(diff);
                    }
                    else
                    {
                        resultModel.RawDacResult.Exclude(diff);
                    }
                }
            }

            string targetDbName = !string.IsNullOrWhiteSpace(resultModel.TargetEndpoint.Database)
                ? resultModel.TargetEndpoint.Database
                : "TargetDatabase";

            var scriptResult = resultModel.RawDacResult.GenerateScript(targetDbName);
            return scriptResult?.Script ?? "-- No script generated.";
        }, cancellationToken);
    }

    private static SchemaDifferenceItem MapDacDifference(SchemaDifference diff)
    {
        var item = new SchemaDifferenceItem
        {
            Id = diff.GetHashCode().ToString(),
            ChangeType = diff.UpdateAction switch
            {
                SchemaUpdateAction.Add => ChangeType.Added,
                SchemaUpdateAction.Delete => ChangeType.Deleted,
                SchemaUpdateAction.Change => ChangeType.Modified,
                _ => ChangeType.Identical
            },
            IsSelected = true
        };

        var primaryObject = diff.SourceObject ?? diff.TargetObject;
        if (primaryObject != null)
        {
            item.Category = MapObjectCategory(primaryObject.ObjectType?.Name);
            item.Name = FormatObjectName(primaryObject.Name);
            item.Schema = primaryObject.Name.Parts.Count > 1 ? primaryObject.Name.Parts[0] : "dbo";
            item.ObjectName = primaryObject.Name.Parts.Count > 0 ? primaryObject.Name.Parts[^1] : primaryObject.Name.ToString();
        }
        else
        {
            item.Name = diff.Name ?? "Unknown Object";
        }

        // Extract DDL script definitions
        try
        {
            if (diff.SourceObject != null)
            {
                item.SourceScript = diff.SourceObject.GetScript() ?? string.Empty;
            }
        }
        catch
        {
            item.SourceScript = $"-- Definition unavailable for {item.Name}";
        }

        try
        {
            if (diff.TargetObject != null)
            {
                item.TargetScript = diff.TargetObject.GetScript() ?? string.Empty;
            }
        }
        catch
        {
            item.TargetScript = $"-- Definition unavailable for {item.Name}";
        }

        // Map sub-differences (columns, constraints, etc.)
        if (diff.Children != null)
        {
            foreach (var child in diff.Children)
            {
                item.Children.Add(MapDacDifference(child));
            }
        }

        return item;
    }

    private static ObjectCategory MapObjectCategory(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return ObjectCategory.Other;

        if (typeName.Contains("Table", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Table;
        if (typeName.Contains("View", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.View;
        if (typeName.Contains("Procedure", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.StoredProcedure;
        if (typeName.Contains("Function", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Function;
        if (typeName.Contains("Trigger", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Trigger;
        if (typeName.Contains("Index", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Index;
        if (typeName.Contains("Constraint", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("PrimaryKey", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("ForeignKey", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Constraint;
        if (typeName.Contains("Type", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.UserDefinedType;
        if (typeName.Contains("Sequence", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Sequence;
        if (typeName.Contains("Synonym", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Synonym;
        if (typeName.Contains("Role", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("User", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Permission", StringComparison.OrdinalIgnoreCase)) return ObjectCategory.Security;

        return ObjectCategory.Other;
    }

    private static string FormatObjectName(ObjectIdentifier? identifier)
    {
        if (identifier == null) return "Unknown";
        if (identifier.Parts == null || identifier.Parts.Count == 0) return identifier.ToString();

        return string.Join(".", identifier.Parts.Select(p => $"[{p}]"));
    }
}
