using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ExploreDB.Services;

public class VirtualRelationshipEntry
{
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty;
    public string ToColumn { get; set; } = string.Empty;
    public bool IsNullable { get; set; } = false;
    public RelationshipType Type { get; set; } = RelationshipType.Inferred;
    public int ConfidenceScore { get; set; } = 100;
    public string? MatchReason { get; set; }
    public bool IsApproved { get; set; } = true;
    public bool IsBlacklisted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Key => $"{FromTable}|{FromColumn}|{ToTable}|{ToColumn}";
}

public class RelationshipVerificationResult
{
    public bool Success { get; set; }
    public bool HasOrphans { get; set; }
    public string Message { get; set; } = string.Empty;
    public int OrphanCount { get; set; } = 0;
}

public class RelationProfileMetadata
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public int EntryCount { get; set; }
}

public class LookupTableEntry
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int InDegree { get; set; }
    public List<string> ReferencingTables { get; set; } = new();
    public string DetectionReason { get; set; } = string.Empty;
    public bool IsAutoDetected { get; set; } = true;
}

public class LocalDatabaseRelationData
{
    public List<VirtualRelationshipEntry> Relationships { get; set; } = new();
    public List<string> NonRoutingTables { get; set; } = new();
    public List<string> DismissedLookupTables { get; set; } = new();
}

public class RelationProfile
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public DateTime? ExportedAt { get; set; }
    public List<VirtualRelationshipEntry> Relationships { get; set; } = new();
    public List<string> NonRoutingTables { get; set; } = new();
    public List<string> DismissedLookupTables { get; set; } = new();
}

public class RelationProfileImportSummary
{
    public string ProfileName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int TotalRelationships { get; set; }
    public int MatchingSchemaCount { get; set; }
    public int UnmatchedSchemaCount { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<VirtualRelationshipEntry> ImportedEntries { get; set; } = new();
    public List<string> ImportedNonRoutingTables { get; set; } = new();
    public List<string> ImportedDismissedLookupTables { get; set; } = new();
}

public class VirtualRelationshipService
{
    private readonly ConnectionService _connectionService;
    private readonly SchemaService _schemaService;

    // Entries for current active server + database
    private readonly List<VirtualRelationshipEntry> _entries = new();
    private readonly List<Relationship> _suggestedRelationships = new();

    // Non-routing lookup tables for current active context
    private readonly HashSet<string> _confirmedNonRoutingTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dismissedLookupTables = new(StringComparer.OrdinalIgnoreCase);
    private List<LookupTableEntry> _suggestedLookupTables = new();

    public event Action? OnVirtualRelationshipsChanged;
    public event Action? OnNonRoutingTablesChanged;

    public VirtualRelationshipService(ConnectionService connectionService, SchemaService schemaService)
    {
        _connectionService = connectionService;
        _schemaService = schemaService;

        _connectionService.OnConnectionChanged += HandleConnectionOrDatabaseChanged;
        _connectionService.OnDatabaseSwitched += HandleConnectionOrDatabaseChanged;
        _schemaService.OnTablesLoaded += HandleTablesLoaded;
    }

    private bool _hasScannedSuggestions = false;
    public bool HasScannedSuggestions => _hasScannedSuggestions;

    private void HandleConnectionOrDatabaseChanged()
    {
        _entries.Clear();
        _suggestedRelationships.Clear();
        _confirmedNonRoutingTables.Clear();
        _dismissedLookupTables.Clear();
        _suggestedLookupTables.Clear();
        _hasScannedSuggestions = false;
        LoadCurrentDatabaseEntries();
        DetectLookupTables();
        SyncActiveToSchema();
    }

    private void HandleTablesLoaded()
    {
        DetectLookupTables();
        SyncActiveToSchema();
    }

    public void EnsureSuggestionsScanned(bool forceRescan = false)
    {
        if (!_hasScannedSuggestions || forceRescan)
        {
            DetectAndSync();
        }
    }

    private bool _isScanningSuggestions = false;
    public bool IsScanningSuggestions => _isScanningSuggestions;
    public event Action? OnScanningStateChanged;

    public async Task EnsureSuggestionsScannedAsync(bool forceRescan = false)
    {
        if (_isScanningSuggestions) return;
        if (!_hasScannedSuggestions || forceRescan)
        {
            _isScanningSuggestions = true;
            OnScanningStateChanged?.Invoke();

            try
            {
                await Task.Run(async () =>
                {
                    DetectAndSync();
                    // Brief 250ms smoothing delay so the scan animation is perceived smoothly without flashing
                    await Task.Delay(250);
                });
            }
            finally
            {
                _isScanningSuggestions = false;
                OnScanningStateChanged?.Invoke();
            }
        }
    }

    private string? _activeProfileName;
    public string? ActiveProfileName => _activeProfileName;

    public static string ProfilesDirectoryPath => Path.Combine(AppEnvironmentService.AppDataDirectory, "RelationProfiles");
    public static string MappingsFilePath => Path.Combine(AppEnvironmentService.AppDataDirectory, "database_profile_mappings.json");

    public static string GetSafeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "UnnamedProfile" : clean.Trim();
    }

    public static string GetProfileFilePath(string profileName)
    {
        var fileName = $"{GetSafeFileName(profileName)}.json";
        return Path.Combine(ProfilesDirectoryPath, fileName);
    }

    private string? GetDatabaseMappingKey()
    {
        var server = _connectionService.ActiveServer;
        var database = _schemaService.ActiveDatabase;
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            return null;

        var safeServer = Regex.Replace(server, @"[^\w\.-]", "_");
        var safeDb = Regex.Replace(database, @"[^\w\.-]", "_");
        return $"{safeServer}|{safeDb}";
    }

