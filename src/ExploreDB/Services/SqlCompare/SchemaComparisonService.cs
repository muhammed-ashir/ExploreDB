namespace ExploreDB.Services.SqlCompare;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExploreDB.Services.SqlCompare.Models;
using Microsoft.SqlServer.Dac.Compare;
using Microsoft.SqlServer.Dac.Model;

public interface ISchemaComparisonService
{
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
            cancellationToken.ThrowIfCancellationRequested();

            var sourceEndpoint = source.ToDacCompareEndpoint();
            var targetEndpoint = target.ToDacCompareEndpoint();

            var comparison = new SchemaComparison(sourceEndpoint, targetEndpoint);
            options.ApplyTo(comparison.Options);

            progress?.Report(new ComparisonProgress("Comparing database schemas and analyzing dependencies...", 30));

            cancellationToken.ThrowIfCancellationRequested();

            var dacResult = comparison.Compare();

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
                    cancellationToken.ThrowIfCancellationRequested();

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

            return new SchemaComparisonResultModel
            {
                Differences = diffItems,
                Summary = summary,
                Options = options,
                SourceEndpoint = source,
                TargetEndpoint = target,
                RawDacResult = dacResult
            };
        }, cancellationToken);
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
