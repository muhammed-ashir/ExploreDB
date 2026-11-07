using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ExploreDB.Services.Profiler.Analytics;

/// <summary>
/// High-performance SQL query normalizer that strips comments, masks literals with '?',
/// collapses whitespace, and calculates deterministic 64-bit query hash signatures.
/// </summary>
public static partial class QueryNormalizer
{
    // Regex for stripping multi-line and single-line comments
    private static readonly Regex BlockCommentsRegex = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex LineCommentsRegex = new(@"--[^\r\n]*", RegexOptions.Compiled);

    // Regex for strings: '...' (including escaped quotes '')
    private static readonly Regex StringLiteralsRegex = new(@"'(?:''|[^'])*'", RegexOptions.Compiled);

    // Regex for numeric literals (integers, decimals, hex)
    private static readonly Regex NumericLiteralsRegex = new(@"\b(?<!@|#|\$)(?:0x[0-9a-fA-F]+|\d+(?:\.\d+)?)\b", RegexOptions.Compiled);

    // Regex for IN clause lists e.g. IN (?, ?, ?) -> IN (?)
    private static readonly Regex InClauseListRegex = new(@"\bIN\s*\(\s*(?:\?\s*,\s*)*\?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex for whitespace collapsing
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a raw SQL query by replacing literals with '?' and standardizing whitespace.
    /// </summary>
    public static string Normalize(string? rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
            return string.Empty;

        // 1. Remove comments
        string result = BlockCommentsRegex.Replace(rawSql, " ");
        result = LineCommentsRegex.Replace(result, " ");

        // 2. Replace string literals with '?'
        result = StringLiteralsRegex.Replace(result, "'?'");

        // 3. Replace numeric/hex literals with ?
        result = NumericLiteralsRegex.Replace(result, "?");

        // 4. Collapse IN lists e.g. IN (?, ?, ?) -> IN (?)
        result = InClauseListRegex.Replace(result, "IN (?)");

        // 5. Normalize whitespace
        result = WhitespaceRegex.Replace(result, " ").Trim();

        return result;
    }

    /// <summary>
    /// Computes a fast, deterministic 64-bit FNV-1a hash formatted as a 16-character hex string.
    /// </summary>
    public static string ComputeHash(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "0000000000000000";

        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;

        ulong hash = fnvOffset;
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= fnvPrime;
        }

        return hash.ToString("X16");
    }
}