    private Dictionary<string, string> LoadMappings()
    {
        var path = MappingsFilePath;
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveMappings(Dictionary<string, string> mappings)
    {
        try
        {
            var dir = Path.GetDirectoryName(MappingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(mappings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MappingsFilePath, json);
        }
        catch { }
    }

    private string GetStorageFilePath()
    {
        var server = _connectionService.ActiveServer;
        var database = _schemaService.ActiveDatabase;

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            return string.Empty;
        }

        var safeServer = Regex.Replace(server, @"[^\w\.-]", "_");
        var safeDb = Regex.Replace(database, @"[^\w\.-]", "_");
        var fileName = $"virtual_relations_{safeServer}_{safeDb}.json";

        return Path.Combine(AppEnvironmentService.AppDataDirectory, fileName);
    }

    public void LoadCurrentDatabaseEntries()
    {
        _entries.Clear();
        _confirmedNonRoutingTables.Clear();
        _dismissedLookupTables.Clear();
        _activeProfileName = null;

        var key = GetDatabaseMappingKey();
        if (key != null)
        {
            var mappings = LoadMappings();
            if (mappings.TryGetValue(key, out var profileName) && !string.IsNullOrWhiteSpace(profileName))
            {
                var profilePath = GetProfileFilePath(profileName);
                if (File.Exists(profilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(profilePath);
                        var profile = JsonSerializer.Deserialize<RelationProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (profile != null)
                        {
                            _activeProfileName = profile.Name;
                            if (profile.Relationships != null) _entries.AddRange(profile.Relationships);
                            if (profile.NonRoutingTables != null)
                            {
                                foreach (var t in profile.NonRoutingTables) _confirmedNonRoutingTables.Add(t);
                            }
                            if (profile.DismissedLookupTables != null)
                            {
                                foreach (var t in profile.DismissedLookupTables) _dismissedLookupTables.Add(t);
                            }
                            return;
                        }
                    }
                    catch { }
                }
            }
        }

        // Fallback to local database storage (backward compatible)
        var localFilePath = GetStorageFilePath();
        if (string.IsNullOrEmpty(localFilePath) || !File.Exists(localFilePath)) return;

        try
        {
            var json = File.ReadAllText(localFilePath);
            var trimmed = json.TrimStart();
            if (trimmed.StartsWith("["))
            {
                var loaded = JsonSerializer.Deserialize<List<VirtualRelationshipEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded != null)
                {
                    _entries.AddRange(loaded);
                }
            }
            else
            {
                var localData = JsonSerializer.Deserialize<LocalDatabaseRelationData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (localData != null)
                {
                    if (localData.Relationships != null) _entries.AddRange(localData.Relationships);
                    if (localData.NonRoutingTables != null)
                    {
                        foreach (var t in localData.NonRoutingTables) _confirmedNonRoutingTables.Add(t);
                    }
                    if (localData.DismissedLookupTables != null)
                    {
                        foreach (var t in localData.DismissedLookupTables) _dismissedLookupTables.Add(t);
                    }
                }
            }
        }
        catch { }
    }

    public void SaveCurrentDatabaseEntries()
    {
        if (!string.IsNullOrEmpty(_activeProfileName))
        {
            SaveActiveProfileToFile(_activeProfileName);
        }
        else
        {
            SaveLocalEntriesToFile();
        }
    }

