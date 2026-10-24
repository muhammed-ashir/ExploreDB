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
        dacOptions.IgnorePermissions = IgnorePermissions;
        dacOptions.IgnoreRoleMembership = IgnorePermissions;
        dacOptions.IgnoreFilegroupPlacement = IgnoreFilegroups;
        dacOptions.IgnorePartitionSchemes = IgnoreFilegroups;
        dacOptions.IgnoreColumnCollation = IgnoreCollation;
        dacOptions.CompareUsingTargetCollation = IgnoreCollation;
    }
}
