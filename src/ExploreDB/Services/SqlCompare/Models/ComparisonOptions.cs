using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Compare;

namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Configurable options to control schema comparison behavior and filter rules.
/// </summary>
public class ComparisonOptions
{
    /// <summary>
    /// Ignore differences in whitespace and indentation within object definitions.
    /// </summary>
    public bool IgnoreWhitespace { get; set; } = true;

    /// <summary>
    /// Ignore SQL comments within object definitions.
    /// </summary>
    public bool IgnoreComments { get; set; } = false;

    /// <summary>
    /// Ignore differences in keyword casing (e.g. SELECT vs select).
    /// </summary>
    public bool IgnoreKeywordCasing { get; set; } = true;

    /// <summary>
    /// Ignore statement terminator semicolons (;) between T-SQL statements.
    /// </summary>
    public bool IgnoreSemicolonBetweenStatements { get; set; } = true;

    /// <summary>
    /// Ignore differences in system-generated constraint names (e.g. PK__Table__12345).
    /// </summary>
    public bool IgnoreSystemConstraintNames { get; set; } = true;

    /// <summary>
    /// Ignore differences in database/table/column collations.
    /// </summary>
    public bool IgnoreCollation { get; set; } = false;

    /// <summary>
    /// Ignore permissions and role assignments.
    /// </summary>
    public bool IgnorePermissions { get; set; } = true;

    /// <summary>
    /// Ignore filegroups and partition schemes.
    /// </summary>
    public bool IgnoreFilegroups { get; set; } = true;

    /// <summary>
    /// Ignore identity column seed and increment values.
    /// </summary>
    public bool IgnoreIdentitySeed { get; set; } = false;

    /// <summary>
    /// Ignore all index differences on tables and views.
    /// </summary>
    public bool IgnoreIndexes { get; set; } = false;

    /// <summary>
    /// Ignore index physical options (STATISTICS_NORECOMPUTE, etc.).
    /// </summary>
    public bool IgnoreIndexOptions { get; set; } = true;

    /// <summary>
    /// Ignore index fill factor values (e.g. FILLFACTOR = 80 vs 90).
    /// </summary>
    public bool IgnoreFillFactor { get; set; } = true;

    /// <summary>
    /// Ignore index padding (PAD_INDEX = ON/OFF).
    /// </summary>
    public bool IgnoreIndexPadding { get; set; } = true;

    /// <summary>
    /// Ignore index lock hints (ALLOW_ROW_LOCKS, ALLOW_PAGE_LOCKS).
    /// </summary>
    public bool IgnoreLockHintsOnIndexes { get; set; } = true;

    /// <summary>
    /// Ignore constraint differences (foreign keys, check constraints, default constraints).
    /// </summary>
    public bool IgnoreConstraints { get; set; } = false;

    /// <summary>
    /// Ignore table options (such as LOCK_ESCALATION, DATA_COMPRESSION, FILESTREAM).
    /// </summary>
    public bool IgnoreTableOptions { get; set; } = true;

    /// <summary>
    /// Ignore extended properties and documentation descriptions on objects.
    /// </summary>
    public bool IgnoreExtendedProperties { get; set; } = true;

    /// <summary>
    /// Ignore ANSI_NULLS settings saved when objects were created.
    /// </summary>
    public bool IgnoreAnsiNulls { get; set; } = true;

    /// <summary>
    /// Ignore QUOTED_IDENTIFIER settings saved when objects were created.
    /// </summary>
    public bool IgnoreQuotedIdentifiers { get; set; } = true;

    /// <summary>
    /// Ignore NOT FOR REPLICATION settings on identity columns and constraints.
    /// </summary>
    public bool IgnoreNotForReplication { get; set; } = true;

    /// <summary>
    /// Command execution timeout in seconds. 0 indicates no timeout (infinite),
    /// ensuring large enterprise databases with thousands of objects do not prematurely fail.
    /// </summary>
    public int CommandTimeout { get; set; } = 0;

    /// <summary>
    /// Applies these options to the underlying DacFx DacDeployOptions object.
    /// </summary>
    public void ApplyTo(DacDeployOptions dacOptions)
    {
        dacOptions.CommandTimeout = CommandTimeout;
        dacOptions.LongRunningCommandTimeout = CommandTimeout;
        dacOptions.IgnoreWhitespace = IgnoreWhitespace;
        dacOptions.IgnoreComments = IgnoreComments;
        dacOptions.IgnoreIdentitySeed = IgnoreIdentitySeed;
        dacOptions.IgnoreIncrement = IgnoreIdentitySeed;
        dacOptions.IgnorePermissions = IgnorePermissions;
        dacOptions.IgnoreRoleMembership = IgnorePermissions;
        dacOptions.IgnoreFilegroupPlacement = IgnoreFilegroups;
        dacOptions.IgnorePartitionSchemes = IgnoreFilegroups;
        dacOptions.IgnoreColumnCollation = IgnoreCollation;
        dacOptions.CompareUsingTargetCollation = IgnoreCollation;

        // Table options and extended properties
        dacOptions.IgnoreTableOptions = IgnoreTableOptions;
        dacOptions.IgnoreExtendedProperties = IgnoreExtendedProperties;
        if (IgnoreExtendedProperties)
        {
            dacOptions.DropExtendedPropertiesNotInSource = false;
        }

        // Configurable noise and formatting options
        dacOptions.IgnoreIndexOptions = IgnoreIndexOptions;
        dacOptions.IgnoreFillFactor = IgnoreFillFactor;
        dacOptions.IgnoreIndexPadding = IgnoreIndexPadding;
        dacOptions.IgnoreLockHintsOnIndexes = IgnoreLockHintsOnIndexes;
        dacOptions.IgnoreAnsiNulls = IgnoreAnsiNulls;
        dacOptions.IgnoreQuotedIdentifiers = IgnoreQuotedIdentifiers;
        dacOptions.IgnoreKeywordCasing = IgnoreKeywordCasing;
        dacOptions.IgnoreSemicolonBetweenStatements = IgnoreSemicolonBetweenStatements;
        dacOptions.IgnoreNotForReplication = IgnoreNotForReplication;

        // Index and constraint synchronization rules
        if (IgnoreIndexes)
        {
            dacOptions.DropIndexesNotInSource = false;
        }

        if (IgnoreConstraints)
        {
            dacOptions.DropConstraintsNotInSource = false;
            dacOptions.IgnoreWithNocheckOnCheckConstraints = true;
            dacOptions.IgnoreWithNocheckOnForeignKeys = true;
        }
    }
}