    private void SaveLocalEntriesToFile()
    {
        var filePath = GetStorageFilePath();
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var localData = new LocalDatabaseRelationData
            {
                Relationships = new List<VirtualRelationshipEntry>(_entries),
                NonRoutingTables = _confirmedNonRoutingTables.ToList(),
                DismissedLookupTables = _dismissedLookupTables.ToList()
            };

            var json = JsonSerializer.Serialize(localData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch { }
    }

    private void SaveActiveProfileToFile(string profileName)
    {
        try
        {
            if (!Directory.Exists(ProfilesDirectoryPath))
            {
                Directory.CreateDirectory(ProfilesDirectoryPath);
            }

            var profilePath = GetProfileFilePath(profileName);
            RelationProfile profile;
            if (File.Exists(profilePath))
            {
                try
                {
                    var existingJson = File.ReadAllText(profilePath);
                    profile = JsonSerializer.Deserialize<RelationProfile>(existingJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RelationProfile();
                }
                catch
                {
                    profile = new RelationProfile();
                }
            }
            else
            {
                profile = new RelationProfile();
            }

            profile.Name = profileName;
            profile.LastModified = DateTime.UtcNow;
            profile.Relationships = new List<VirtualRelationshipEntry>(_entries);
            profile.NonRoutingTables = _confirmedNonRoutingTables.ToList();
            profile.DismissedLookupTables = _dismissedLookupTables.ToList();

            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(profilePath, json);
        }
        catch { }
    }

    public static string NormalizeFullTableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return name.Replace("[", "").Replace("]", "").Trim();
    }

    /// <summary>
    /// Runs the 4-rule bulletproof inference engine against loaded tables,
    /// merges approved virtual relationships into SchemaService, and populates suggested list.
    /// Columns that already participate in physical database foreign keys or active approved virtual keys
    /// are strictly excluded from candidate suggestions.
    /// </summary>
    public void DetectAndSync()
    {
        _hasScannedSuggestions = true;
        LoadCurrentDatabaseEntries();
        _suggestedRelationships.Clear();

        var tables = _schemaService.Tables;
        if (tables == null || tables.Count == 0) return;

        var blacklistedKeys = new HashSet<string>(
            _entries.Where(e => e.IsBlacklisted).Select(e => e.Key),
            StringComparer.OrdinalIgnoreCase);

        // Pre-index existing physical foreign keys by both full key and source column
        var existingPhysicalFkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingPhysicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
        {
            foreach (var fk in t.OutgoingKeys.Where(k => k.Type == RelationshipType.Physical))
            {
                var normFrom = NormalizeFullTableName(fk.FromTable);
                var normTo = NormalizeFullTableName(fk.ToTable);

                existingPhysicalFkColumns.Add($"{normFrom}|{fk.FromColumn}");
                if (normFrom.Contains('.'))
                {
                    var dot = normFrom.LastIndexOf('.');
                    existingPhysicalFkColumns.Add($"{normFrom.Substring(dot + 1)}|{fk.FromColumn}");
                }

                existingPhysicalKeys.Add($"{normFrom}|{fk.FromColumn}|{normTo}|{fk.ToColumn}");
                existingPhysicalKeys.Add($"{fk.FromTable}|{fk.FromColumn}|{fk.ToTable}|{fk.ToColumn}");
            }
        }

        // Pre-index active approved virtual relationship columns
        var activeVirtualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries.Where(e => e.IsApproved && !e.IsBlacklisted))
        {
            var normFrom = NormalizeFullTableName(e.FromTable);
            activeVirtualColumns.Add($"{normFrom}|{e.FromColumn}");
            if (normFrom.Contains('.'))
            {
                var dot = normFrom.LastIndexOf('.');
                activeVirtualColumns.Add($"{normFrom.Substring(dot + 1)}|{e.FromColumn}");
            }
        }

        // Rule 1 Anchor: Find all candidate target tables that have a single-column Primary Key or single-column Unique Key
        var targetCandidates = new List<(TableInfo Table, ColumnInfo TargetCol, bool IsPk)>();
        foreach (var t in tables)
        {
            var pkCols = t.Columns.Where(c => c.IsPrimaryKey).ToList();
            if (pkCols.Count == 1)
            {
                targetCandidates.Add((t, pkCols[0], true));
            }
            else
            {
                var uqCols = t.Columns.Where(c => c.IsUnique).ToList();
                if (uqCols.Count == 1)
                {
                    targetCandidates.Add((t, uqCols[0], false));
                }
            }
        }

        var detected = new List<Relationship>();

        // Pre-populate detected with all approved virtual relationships from storage (both Inferred and UserDefined)
        foreach (var approved in _entries.Where(e => e.IsApproved && !e.IsBlacklisted))
        {
            var normFrom = NormalizeFullTableName(approved.FromTable);
            var normTo = NormalizeFullTableName(approved.ToTable);
            var colKey = $"{normFrom}|{approved.FromColumn}";
            var colKeyShort = normFrom.Contains('.') ? $"{normFrom.Substring(normFrom.LastIndexOf('.') + 1)}|{approved.FromColumn}" : colKey;
            var fullKey = $"{normFrom}|{approved.FromColumn}|{normTo}|{approved.ToColumn}";

            // Safety guard: If this column physically has a database FK constraint now, do not activate virtual key
            if (existingPhysicalFkColumns.Contains(colKey) ||
                existingPhysicalFkColumns.Contains(colKeyShort) ||
                existingPhysicalKeys.Contains(fullKey) ||
                existingPhysicalKeys.Contains(approved.Key))
            {
                continue;
            }

            detected.Add(new Relationship
            {
                FromTable = approved.FromTable,
                FromColumn = approved.FromColumn,
                ToTable = approved.ToTable,
                ToColumn = approved.ToColumn,
                IsNullable = approved.IsNullable,
                Type = approved.Type,
                ConfidenceScore = approved.ConfidenceScore,
                MatchReason = approved.MatchReason ?? (approved.Type == RelationshipType.UserDefined ? "User Defined" : "Virtual")
            });
        }

        // Generic blacklist columns that must NEVER match across different tables unless entity name is prefixed
        var genericBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "code", "guid", "type", "status", "name", "desc", "description",
            "value", "date", "createddate", "createdby", "updateddate", "updatedby",
            "modifieddate", "modifiedby", "isdeleted", "isactive", "rowversion", "timestamp"
        };

