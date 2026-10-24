namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Categorization of SQL Server database objects.
/// </summary>
public enum ObjectCategory
{
    Table = 0,
    View = 1,
    StoredProcedure = 2,
    Function = 3,
    Trigger = 4,
    UserDefinedType = 5,
    Sequence = 6,
    Synonym = 7,
    Constraint = 8,
    Index = 9,
    Security = 10,
    Other = 99
}
