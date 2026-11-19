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

public class RelationProfile
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public DateTime? ExportedAt { get; set; }
    public List<VirtualRelationshipEntry> Relationships { get; set; } = new();
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
}

public class VirtualRelationshipService
{
    private readonly ConnectionService _connectionService;
    private readonly SchemaService _schemaService;

    // Entries for current active server + database
    private readonly List<VirtualRelationshipEntry> _entries = new();
    private readonly List<Relationship> _suggestedRelationships = new();

    public event Action? OnVirtualRelationshipsChanged;

    public VirtualRelationshipService(ConnectionService connectionService, SchemaService schemaService)
    {
        _connectionService = connectionService;
        _schemaService = schemaService;

        _connectionService.OnConnectionChanged += HandleConnectionOrDatabaseChanged;
        _connectionService.OnDatabaseSwitched += HandleConnectionOrDatabaseChanged;
        _schemaService.OnTablesLoaded += HandleTablesLoaded;
    }

    private void HandleConnectionOrDatabaseChanged()
    {
        _entries.Clear();
        _suggestedRelationships.Clear();
        LoadCurrentDatabaseEntries();
    }

    private void HandleTablesLoaded()
    {
        if (_schemaService.Tables.Count > 0)
        {
            DetectAndSync();
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
                        if (profile?.Relationships != null)
                        {
                            _activeProfileName = profile.Name;
                            _entries.AddRange(profile.Relationships);
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
            var loaded = JsonSerializer.Deserialize<List<VirtualRelationshipEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (loaded != null)
            {
                _entries.AddRange(loaded);
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

            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
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

            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(profilePath, json);
        }
        catch { }
    }

    /// <summary>
    /// Runs the 4-rule bulletproof inference engine against loaded tables,
    /// merges approved virtual relationships into SchemaService, and populates suggested list.
    /// </summary>
    public void DetectAndSync()
    {
        LoadCurrentDatabaseEntries();
        _suggestedRelationships.Clear();

        var tables = _schemaService.Tables;
        if (tables == null || tables.Count == 0) return;

        var blacklistedKeys = new HashSet<string>(
            _entries.Where(e => e.IsBlacklisted).Select(e => e.Key),
            StringComparer.OrdinalIgnoreCase);

        // Pre-index existing physical foreign keys
        var existingPhysicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
        {
            foreach (var fk in t.OutgoingKeys.Where(k => k.Type == RelationshipType.Physical))
            {
                existingPhysicalKeys.Add($"{fk.FromTable}|{fk.FromColumn}|{fk.ToTable}|{fk.ToColumn}");
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

        // Generic blacklist columns that must NEVER match across different tables unless entity name is prefixed
        var genericBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "code", "guid", "type", "status", "name", "desc", "description",
            "value", "date", "createddate", "createdby", "updateddate", "updatedby",
            "modifieddate", "modifiedby", "isdeleted", "isactive", "rowversion", "timestamp"
        };

        foreach (var childTable in tables)
        {
            foreach (var childCol in childTable.Columns)
            {
                var colName = childCol.Name.Trim();
                if (genericBlacklist.Contains(colName)) continue;

                // Match against candidate target tables
                var matchesForColumn = new List<(TableInfo TargetTable, ColumnInfo TargetCol, int Confidence, string Reason)>();

                foreach (var (parentTable, parentCol, isPk) in targetCandidates)
                {
                    // Do not self-match unless role column (e.g. ParentId, ManagerId)
                    bool isSelf = string.Equals(childTable.FullName, parentTable.FullName, StringComparison.OrdinalIgnoreCase);

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

                // Check if this was already recorded in persistent entries
                var existingEntry = _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
                if (existingEntry != null)
                {
                    if (existingEntry.IsApproved && !existingEntry.IsBlacklisted)
                    {
                        rel.Type = existingEntry.Type;
                        rel.ConfidenceScore = existingEntry.ConfidenceScore;
                        rel.MatchReason = existingEntry.MatchReason ?? rel.MatchReason;
                        detected.Add(rel);
                    }
                }
                else
                {
                    // New inferred candidate
                    if (topMatch.Confidence >= 85 && !isAmbiguous)
                    {
                        // High confidence: automatically eligible and recorded as approved inferred
                        _entries.Add(new VirtualRelationshipEntry
                        {
                            FromTable = rel.FromTable,
                            FromColumn = rel.FromColumn,
                            ToTable = rel.ToTable,
                            ToColumn = rel.ToColumn,
                            IsNullable = rel.IsNullable,
                            Type = RelationshipType.Inferred,
                            ConfidenceScore = rel.ConfidenceScore,
                            MatchReason = rel.MatchReason,
                            IsApproved = true,
                            IsBlacklisted = false
                        });
                        detected.Add(rel);
                    }
                    else
                    {
                        // Moderate confidence or ambiguous: queue for manual review in suggested list
                        _suggestedRelationships.Add(rel);
                    }
                }
            }
        }

        // Add user-defined relationships from persistent storage
        foreach (var userDef in _entries.Where(e => e.Type == RelationshipType.UserDefined && e.IsApproved && !e.IsBlacklisted))
        {
            var key = userDef.Key;
            if (!existingPhysicalKeys.Contains(key) && !detected.Any(d => string.Equals(d.FromTable + "|" + d.FromColumn + "|" + d.ToTable + "|" + d.ToColumn, key, StringComparison.OrdinalIgnoreCase)))
            {
                detected.Add(new Relationship
                {
                    FromTable = userDef.FromTable,
                    FromColumn = userDef.FromColumn,
                    ToTable = userDef.ToTable,
                    ToColumn = userDef.ToColumn,
                    IsNullable = userDef.IsNullable,
                    Type = RelationshipType.UserDefined,
                    ConfidenceScore = userDef.ConfidenceScore,
                    MatchReason = userDef.MatchReason ?? "User Defined"
                });
            }
        }

        SaveCurrentDatabaseEntries();

        // Push all active virtual relationships to SchemaService
        _schemaService.ApplyVirtualRelationships(detected);
        OnVirtualRelationshipsChanged?.Invoke();
    }

    public void SyncActiveToSchema()
    {
        var activeList = _entries
            .Where(e => e.IsApproved && !e.IsBlacklisted)
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

        // Pattern 1: Exact match with Parent PK (e.g. child has Customer_Id and parent PK is Customer_Id)
        if (!string.Equals(normParentCol, "id", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normChildCol, normParentCol, StringComparison.OrdinalIgnoreCase))
        {
            return (true, 98, $"Column [{childColName}] directly matches primary key [{parentColName}] of {parentTableName}");
        }

        // Pattern 2: Self-referential hierarchies
        if (isSelf)
        {
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
        return _suggestedRelationships;
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
                Relationships = new List<VirtualRelationshipEntry>(_entries)
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
            if (profile.Relationships != null)
            {
                _entries.AddRange(profile.Relationships);
            }

            _suggestedRelationships.Clear();
            SyncActiveToSchema();
        }
        catch { }
    }

    public void DetachToLocal(bool keepCopy)
    {
        var key = GetDatabaseMappingKey();
        if (key != null)
        {
            var mappings = LoadMappings();
            mappings.Remove(key);
            SaveMappings(mappings);
        }

        _activeProfileName = null;

        if (keepCopy)
        {
            SaveLocalEntriesToFile();
        }
        else
        {
            _entries.Clear();
            var localPath = GetStorageFilePath();
            if (File.Exists(localPath))
            {
                try
                {
                    var json = File.ReadAllText(localPath);
                    var loaded = JsonSerializer.Deserialize<List<VirtualRelationshipEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (loaded != null) _entries.AddRange(loaded);
                }
                catch { }
            }
            else
            {
                DetectAndSync();
                return;
            }
        }

        SyncActiveToSchema();
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
                Relationships = new List<VirtualRelationshipEntry>(_entries)
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

        try
        {
            var profile = JsonSerializer.Deserialize<RelationProfile>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (profile?.Relationships != null && profile.Relationships.Count > 0)
            {
                entries = profile.Relationships;
                if (!string.IsNullOrWhiteSpace(profile.Name)) profileName = profile.Name;
                description = profile.Description;
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
            summary.Warnings.Add("Could not recognize valid relationship entries in this JSON file.");
            return summary;
        }

        summary.ProfileName = profileName;
        summary.Description = description;
        summary.TotalRelationships = entries.Count;
        summary.ImportedEntries = entries;

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
                Relationships = new List<VirtualRelationshipEntry>(summary.ImportedEntries)
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
                SyncActiveToSchema();
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

            SaveCurrentDatabaseEntries();
            SyncActiveToSchema();
        }
    }
}
