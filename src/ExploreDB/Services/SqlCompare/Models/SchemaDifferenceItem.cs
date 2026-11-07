using System;
using System.Collections.Generic;

namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Represents a compared database schema object with its difference state and DDL definitions.
/// </summary>
public class SchemaDifferenceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Formatted name of the object, e.g. [dbo].[Customers]
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Schema name (e.g. dbo, sales)
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// Raw unqualified name of the object (e.g. Customers)
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Categorization (Table, View, StoredProcedure, etc.)
    /// </summary>
    public ObjectCategory Category { get; set; } = ObjectCategory.Other;

    /// <summary>
    /// Difference type relative to target (Added, Deleted, Modified, Identical).
    /// </summary>
    public ChangeType ChangeType { get; set; } = ChangeType.Identical;

    /// <summary>
    /// Full T-SQL DDL script from the Source database/project.
    /// </summary>
    public string SourceScript { get; set; } = string.Empty;

    /// <summary>
    /// Full T-SQL DDL script from the Target database/project.
    /// </summary>
    public string TargetScript { get; set; } = string.Empty;

    /// <summary>
    /// Whether this object is checked for inclusion in synchronization scripts.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// Sub-differences (e.g. specific columns, indexes, constraints on a table).
    /// </summary>
    public List<SchemaDifferenceItem> Children { get; set; } = [];

    /// <summary>
    /// Convenience property to check if there is an actual difference.
    /// </summary>
    public bool HasDifference => ChangeType != ChangeType.Identical;

    /// <summary>
    /// Friendly category label for grouping (e.g. "Tables", "Views", "Stored Procedures").
    /// </summary>
    public string CategoryDisplayName => Category switch
    {
        ObjectCategory.Table => "Tables",
        ObjectCategory.View => "Views",
        ObjectCategory.StoredProcedure => "Stored Procedures",
        ObjectCategory.Function => "Functions",
        ObjectCategory.Trigger => "Triggers",
        ObjectCategory.UserDefinedType => "User-Defined Types",
        ObjectCategory.Sequence => "Sequences",
        ObjectCategory.Synonym => "Synonyms",
        ObjectCategory.Constraint => "Constraints",
        ObjectCategory.Index => "Indexes",
        ObjectCategory.Security => "Security & Roles",
        _ => "Other Objects"
    };

    /// <summary>
    /// Bootstrap Icon class for UI display matching ExploreDB design.
    /// </summary>
    public string BootstrapIcon => Category switch
    {
        ObjectCategory.Table => "bi-table",
        ObjectCategory.View => "bi-eye",
        ObjectCategory.StoredProcedure => "bi-file-earmark-code",
        ObjectCategory.Function => "bi-calculator",
        ObjectCategory.Trigger => "bi-lightning-charge",
        ObjectCategory.Index => "bi-sort-alpha-down",
        ObjectCategory.Constraint => "bi-key",
        ObjectCategory.UserDefinedType => "bi-braces",
        ObjectCategory.Sequence => "bi-123",
        ObjectCategory.Synonym => "bi-link-45deg",
        ObjectCategory.Security => "bi-shield-lock",
        _ => "bi-file-earmark"
    };
}
