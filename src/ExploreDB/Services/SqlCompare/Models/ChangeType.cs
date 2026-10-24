namespace ExploreDB.Services.SqlCompare.Models;

/// <summary>
/// Represents the type of difference detected for a database schema object.
/// </summary>
public enum ChangeType
{
    Identical = 0,
    Added = 1,      // Exists in Source, missing in Target (needs to be created in Target)
    Deleted = 2,    // Missing in Source, exists in Target (needs to be dropped from Target)
    Modified = 3    // Exists in both, but definitions differ (needs to be altered)
}