        foreach (var childTable in tables)
        {
            var normChildTable = NormalizeFullTableName(childTable.FullName);

            foreach (var childCol in childTable.Columns)
            {
                var colName = childCol.Name.Trim();
                if (genericBlacklist.Contains(colName)) continue;

                var colKey = $"{normChildTable}|{colName}";
                var colKeyShort = $"{childTable.Name}|{colName}";

                // 1. Skip if column already participates in a physical database foreign key constraint
                if (childTable.OutgoingKeys.Any(k => k.Type == RelationshipType.Physical && string.Equals(k.FromColumn, colName, StringComparison.OrdinalIgnoreCase)) ||
                    existingPhysicalFkColumns.Contains(colKey) ||
                    existingPhysicalFkColumns.Contains(colKeyShort))
                {
                    continue;
                }

                // 2. Skip if column already has an active approved virtual relationship
                if (activeVirtualColumns.Contains(colKey) || activeVirtualColumns.Contains(colKeyShort))
                {
                    continue;
                }

                // Match against candidate target tables
                var matchesForColumn = new List<(TableInfo TargetTable, ColumnInfo TargetCol, int Confidence, string Reason)>();

                foreach (var (parentTable, parentCol, isPk) in targetCandidates)
                {
                    // Do not self-match unless role column (e.g. ParentId, ManagerId)
                    bool isSelf = string.Equals(childTable.FullName, parentTable.FullName, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(normChildTable, NormalizeFullTableName(parentTable.FullName), StringComparison.OrdinalIgnoreCase);

                    // A column can NEVER reference itself, and a table's primary key can NEVER be a foreign key to itself on the same table
                    if (isSelf && (childCol.IsPrimaryKey || string.Equals(childCol.Name, parentCol.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // Rule 2: Strict Data Type Match
                    if (!AreDataTypesCompatible(childCol.DataType, parentCol.DataType))
                    {
                        continue;
                    }

                    // Rule 3 & 4: Naming Pattern Engine
                    var (isMatch, confidence, reason) = MatchNamingPattern(
                        childTable.Name,
                        colName,
                        parentTable.Name,
                        parentCol.Name,
                        isSelf);

                    if (isMatch)
                    {
                        // Check schema affinity: same schema gets +5 bonus confidence
                        if (string.Equals(childTable.Schema, parentTable.Schema, StringComparison.OrdinalIgnoreCase))
                        {
                            confidence = Math.Min(100, confidence + 5);
                        }
                        else
                        {
                            confidence -= 5;
                        }

                        matchesForColumn.Add((parentTable, parentCol, confidence, reason));
                    }
                }

                if (matchesForColumn.Count == 0) continue;

                // If multiple candidate parent tables match the same child column (e.g. multi-schema ambiguity),
                // check if one has significantly higher confidence or same schema.
                var bestMatches = matchesForColumn.OrderByDescending(m => m.Confidence).ToList();
                var topMatch = bestMatches[0];

                // If ambiguous (multiple targets with identical high confidence across different tables)
                bool isAmbiguous = bestMatches.Count > 1 && bestMatches[1].Confidence >= topMatch.Confidence - 5;
                if (isAmbiguous)
                {
                    topMatch.Confidence = 70;
                    topMatch.Reason += " (Note: Multiple candidate tables matched; user verification recommended)";
                }

                // A column can NEVER reference itself on the same table
                if (string.Equals(childTable.FullName, topMatch.TargetTable.FullName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(childCol.Name, topMatch.TargetCol.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = $"{childTable.FullName}|{childCol.Name}|{topMatch.TargetTable.FullName}|{topMatch.TargetCol.Name}";

                // Skip if physical FK already exists
                if (existingPhysicalKeys.Contains(key)) continue;

                // Skip if blacklisted by user
                if (blacklistedKeys.Contains(key)) continue;

                var rel = new Relationship
                {
                    FromTable = childTable.FullName,
                    FromColumn = childCol.Name,
                    ToTable = topMatch.TargetTable.FullName,
                    ToColumn = topMatch.TargetCol.Name,
                    IsNullable = true,
                    Type = RelationshipType.Inferred,
                    ConfidenceScore = topMatch.Confidence,
                    MatchReason = topMatch.Reason
                };

                // Inferred candidate: queue for user review in suggested list (strict user control - never auto-approve)
                _suggestedRelationships.Add(rel);
            }
        }

        SaveCurrentDatabaseEntries();

        // Push all active virtual relationships to SchemaService
        _schemaService.ApplyVirtualRelationships(detected);
        OnVirtualRelationshipsChanged?.Invoke();
    }

    public void SyncActiveToSchema()
    {
        var tables = _schemaService.Tables;
        var existingPhysicalFkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tables != null)
        {
            foreach (var t in tables)
            {
                foreach (var fk in t.OutgoingKeys.Where(k => k.Type == RelationshipType.Physical))
                {
                    var normFrom = NormalizeFullTableName(fk.FromTable);
                    existingPhysicalFkColumns.Add($"{normFrom}|{fk.FromColumn}");
                    if (normFrom.Contains('.'))
                    {
                        var dot = normFrom.LastIndexOf('.');
                        existingPhysicalFkColumns.Add($"{normFrom.Substring(dot + 1)}|{fk.FromColumn}");
                    }
                }
            }
        }

        var activeList = _entries
            .Where(e => e.IsApproved && !e.IsBlacklisted)
            .Where(e =>
            {
                var normFrom = NormalizeFullTableName(e.FromTable);
                var colKey = $"{normFrom}|{e.FromColumn}";
                var colKeyShort = normFrom.Contains('.') ? $"{normFrom.Substring(normFrom.LastIndexOf('.') + 1)}|{e.FromColumn}" : colKey;
                return !existingPhysicalFkColumns.Contains(colKey) && !existingPhysicalFkColumns.Contains(colKeyShort);
            })
            .Select(e => new Relationship
            {
                FromTable = e.FromTable,
                FromColumn = e.FromColumn,
                ToTable = e.ToTable,
                ToColumn = e.ToColumn,
                IsNullable = e.IsNullable,
                Type = e.Type,
                ConfidenceScore = e.ConfidenceScore,
                MatchReason = e.MatchReason ?? "Virtual"
            })
            .ToList();

        _schemaService.ApplyVirtualRelationships(activeList);
        OnVirtualRelationshipsChanged?.Invoke();
    }

    private static (bool IsMatch, int Confidence, string Reason) MatchNamingPattern(
        string childTableName,
        string childColName,
        string parentTableName,
        string parentColName,
        bool isSelf)
    {
        var cleanChildTable = CleanObjectName(childTableName);
        var cleanParentTable = CleanObjectName(parentTableName);
        var singularParent = Singularize(cleanParentTable);

        var normChildCol = NormalizeIdentifier(childColName);
        var normParentCol = NormalizeIdentifier(parentColName);
        var normParentTable = NormalizeIdentifier(cleanParentTable);
        var normSingularParent = NormalizeIdentifier(singularParent);

        // Pattern 2: Self-referential hierarchies
        if (isSelf)
        {
            // A column can NEVER reference itself on the same table
            if (string.Equals(normChildCol, normParentCol, StringComparison.OrdinalIgnoreCase))
            {
                return (false, 0, string.Empty);
            }

            if (normChildCol is "parentid" or "parent_id" or "managerid" or "manager_id" or "reportsto" or "reportstoid")
            {
                return (true, 88, $"Self-referencing hierarchy column [{childColName}] referencing {parentTableName}.[{parentColName}]");
            }
            if (normChildCol.StartsWith("parent") && (normChildCol.EndsWith("id") || normChildCol.EndsWith(normParentCol)))
            {
                return (true, 85, $"Self-referencing hierarchy column [{childColName}] referencing {parentTableName}.[{parentColName}]");
            }
            return (false, 0, string.Empty);
        }

        // Pattern 1: Exact match with Parent PK (e.g. child has Customer_Id and parent PK is Customer_Id)
        // STRICT: Only across different tables!
        if (!string.Equals(normParentCol, "id", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normChildCol, normParentCol, StringComparison.OrdinalIgnoreCase))
        {
            return (true, 98, $"Column [{childColName}] directly matches primary key [{parentColName}] of {parentTableName}");
        }

        // Pattern 3: Singular parent + "id" (e.g. Customer_Id, CustomerId, customer_id, CUSTOMER_ID)
        var expectedSingularId = normSingularParent + "id";
        var expectedPluralId = normParentTable + "id";

        if (string.Equals(normChildCol, expectedSingularId, StringComparison.OrdinalIgnoreCase))
        {
            return (true, 95, $"Column [{childColName}] matches [{singularParent}Id] referencing {parentTableName}.[{parentColName}]");
        }

        // Pattern 4: Plural parent + "id" (e.g. Customers_Id, CustomersId)
        if (string.Equals(normChildCol, expectedPluralId, StringComparison.OrdinalIgnoreCase))
        {
            return (true, 90, $"Column [{childColName}] matches [{cleanParentTable}Id] referencing {parentTableName}.[{parentColName}]");
        }

        // Pattern 5: Role prefix + parent name + id (e.g. Billing_Customer_Id, Sender_User_Id, ShipTo_Customer_Id)
        if (normChildCol.EndsWith(expectedSingularId, StringComparison.OrdinalIgnoreCase) && normChildCol.Length > expectedSingularId.Length)
        {
            var prefix = childColName.Substring(0, childColName.Length - expectedSingularId.Length).TrimEnd('_', '-');
            return (true, 85, $"Role-prefixed foreign key [{childColName}] (Role: {prefix}) referencing {parentTableName}.[{parentColName}]");
        }

        // Pattern 6: Standard prefix like fk_CustomerId or fk_Customer
        if (normChildCol.StartsWith("fk") && (normChildCol.EndsWith(expectedSingularId) || normChildCol.EndsWith(normSingularParent)))
        {
            return (true, 92, $"Prefixed foreign key [{childColName}] referencing {parentTableName}.[{parentColName}]");
        }

        return (false, 0, string.Empty);
    }

    private static string CleanObjectName(string name)
    {
        var clean = name.Replace("[", "").Replace("]", "").Trim();
        var dot = clean.LastIndexOf('.');
        if (dot >= 0) clean = clean.Substring(dot + 1);
        // Strip common table prefixes
        clean = Regex.Replace(clean, @"^(?:tbl_|tbl|tab_|t_)", "", RegexOptions.IgnoreCase);
        return clean;
    }

    private static string NormalizeIdentifier(string name)
    {
        return name.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
    }

    private static string Singularize(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length <= 3) return word;

        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
        {
            return word.Substring(0, word.Length - 3) + "y";
        }
        if (word.EndsWith("sses", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
        {
            return word.Substring(0, word.Length - 2);
        }
        if (word.EndsWith("xes", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
        {
            return word.Substring(0, word.Length - 2);
        }
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("us", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("is", StringComparison.OrdinalIgnoreCase))
        {
            return word.Substring(0, word.Length - 1);
        }

        return word;
    }

    private static bool AreDataTypesCompatible(string childType, string parentType)
    {
        var c = NormalizeType(childType);
        var p = NormalizeType(parentType);
        return string.Equals(c, p, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeType(string dataType)
    {
        var t = dataType.ToLowerInvariant().Trim();
        var paren = t.IndexOf('(');
        if (paren > 0) t = t.Substring(0, paren);
        return t switch
        {
            "int" or "integer" => "int",
            "bigint" => "bigint",
            "smallint" => "smallint",
            "tinyint" => "tinyint",
            "uniqueidentifier" or "guid" => "uniqueidentifier",
            "varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext" => "string",
            _ => t
        };
    }

    /// <summary>
    /// Executes a safe, bounded referential integrity check against SQL Server with WITH (NOLOCK).
    /// </summary>
    public async Task<RelationshipVerificationResult> VerifyRelationshipAsync(Relationship rel, CancellationToken ct = default)
    {
        var connStr = _connectionService.ConnectionString;
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return new RelationshipVerificationResult
            {
                Success = false,
                Message = "No active database connection."
            };
        }

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            // Check if any non-null child keys exist that do not exist in the parent table
            var checkSql = $@"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
                SELECT TOP 1 1 
                FROM {rel.FromTable} c WITH (NOLOCK)
                WHERE c.[{rel.FromColumn}] IS NOT NULL 
                  AND NOT EXISTS (
                      SELECT 1 FROM {rel.ToTable} p WITH (NOLOCK) 
                      WHERE p.[{rel.ToColumn}] = c.[{rel.FromColumn}]
                  );";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = checkSql;
            cmd.CommandTimeout = 5; // Fast 5s timeout

            var result = await cmd.ExecuteScalarAsync(ct);
            if (result == null || result == DBNull.Value)
            {
                return new RelationshipVerificationResult
                {
                    Success = true,
                    HasOrphans = false,
                    Message = "100% verified! All foreign keys exist in parent table."
                };
            }
            else
            {
                return new RelationshipVerificationResult
                {
                    Success = true,
                    HasOrphans = true,
                    Message = "Orphan rows detected (some child IDs do not exist in the parent table). Consider using LEFT JOIN."
                };
            }
        }
        catch (Exception ex)
        {
            return new RelationshipVerificationResult
            {
                Success = false,
                Message = $"Verification error: {ex.Message}"
            };
        }
    }

    public IReadOnlyList<VirtualRelationshipEntry> GetActiveVirtualEntries()
    {
        return _entries.Where(e => e.IsApproved && !e.IsBlacklisted).ToList();
    }

    public IReadOnlyList<Relationship> GetSuggestedRelationships()
    {
        return _suggestedRelationships
            .OrderByDescending(r => r.ConfidenceScore)
            .ThenBy(r => r.FromTable)
            .ThenBy(r => r.FromColumn)
            .ToList();
    }

    public IReadOnlyList<VirtualRelationshipEntry> GetBlacklistedEntries()
    {
        return _entries.Where(e => e.IsBlacklisted).ToList();
    }

    public void AddCustomRelationship(Relationship rel)
    {
        rel.Type = RelationshipType.UserDefined;
        rel.ConfidenceScore = 100;
        rel.MatchReason = "User defined custom relationship";

        var key = $"{rel.FromTable}|{rel.FromColumn}|{rel.ToTable}|{rel.ToColumn}";
        _entries.RemoveAll(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        _entries.Add(new VirtualRelationshipEntry
        {
            FromTable = rel.FromTable,
            FromColumn = rel.FromColumn,
            ToTable = rel.ToTable,
            ToColumn = rel.ToColumn,
            IsNullable = rel.IsNullable,
            Type = RelationshipType.UserDefined,
            ConfidenceScore = 100,
            MatchReason = rel.MatchReason,
            IsApproved = true,
            IsBlacklisted = false
        });

        SaveCurrentDatabaseEntries();
        SyncActiveToSchema();
    }

    public void ApproveRelationship(Relationship rel)
    {
        var key = $"{rel.FromTable}|{rel.FromColumn}|{rel.ToTable}|{rel.ToColumn}";
        _suggestedRelationships.RemoveAll(r => string.Equals($"{r.FromTable}|{r.FromColumn}|{r.ToTable}|{r.ToColumn}", key, StringComparison.OrdinalIgnoreCase));

        var existing = _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.IsApproved = true;
            existing.IsBlacklisted = false;
        }
        else
        {
            _entries.Add(new VirtualRelationshipEntry
            {
                FromTable = rel.FromTable,
                FromColumn = rel.FromColumn,
                ToTable = rel.ToTable,
                ToColumn = rel.ToColumn,
                IsNullable = rel.IsNullable,
                Type = rel.Type,
                ConfidenceScore = rel.ConfidenceScore,
                MatchReason = rel.MatchReason,
                IsApproved = true,
                IsBlacklisted = false
            });
        }

        SaveCurrentDatabaseEntries();
        SyncActiveToSchema();
    }

    public void BlacklistRelationship(Relationship rel)
    {
        var key = $"{rel.FromTable}|{rel.FromColumn}|{rel.ToTable}|{rel.ToColumn}";
        _suggestedRelationships.RemoveAll(r => string.Equals($"{r.FromTable}|{r.FromColumn}|{r.ToTable}|{r.ToColumn}", key, StringComparison.OrdinalIgnoreCase));

        var existing = _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.IsApproved = false;
            existing.IsBlacklisted = true;
        }
        else
        {
            _entries.Add(new VirtualRelationshipEntry
            {
                FromTable = rel.FromTable,
                FromColumn = rel.FromColumn,
                ToTable = rel.ToTable,
                ToColumn = rel.ToColumn,
                IsNullable = rel.IsNullable,
                Type = rel.Type,
                ConfidenceScore = rel.ConfidenceScore,
                MatchReason = rel.MatchReason,
                IsApproved = false,
                IsBlacklisted = true
            });
        }

        SaveCurrentDatabaseEntries();
        SyncActiveToSchema();
    }

    public void DeleteRelationship(VirtualRelationshipEntry entry)
    {
        _entries.RemoveAll(e => string.Equals(e.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
        SaveCurrentDatabaseEntries();
        SyncActiveToSchema();
    }

    public void UnblacklistRelationship(VirtualRelationshipEntry entry)
    {
        entry.IsBlacklisted = false;
        entry.IsApproved = true;
        SaveCurrentDatabaseEntries();
        SyncActiveToSchema();
    }

    public List<RelationProfileMetadata> GetAvailableProfiles()
    {
        var list = new List<RelationProfileMetadata>();
        if (!Directory.Exists(ProfilesDirectoryPath)) return list;

        try
        {
            var files = Directory.GetFiles(ProfilesDirectoryPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<RelationProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                    {
                        list.Add(new RelationProfileMetadata
                        {
                            Name = profile.Name,
                            Description = profile.Description,
                            CreatedAt = profile.CreatedAt,
                            LastModified = profile.LastModified,
                            EntryCount = profile.Relationships?.Count ?? 0
                        });
                    }
                }
                catch { }
            }
        }
        catch { }

        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool SaveAsNewProfile(string profileName, string? description = null, bool linkCurrentDb = true)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return false;

        try
        {
            if (!Directory.Exists(ProfilesDirectoryPath))
            {
                Directory.CreateDirectory(ProfilesDirectoryPath);
            }

            var cleanName = profileName.Trim();
            var profile = new RelationProfile
            {
                Name = cleanName,
                Description = description?.Trim(),
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Relationships = new List<VirtualRelationshipEntry>(_entries),
                NonRoutingTables = _confirmedNonRoutingTables.ToList(),
                DismissedLookupTables = _dismissedLookupTables.ToList()
            };

            var filePath = GetProfileFilePath(cleanName);
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            if (linkCurrentDb)
            {
                var key = GetDatabaseMappingKey();
                if (key != null)
                {
                    var mappings = LoadMappings();
                    mappings[key] = cleanName;
                    SaveMappings(mappings);
                    _activeProfileName = cleanName;
                }
            }

            SyncActiveToSchema();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SwitchToProfile(string profileName)
    {
        var profilePath = GetProfileFilePath(profileName);
        if (!File.Exists(profilePath)) return;

        try
        {
            var json = File.ReadAllText(profilePath);
            var profile = JsonSerializer.Deserialize<RelationProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (profile == null) return;

            var key = GetDatabaseMappingKey();
            if (key != null)
            {
                var mappings = LoadMappings();
                mappings[key] = profile.Name;
                SaveMappings(mappings);
            }

            _activeProfileName = profile.Name;
            _entries.Clear();
            _confirmedNonRoutingTables.Clear();
            _dismissedLookupTables.Clear();

            if (profile.Relationships != null)
            {
                _entries.AddRange(profile.Relationships);
            }
            if (profile.NonRoutingTables != null)
            {
                foreach (var t in profile.NonRoutingTables) _confirmedNonRoutingTables.Add(t);
            }
            if (profile.DismissedLookupTables != null)
            {
                foreach (var t in profile.DismissedLookupTables) _dismissedLookupTables.Add(t);
            }

            _suggestedRelationships.Clear();
            DetectLookupTables();
            SyncActiveToSchema();
            OnNonRoutingTablesChanged?.Invoke();
        }
        catch { }
    }

    public void SwitchToLocal()
    {
        var key = GetDatabaseMappingKey();
        if (key != null)
        {
            var mappings = LoadMappings();
            mappings.Remove(key);
            SaveMappings(mappings);
        }

        _activeProfileName = null;
        LoadCurrentDatabaseEntries();
        _suggestedRelationships.Clear();
        DetectLookupTables();
        SyncActiveToSchema();
        OnNonRoutingTablesChanged?.Invoke();
    }

    public void DetachToLocal(bool keepCopy = false)
    {
        SwitchToLocal();
    }

    // ==========================================
    // Non-Routing Lookup Table Engine & Methods
    // ==========================================

    public IReadOnlySet<string> GetActiveNonRoutingTables()
    {
        return _confirmedNonRoutingTables;
    }

    public IReadOnlyList<LookupTableEntry> GetSuggestedLookupTables()
    {
        return _suggestedLookupTables;
    }

    public IReadOnlyList<string> GetDismissedLookupTables()
    {
        return _dismissedLookupTables.OrderBy(t => t).ToList();
    }

    public void DetectLookupTables()
    {
        _suggestedLookupTables.Clear();
    }

    public void ConfirmLookupTable(string tableName)
    {
        var norm = NormalizeFullTableName(tableName);
        var matchingTable = _schemaService.Tables?.FirstOrDefault(t => 
            string.Equals(t.FullName, tableName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(NormalizeFullTableName(t.FullName), norm, StringComparison.OrdinalIgnoreCase));

        var fullKey = matchingTable?.FullName ?? tableName;
        _confirmedNonRoutingTables.Add(fullKey);
        _dismissedLookupTables.Remove(fullKey);
        _suggestedLookupTables.RemoveAll(s => string.Equals(s.TableName, fullKey, StringComparison.OrdinalIgnoreCase) || string.Equals(NormalizeFullTableName(s.TableName), norm, StringComparison.OrdinalIgnoreCase));

        SaveCurrentDatabaseEntries();
        OnNonRoutingTablesChanged?.Invoke();
    }

    public void ConfirmAllLookupTables()
    {
        foreach (var item in _suggestedLookupTables.ToList())
        {
            _confirmedNonRoutingTables.Add(item.TableName);
        }
        _suggestedLookupTables.Clear();

        SaveCurrentDatabaseEntries();
        OnNonRoutingTablesChanged?.Invoke();
    }

    public void DismissLookupTable(string tableName)
    {
        var norm = NormalizeFullTableName(tableName);
        var matchingTable = _schemaService.Tables?.FirstOrDefault(t => 
            string.Equals(t.FullName, tableName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(NormalizeFullTableName(t.FullName), norm, StringComparison.OrdinalIgnoreCase));

        var fullKey = matchingTable?.FullName ?? tableName;
        _dismissedLookupTables.Add(fullKey);
        _confirmedNonRoutingTables.Remove(fullKey);
        _suggestedLookupTables.RemoveAll(s => string.Equals(s.TableName, fullKey, StringComparison.OrdinalIgnoreCase) || string.Equals(NormalizeFullTableName(s.TableName), norm, StringComparison.OrdinalIgnoreCase));

        SaveCurrentDatabaseEntries();
        OnNonRoutingTablesChanged?.Invoke();
    }

    public void AddCustomNonRoutingTable(string tableName)
    {
        var norm = NormalizeFullTableName(tableName);
        var matchingTable = _schemaService.Tables?.FirstOrDefault(t => 
            string.Equals(t.FullName, tableName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(NormalizeFullTableName(t.FullName), norm, StringComparison.OrdinalIgnoreCase));

        var fullKey = matchingTable?.FullName ?? tableName;
        _confirmedNonRoutingTables.Add(fullKey);
        _dismissedLookupTables.Remove(fullKey);
        _suggestedLookupTables.RemoveAll(s => string.Equals(s.TableName, fullKey, StringComparison.OrdinalIgnoreCase) || string.Equals(NormalizeFullTableName(s.TableName), norm, StringComparison.OrdinalIgnoreCase));

        SaveCurrentDatabaseEntries();
        OnNonRoutingTablesChanged?.Invoke();
    }

    public void RemoveNonRoutingTable(string tableName)
    {
        var norm = NormalizeFullTableName(tableName);
        _confirmedNonRoutingTables.RemoveWhere(t => 
            string.Equals(t, tableName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(NormalizeFullTableName(t), norm, StringComparison.OrdinalIgnoreCase));

        SaveCurrentDatabaseEntries();
        DetectLookupTables();
        OnNonRoutingTablesChanged?.Invoke();
    }

    public void RestoreDismissedLookupTable(string tableName)
    {
        var norm = NormalizeFullTableName(tableName);
        _dismissedLookupTables.RemoveWhere(t => 
            string.Equals(t, tableName, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(NormalizeFullTableName(t), norm, StringComparison.OrdinalIgnoreCase));

        SaveCurrentDatabaseEntries();
        DetectLookupTables();
        OnNonRoutingTablesChanged?.Invoke();
    }

    public void DeleteProfile(string profileName)
    {
        var profilePath = GetProfileFilePath(profileName);
        if (File.Exists(profilePath))
        {
            try { File.Delete(profilePath); } catch { }
        }

        var mappings = LoadMappings();
        var toRemove = mappings.Where(kvp => string.Equals(kvp.Value, profileName, StringComparison.OrdinalIgnoreCase)).Select(kvp => kvp.Key).ToList();
        if (toRemove.Count > 0)
        {
            foreach (var k in toRemove) mappings.Remove(k);
            SaveMappings(mappings);
        }

        if (string.Equals(_activeProfileName, profileName, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfileName = null;
            LoadCurrentDatabaseEntries();
            SyncActiveToSchema();
        }
        else
        {
            OnVirtualRelationshipsChanged?.Invoke();
        }
    }

    public string ExportProfileJson(string? profileName = null)
    {
        RelationProfile exportModel;

        if (string.IsNullOrEmpty(profileName) || string.Equals(profileName, _activeProfileName, StringComparison.OrdinalIgnoreCase))
        {
            exportModel = new RelationProfile
            {
                Name = _activeProfileName ?? $"Local_{_schemaService.ActiveDatabase}",
                Description = _activeProfileName != null ? "Exported Relation Profile from ExploreDB" : $"Local relationships for {_schemaService.ActiveDatabase}",
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                ExportedAt = DateTime.UtcNow,
                Version = 1,
                Relationships = new List<VirtualRelationshipEntry>(_entries),
                NonRoutingTables = _confirmedNonRoutingTables.ToList(),
                DismissedLookupTables = _dismissedLookupTables.ToList()
            };
        }
        else
        {
            var profilePath = GetProfileFilePath(profileName);
            if (File.Exists(profilePath))
            {
                try
                {
                    var json = File.ReadAllText(profilePath);
                    exportModel = JsonSerializer.Deserialize<RelationProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RelationProfile { Name = profileName };
                }
                catch
                {
                    exportModel = new RelationProfile { Name = profileName };
                }
            }
            else
            {
                exportModel = new RelationProfile { Name = profileName };
            }
            exportModel.ExportedAt = DateTime.UtcNow;
        }

        return JsonSerializer.Serialize(exportModel, new JsonSerializerOptions { WriteIndented = true });
    }

    public RelationProfileImportSummary AnalyzeImportJson(string jsonContent)
    {
        var summary = new RelationProfileImportSummary();
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            summary.Warnings.Add("File is empty.");
            return summary;
        }

        List<VirtualRelationshipEntry>? entries = null;
        string profileName = "Imported Profile";
        string? description = null;
        List<string> nonRouting = new();
        List<string> dismissed = new();

        try
        {
            var profile = JsonSerializer.Deserialize<RelationProfile>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (profile?.Relationships != null && profile.Relationships.Count > 0)
            {
                entries = profile.Relationships;
                if (!string.IsNullOrWhiteSpace(profile.Name)) profileName = profile.Name;
                description = profile.Description;
                if (profile.NonRoutingTables != null) nonRouting = profile.NonRoutingTables;
                if (profile.DismissedLookupTables != null) dismissed = profile.DismissedLookupTables;
            }
        }
        catch { }

        if (entries == null)
        {
            try
            {
                entries = JsonSerializer.Deserialize<List<VirtualRelationshipEntry>>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }
        }

        if (entries == null || entries.Count == 0)
        {
            summary.Warnings.Add("Could not recognize valid relationship entries in this .exploredb file.");
            return summary;
        }

        summary.ProfileName = profileName;
        summary.Description = description;
        summary.TotalRelationships = entries.Count;
        summary.ImportedEntries = entries;
        summary.ImportedNonRoutingTables = nonRouting;
        summary.ImportedDismissedLookupTables = dismissed;

        var loadedTables = new HashSet<string>(_schemaService.Tables.Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);

        int matching = 0;
        int unmatched = 0;
        var unmatchedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            bool fromOk = loadedTables.Contains(e.FromTable);
            bool toOk = loadedTables.Contains(e.ToTable);
            if (fromOk && toOk)
            {
                matching++;
            }
            else
            {
                unmatched++;
                if (!fromOk) unmatchedTables.Add(e.FromTable);
                if (!toOk) unmatchedTables.Add(e.ToTable);
            }
        }

        summary.MatchingSchemaCount = matching;
        summary.UnmatchedSchemaCount = unmatched;

        if (unmatched > 0)
        {
            summary.Warnings.Add($"{unmatched} relationship(s) reference tables not in the current database ({string.Join(", ", unmatchedTables.Take(5))}{(unmatchedTables.Count > 5 ? "..." : "")}). They will still be preserved in the profile for other databases.");
        }

        return summary;
    }

    public void ExecuteImport(RelationProfileImportSummary summary, string targetProfileName, bool asNewProfile, bool linkToCurrentDb)
    {
        if (summary.ImportedEntries == null || summary.ImportedEntries.Count == 0) return;

        if (asNewProfile)
        {
            var cleanName = string.IsNullOrWhiteSpace(targetProfileName) ? summary.ProfileName : targetProfileName.Trim();
            var profile = new RelationProfile
            {
                Name = cleanName,
                Description = summary.Description,
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Relationships = new List<VirtualRelationshipEntry>(summary.ImportedEntries),
                NonRoutingTables = summary.ImportedNonRoutingTables?.ToList() ?? new(),
                DismissedLookupTables = summary.ImportedDismissedLookupTables?.ToList() ?? new()
            };

            if (!Directory.Exists(ProfilesDirectoryPath))
            {
                Directory.CreateDirectory(ProfilesDirectoryPath);
            }

            var filePath = GetProfileFilePath(cleanName);
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            if (linkToCurrentDb)
            {
                var key = GetDatabaseMappingKey();
                if (key != null)
                {
                    var mappings = LoadMappings();
                    mappings[key] = cleanName;
                    SaveMappings(mappings);
                    _activeProfileName = cleanName;
                }
                _entries.Clear();
                _entries.AddRange(summary.ImportedEntries);

                _confirmedNonRoutingTables.Clear();
                if (summary.ImportedNonRoutingTables != null)
                {
                    foreach (var t in summary.ImportedNonRoutingTables) _confirmedNonRoutingTables.Add(t);
                }
                _dismissedLookupTables.Clear();
                if (summary.ImportedDismissedLookupTables != null)
                {
                    foreach (var t in summary.ImportedDismissedLookupTables) _dismissedLookupTables.Add(t);
                }

                DetectLookupTables();
                SyncActiveToSchema();
                OnNonRoutingTablesChanged?.Invoke();
            }
            else
            {
                OnVirtualRelationshipsChanged?.Invoke();
            }
        }
        else
        {
            var existingKeys = new HashSet<string>(_entries.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in summary.ImportedEntries)
            {
                if (!existingKeys.Contains(entry.Key))
                {
                    _entries.Add(entry);
                    existingKeys.Add(entry.Key);
                }
            }

            if (summary.ImportedNonRoutingTables != null)
            {
                foreach (var t in summary.ImportedNonRoutingTables)
                {
                    _confirmedNonRoutingTables.Add(t);
                }
            }

            SaveCurrentDatabaseEntries();
            DetectLookupTables();
            SyncActiveToSchema();
            OnNonRoutingTablesChanged?.Invoke();
        }
    }
}
