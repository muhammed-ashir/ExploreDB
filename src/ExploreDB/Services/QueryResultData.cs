using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Data.SqlClient;

namespace ExploreDB.Services;

public enum ColumnDisplayKind
{
    Default = 0,
    Date = 1,
    DateTime = 2,
    SmallDateTime = 3,
    DateTime2 = 4,
    Time = 5,
    DateTimeOffset = 6,
    Binary = 7
}

public sealed record ColumnAggregateResult(
    int Count,
    int DistinctCount,
    double? Sum,
    double? Average,
    object? Min,
    object? Max);

/// <summary>
/// High-performance, lock-free chunked columnar store capable of holding 5,000,000+ rows
/// with minimal RAM overhead (unboxed primitive arrays + null bitmasks, zero per-row object arrays).
/// </summary>
public class QueryResultTable : IEnumerable<object>
{
    internal const int ChunkShift = 12;
    internal const int ChunkSize = 1 << ChunkShift; // 4096 rows per chunk (always SOH, never LOH)
    internal const int ChunkMask = ChunkSize - 1;

    private readonly List<string> _columns = new();
    private readonly List<string> _columnTypes = new();
    private readonly Dictionary<string, int> _columnIndexMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ColumnStorage[] _columnStorages;
    private readonly ColumnDisplayKind[] _displayKinds;
    private readonly bool[] _canBeJsonOrXml;
    private readonly bool[] _isBinaryColumn;
    private readonly object _writerLock = new();

    private int _writerRowCount = 0;
    private int _committedRowCount = 0;
    private bool _isComplete = false;

    public event Action? OnRowsUpdated;

    public IReadOnlyList<string> Columns => _columns;
    public IReadOnlyList<string> ColumnTypes => _columnTypes;

    public int RowCount => Volatile.Read(ref _committedRowCount);

    public bool IsComplete
    {
        get => Volatile.Read(ref _isComplete);
        set => Volatile.Write(ref _isComplete, value);
    }

    public string? TableName { get; set; }

    public QueryResultTable(IEnumerable<string> columns, IEnumerable<string>? columnTypes = null, Type?[]? fieldTypes = null)
    {
        if (columnTypes != null) _columnTypes.AddRange(columnTypes);
        int idx = 0;
        foreach (var col in columns)
        {
            var colName = string.IsNullOrEmpty(col) ? $"Column{idx}" : col;
            _columns.Add(colName);
            if (!_columnIndexMap.ContainsKey(colName))
            {
                _columnIndexMap[colName] = idx;
            }
            idx++;
        }

        int colCount = _columns.Count;
        _columnStorages = new ColumnStorage[colCount];
        _displayKinds = new ColumnDisplayKind[colCount];
        _canBeJsonOrXml = new bool[colCount];
        _isBinaryColumn = new bool[colCount];

        for (int c = 0; c < colCount; c++)
        {
            string? sqlType = c < _columnTypes.Count ? _columnTypes[c] : null;
            Type? clrType = (fieldTypes != null && c < fieldTypes.Length) ? fieldTypes[c] : null;

            _displayKinds[c] = ResolveDisplayKind(sqlType, clrType);
            _columnStorages[c] = ColumnStorage.Create(clrType);
            _canBeJsonOrXml[c] = clrType == null || clrType == typeof(string) || clrType == typeof(object);
            _isBinaryColumn[c] = clrType == null || clrType == typeof(byte[]) || clrType == typeof(object);
        }
    }

    private static ColumnDisplayKind ResolveDisplayKind(string? sqlType, Type? clrType)
    {
        if (!string.IsNullOrEmpty(sqlType))
        {
            if (sqlType.Equals("date", StringComparison.OrdinalIgnoreCase)) return ColumnDisplayKind.Date;
            if (sqlType.Equals("datetime", StringComparison.OrdinalIgnoreCase)) return ColumnDisplayKind.DateTime;
            if (sqlType.Equals("smalldatetime", StringComparison.OrdinalIgnoreCase)) return ColumnDisplayKind.SmallDateTime;
            if (sqlType.Equals("datetime2", StringComparison.OrdinalIgnoreCase)) return ColumnDisplayKind.DateTime2;
            if (sqlType.Equals("time", StringComparison.OrdinalIgnoreCase)) return ColumnDisplayKind.Time;
            if (sqlType.Equals("datetimeoffset", StringComparison.OrdinalIgnoreCase)) return ColumnDisplayKind.DateTimeOffset;
            if (sqlType.Equals("binary", StringComparison.OrdinalIgnoreCase) ||
                sqlType.Equals("varbinary", StringComparison.OrdinalIgnoreCase) ||
                sqlType.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                sqlType.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                sqlType.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
            {
                return ColumnDisplayKind.Binary;
            }
        }

        if (clrType == typeof(byte[])) return ColumnDisplayKind.Binary;
        if (clrType == typeof(DateTimeOffset)) return ColumnDisplayKind.DateTimeOffset;
        return ColumnDisplayKind.Default;
    }

    public ColumnDisplayKind GetColumnDisplayKind(int colIndex)
        => (uint)colIndex < (uint)_displayKinds.Length ? _displayKinds[colIndex] : ColumnDisplayKind.Default;

    public bool CanContainJsonOrXml(int colIndex)
        => (uint)colIndex < (uint)_canBeJsonOrXml.Length && _canBeJsonOrXml[colIndex];

    public bool IsBinaryColumn(int colIndex)
        => (uint)colIndex < (uint)_isBinaryColumn.Length && _isBinaryColumn[colIndex];

    public string? GetColumnType(int colIndex)
    {
        if (colIndex >= 0 && colIndex < _columnTypes.Count)
            return _columnTypes[colIndex];
        return null;
    }

    public string? GetColumnType(string columnName)
    {
        if (_columnIndexMap.TryGetValue(columnName, out var idx) && idx < _columnTypes.Count)
            return _columnTypes[idx];
        return null;
    }

    public int GetColumnIndex(string columnName)
        => _columnIndexMap.TryGetValue(columnName, out var idx) ? idx : -1;

    /// <summary>
    /// Appends one row directly from an active DbDataReader in strict column ordinal order (SequentialAccess safe)
    /// without boxing value types. Call PublishWrittenRows() to make buffered rows visible to readers.
    /// </summary>
    internal int AppendRowFromReader(DbDataReader reader)
    {
        int rowIndex = _writerRowCount;
        int colCount = _columnStorages.Length;
        var sqlReader = reader as SqlDataReader;

        for (int c = 0; c < colCount; c++)
        {
            var storage = _columnStorages[c];
            storage.EnsureSlot(rowIndex);
            if (reader.IsDBNull(c))
            {
                storage.SetNull(rowIndex);
            }
            else
            {
                storage.ReadNonNull(reader, sqlReader, c, rowIndex);
            }
        }

        _writerRowCount = rowIndex + 1;
        return _writerRowCount;
    }

    internal void PublishWrittenRows()
    {
        Volatile.Write(ref _committedRowCount, _writerRowCount);
    }

    public void NotifyRowsUpdated()
    {
        OnRowsUpdated?.Invoke();
    }

    public void AddRow(object?[] row)
    {
        lock (_writerLock)
        {
            int rowIndex = _writerRowCount;
            int colCount = _columnStorages.Length;
            int copyCount = Math.Min(colCount, row.Length);

            for (int c = 0; c < colCount; c++)
            {
                var storage = _columnStorages[c];
                storage.EnsureSlot(rowIndex);
                object? val = c < copyCount ? row[c] : null;
                if (val == null || val is DBNull)
                {
                    storage.SetNull(rowIndex);
                }
                else
                {
                    storage.SetBoxedValue(rowIndex, val);
                }
            }

            _writerRowCount = rowIndex + 1;
            Volatile.Write(ref _committedRowCount, _writerRowCount);
        }
    }

    public void AddRows(IEnumerable<object?[]> rows)
    {
        lock (_writerLock)
        {
            int colCount = _columnStorages.Length;
            foreach (var row in rows)
            {
                int rowIndex = _writerRowCount;
                int copyCount = Math.Min(colCount, row.Length);
                for (int c = 0; c < colCount; c++)
                {
                    var storage = _columnStorages[c];
                    storage.EnsureSlot(rowIndex);
                    object? val = c < copyCount ? row[c] : null;
                    if (val == null || val is DBNull)
                    {
                        storage.SetNull(rowIndex);
                    }
                    else
                    {
                        storage.SetBoxedValue(rowIndex, val);
                    }
                }
                _writerRowCount = rowIndex + 1;
            }
            Volatile.Write(ref _committedRowCount, _writerRowCount);
        }
    }

    public bool IsNull(int rowIndex, int colIndex)
    {
        int count = Volatile.Read(ref _committedRowCount);
        if ((uint)rowIndex >= (uint)count || (uint)colIndex >= (uint)_columnStorages.Length)
            return true;
        return _columnStorages[colIndex].IsNull(rowIndex);
    }

    public object? GetValue(int rowIndex, int colIndex)
    {
        int count = Volatile.Read(ref _committedRowCount);
        if ((uint)rowIndex >= (uint)count || (uint)colIndex >= (uint)_columnStorages.Length)
            return null;
        return _columnStorages[colIndex].GetBoxedValue(rowIndex);
    }

    public object? GetValue(int rowIndex, string columnName)
    {
        if (_columnIndexMap.TryGetValue(columnName, out var colIndex))
        {
            return GetValue(rowIndex, colIndex);
        }
        return null;
    }

    public string? FormatCellForDisplay(int rowIndex, int colIndex)
    {
        int count = Volatile.Read(ref _committedRowCount);
        if ((uint)rowIndex >= (uint)count || (uint)colIndex >= (uint)_columnStorages.Length)
            return null;
        return _columnStorages[colIndex].FormatDisplay(rowIndex, _displayKinds[colIndex]);
    }

    public byte[]? GetBinaryBytes(int rowIndex, int colIndex)
    {
        int count = Volatile.Read(ref _committedRowCount);
        if ((uint)rowIndex >= (uint)count || (uint)colIndex >= (uint)_columnStorages.Length)
            return null;
        return _columnStorages[colIndex].GetBinaryBytes(rowIndex);
    }

    public object?[] GetRow(int index)
    {
        int count = Volatile.Read(ref _committedRowCount);
        if ((uint)index >= (uint)count)
            return Array.Empty<object?>();

        int colCount = _columnStorages.Length;
        var row = new object?[colCount];
        for (int c = 0; c < colCount; c++)
        {
            row[c] = _columnStorages[c].GetBoxedValue(index);
        }
        return row;
    }

    public List<object?[]> GetRowsSnapshot()
    {
        int count = Volatile.Read(ref _committedRowCount);
        var snapshot = new List<object?[]>(count);
        for (int i = 0; i < count; i++)
        {
            snapshot.Add(GetRow(i));
        }
        return snapshot;
    }

    public int[] BuildSortedIndices(int colIndex, bool ascending, CancellationToken cancellationToken = default)
    {
        int count = Volatile.Read(ref _committedRowCount);
        if (count == 0 || (uint)colIndex >= (uint)_columnStorages.Length)
            return Array.Empty<int>();

        return _columnStorages[colIndex].SortIndices(count, ascending, cancellationToken);
    }

    public int[] BuildFilteredIndices(
        string searchTerm,
        bool caseSensitive,
        bool matchWholeWord,
        bool exactMatch,
        int? targetColIndex = null,
        CancellationToken cancellationToken = default)
    {
        int total = Volatile.Read(ref _committedRowCount);
        if (total == 0) return Array.Empty<int>();

        int colCount = _columnStorages.Length;
        if (targetColIndex.HasValue && (uint)targetColIndex.Value >= (uint)colCount)
            return Array.Empty<int>();

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<int>();
        int startCol = targetColIndex ?? 0;
        int endCol = targetColIndex.HasValue ? targetColIndex.Value + 1 : colCount;

        for (int r = 0; r < total; r++)
        {
            if ((r & 4095) == 0 && cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();

            for (int c = startCol; c < endCol; c++)
            {
                if (_columnStorages[c].MatchesFilter(r, _displayKinds[c], searchTerm, comparison, exactMatch, matchWholeWord))
                {
                    matches.Add(r);
                    break;
                }
            }
        }

        return matches.ToArray();
    }

    public ColumnAggregateResult ComputeAggregates(int colIndex, IReadOnlyList<int>? filteredIndices = null)
    {
        int total = Volatile.Read(ref _committedRowCount);
        if (total == 0 || (uint)colIndex >= (uint)_columnStorages.Length)
        {
            return new ColumnAggregateResult(0, 0, null, null, null, null);
        }

        int activeCount = filteredIndices?.Count ?? total;
        return _columnStorages[colIndex].ComputeAggregates(activeCount, filteredIndices);
    }

    public IEnumerator<object> GetEnumerator()
    {
        int count = RowCount;
        for (int i = 0; i < count; i++)
        {
            yield return new DynamicRowAdapter(GetRow(i), _columns, _columnIndexMap);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static string FormatDateTime(DateTime dt, ColumnDisplayKind kind)
    {
        return kind switch
        {
            ColumnDisplayKind.Date => dt.ToString("yyyy-MM-dd"),
            ColumnDisplayKind.DateTime => dt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            ColumnDisplayKind.SmallDateTime => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            ColumnDisplayKind.DateTime2 => dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff"),
            _ => dt.Ticks % TimeSpan.TicksPerMillisecond != 0
                ? dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff")
                : dt.Millisecond != 0
                    ? dt.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    : dt.TimeOfDay == TimeSpan.Zero
                        ? dt.ToString("yyyy-MM-dd")
                        : dt.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    internal static bool MatchesText(string? str, string searchTerm, StringComparison comparison, bool exactMatch, bool matchWholeWord)
    {
        if (string.IsNullOrEmpty(str)) return false;

        if (exactMatch)
            return string.Equals(str, searchTerm, comparison);

        if (matchWholeWord)
        {
            var tokens = str.Split(' ', '.', '_', '-', ',', ';', ':', '/', '\\');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (string.Equals(tokens[i], searchTerm, comparison))
                    return true;
            }
            return false;
        }

        return str.IndexOf(searchTerm, comparison) >= 0;
    }

    internal abstract class ColumnStorage
    {
        public static ColumnStorage Create(Type? clrType)
        {
            if (clrType == typeof(int)) return new Int32ColumnStorage();
            if (clrType == typeof(long)) return new Int64ColumnStorage();
            if (clrType == typeof(short)) return new Int16ColumnStorage();
            if (clrType == typeof(byte)) return new ByteColumnStorage();
            if (clrType == typeof(bool)) return new BooleanColumnStorage();
            if (clrType == typeof(decimal)) return new DecimalColumnStorage();
            if (clrType == typeof(double)) return new DoubleColumnStorage();
            if (clrType == typeof(float)) return new SingleColumnStorage();
            if (clrType == typeof(DateTime)) return new DateTimeColumnStorage();
            if (clrType == typeof(DateTimeOffset)) return new DateTimeOffsetColumnStorage();
            if (clrType == typeof(TimeSpan)) return new TimeSpanColumnStorage();
            if (clrType == typeof(Guid)) return new GuidColumnStorage();
            if (clrType == typeof(string)) return new StringColumnStorage();
            if (clrType == typeof(byte[])) return new BinaryColumnStorage();
            return new ObjectColumnStorage();
        }

        public abstract void EnsureSlot(int rowIndex);
        public abstract bool IsNull(int rowIndex);
        public abstract void SetNull(int rowIndex);
        public abstract void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex);
        public abstract void SetBoxedValue(int rowIndex, object value);
        public abstract object? GetBoxedValue(int rowIndex);
        public virtual byte[]? GetBinaryBytes(int rowIndex) => null;
        public abstract string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind);
        public abstract int[] SortIndices(int count, bool ascending, CancellationToken ct);
        public abstract bool MatchesFilter(int rowIndex, ColumnDisplayKind displayKind, string searchTerm, StringComparison comparison, bool exactMatch, bool wholeWord);
        public abstract ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices);
    }

    internal abstract class StructColumnStorage<T> : ColumnStorage where T : struct
    {
        protected T[][] _chunks = new T[4][];
        protected ulong[][] _nullBits = new ulong[4][];

        public override void EnsureSlot(int rowIndex)
        {
            int chunkIdx = rowIndex >> ChunkShift;
            if (chunkIdx >= _chunks.Length)
            {
                int newLen = Math.Max(_chunks.Length * 2, chunkIdx + 1);
                var nextChunks = new T[newLen][];
                Array.Copy(_chunks, nextChunks, _chunks.Length);
                var nextNulls = new ulong[newLen][];
                Array.Copy(_nullBits, nextNulls, _nullBits.Length);

                nextNulls[chunkIdx] = new ulong[ChunkSize >> 6];
                nextChunks[chunkIdx] = new T[ChunkSize];

                Volatile.Write(ref _nullBits, nextNulls);
                Volatile.Write(ref _chunks, nextChunks);
            }
            else if (_chunks[chunkIdx] == null)
            {
                Volatile.Write(ref _nullBits[chunkIdx], new ulong[ChunkSize >> 6]);
                Volatile.Write(ref _chunks[chunkIdx], new T[ChunkSize]);
            }
        }

        public override bool IsNull(int rowIndex)
        {
            int chunkIdx = rowIndex >> ChunkShift;
            int offset = rowIndex & ChunkMask;
            var nullBits = Volatile.Read(ref _nullBits);
            return (nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) != 0;
        }

        public override void SetNull(int rowIndex)
        {
            int chunkIdx = rowIndex >> ChunkShift;
            int offset = rowIndex & ChunkMask;
            _nullBits[chunkIdx][offset >> 6] |= (1UL << (offset & 63));
        }

        protected void ClearNull(int rowIndex)
        {
            int chunkIdx = rowIndex >> ChunkShift;
            int offset = rowIndex & ChunkMask;
            _nullBits[chunkIdx][offset >> 6] &= ~(1UL << (offset & 63));
        }

        protected T ReadUnboxed(int rowIndex)
        {
            var chunks = Volatile.Read(ref _chunks);
            return chunks[rowIndex >> ChunkShift][rowIndex & ChunkMask];
        }

        protected void WriteUnboxed(int rowIndex, T value)
        {
            _chunks[rowIndex >> ChunkShift][rowIndex & ChunkMask] = value;
        }

        public override object? GetBoxedValue(int rowIndex)
        {
            if (IsNull(rowIndex)) return null;
            return ReadUnboxed(rowIndex);
        }

        public override void SetBoxedValue(int rowIndex, object value)
        {
            if (value is T typed)
            {
                ClearNull(rowIndex);
                WriteUnboxed(rowIndex, typed);
            }
            else
            {
                SetNull(rowIndex);
            }
        }

        public override string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind)
        {
            if (IsNull(rowIndex)) return null;
            return ReadUnboxed(rowIndex).ToString();
        }

        public override bool MatchesFilter(int rowIndex, ColumnDisplayKind displayKind, string searchTerm, StringComparison comparison, bool exactMatch, bool wholeWord)
        {
            if (IsNull(rowIndex)) return false;
            var str = FormatDisplay(rowIndex, displayKind);
            return MatchesText(str, searchTerm, comparison, exactMatch, wholeWord);
        }

        public override int[] SortIndices(int count, bool ascending, CancellationToken ct)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var nullIndices = new List<int>();
            var nonNullIndices = new int[count];
            var keys = new T[count];
            int nonNullCount = 0;

            for (int i = 0; i < count; i++)
            {
                if ((i & 8191) == 0 && ct.IsCancellationRequested)
                    ct.ThrowIfCancellationRequested();

                int chunkIdx = i >> ChunkShift;
                int offset = i & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) != 0)
                {
                    nullIndices.Add(i);
                }
                else
                {
                    keys[nonNullCount] = chunks[chunkIdx][offset];
                    nonNullIndices[nonNullCount] = i;
                    nonNullCount++;
                }
            }

            if (nonNullCount > 1)
            {
                Array.Sort(keys, nonNullIndices, 0, nonNullCount, Comparer<T>.Default);
                if (!ascending)
                {
                    Array.Reverse(nonNullIndices, 0, nonNullCount);
                }
            }

            var result = new int[count];
            if (ascending)
            {
                if (nullIndices.Count > 0) nullIndices.CopyTo(result, 0);
                if (nonNullCount > 0) Array.Copy(nonNullIndices, 0, result, nullIndices.Count, nonNullCount);
            }
            else
            {
                if (nonNullCount > 0) Array.Copy(nonNullIndices, 0, result, 0, nonNullCount);
                if (nullIndices.Count > 0) nullIndices.CopyTo(result, nonNullCount);
            }
            return result;
        }

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<T>();
            int count = 0;

            for (int i = 0; i < activeCount; i++)
            {
                int rowIndex = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = rowIndex >> ChunkShift;
                int offset = rowIndex & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    count++;
                    distinct.Add(chunks[chunkIdx][offset]);
                }
            }

            return new ColumnAggregateResult(count, distinct.Count, null, null, null, null);
        }
    }

    internal sealed class Int32ColumnStorage : StructColumnStorage<int>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetInt32(colIndex));

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<int>();
            int count = 0;
            double sum = 0;
            int min = int.MaxValue;
            int max = int.MinValue;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    int val = chunks[chunkIdx][offset];
                    count++;
                    distinct.Add(val);
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, sum, sum / count, (double)min, (double)max);
        }
    }

    internal sealed class Int64ColumnStorage : StructColumnStorage<long>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetInt64(colIndex));

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<long>();
            int count = 0;
            double sum = 0;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    double val = chunks[chunkIdx][offset];
                    count++;
                    distinct.Add(chunks[chunkIdx][offset]);
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, sum, sum / count, min, max);
        }
    }

    internal sealed class Int16ColumnStorage : StructColumnStorage<short>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetInt16(colIndex));

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<short>();
            int count = 0;
            double sum = 0;
            short min = short.MaxValue;
            short max = short.MinValue;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    short val = chunks[chunkIdx][offset];
                    count++;
                    distinct.Add(val);
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, sum, sum / count, (double)min, (double)max);
        }
    }

    internal sealed class ByteColumnStorage : StructColumnStorage<byte>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetByte(colIndex));

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<byte>();
            int count = 0;
            double sum = 0;
            byte min = byte.MaxValue;
            byte max = byte.MinValue;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    byte val = chunks[chunkIdx][offset];
                    count++;
                    distinct.Add(val);
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, sum, sum / count, (double)min, (double)max);
        }
    }

    internal sealed class BooleanColumnStorage : StructColumnStorage<bool>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetBoolean(colIndex));
    }

    internal sealed class DecimalColumnStorage : StructColumnStorage<decimal>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetDecimal(colIndex));

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<decimal>();
            int count = 0;
            double sum = 0;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    decimal raw = chunks[chunkIdx][offset];
                    double val = (double)raw;
                    count++;
                    distinct.Add(raw);
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, sum, sum / count, min, max);
        }
    }

    internal sealed class DoubleColumnStorage : StructColumnStorage<double>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetDouble(colIndex));

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<double>();
            int count = 0;
            double sum = 0;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    double val = chunks[chunkIdx][offset];
                    count++;
                    distinct.Add(val);
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, sum, sum / count, min, max);
        }
    }

    internal sealed class SingleColumnStorage : StructColumnStorage<float>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetFloat(colIndex));

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<float>();
            int count = 0;
            double sum = 0;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    float raw = chunks[chunkIdx][offset];
                    double val = raw;
                    count++;
                    distinct.Add(raw);
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, sum, sum / count, min, max);
        }
    }

    internal sealed class DateTimeColumnStorage : StructColumnStorage<DateTime>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetDateTime(colIndex));

        public override string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind)
        {
            if (IsNull(rowIndex)) return null;
            return FormatDateTime(ReadUnboxed(rowIndex), displayKind);
        }

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<DateTime>();
            int count = 0;
            DateTime min = DateTime.MaxValue;
            DateTime max = DateTime.MinValue;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    DateTime val = chunks[chunkIdx][offset];
                    count++;
                    distinct.Add(val);
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, null, null, min.ToString("g"), max.ToString("g"));
        }
    }

    internal sealed class DateTimeOffsetColumnStorage : StructColumnStorage<DateTimeOffset>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
        {
            var val = sqlReader != null ? sqlReader.GetDateTimeOffset(colIndex) : reader.GetFieldValue<DateTimeOffset>(colIndex);
            WriteUnboxed(rowIndex, val);
        }

        public override string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind)
        {
            if (IsNull(rowIndex)) return null;
            return ReadUnboxed(rowIndex).ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz");
        }

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullBits = Volatile.Read(ref _nullBits);
            var distinct = new HashSet<DateTimeOffset>();
            int count = 0;
            DateTimeOffset min = DateTimeOffset.MaxValue;
            DateTimeOffset max = DateTimeOffset.MinValue;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                int chunkIdx = r >> ChunkShift;
                int offset = r & ChunkMask;
                if ((nullBits[chunkIdx][offset >> 6] & (1UL << (offset & 63))) == 0)
                {
                    DateTimeOffset val = chunks[chunkIdx][offset];
                    count++;
                    distinct.Add(val);
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }

            return count == 0
                ? new ColumnAggregateResult(0, 0, null, null, null, null)
                : new ColumnAggregateResult(count, distinct.Count, null, null, min.LocalDateTime.ToString("g"), max.LocalDateTime.ToString("g"));
        }
    }

    internal sealed class TimeSpanColumnStorage : StructColumnStorage<TimeSpan>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
        {
            var val = sqlReader != null ? sqlReader.GetTimeSpan(colIndex) : reader.GetFieldValue<TimeSpan>(colIndex);
            WriteUnboxed(rowIndex, val);
        }

        public override string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind)
        {
            if (IsNull(rowIndex)) return null;
            var ts = ReadUnboxed(rowIndex);
            return displayKind == ColumnDisplayKind.Time
                ? ts.ToString(@"hh\:mm\:ss\.fffffff")
                : ts.ToString();
        }
    }

    internal sealed class GuidColumnStorage : StructColumnStorage<Guid>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteUnboxed(rowIndex, reader.GetGuid(colIndex));
    }

    internal abstract class RefColumnStorage<T> : ColumnStorage where T : class
    {
        protected T?[][] _chunks = new T?[4][];

        public override void EnsureSlot(int rowIndex)
        {
            int chunkIdx = rowIndex >> ChunkShift;
            if (chunkIdx >= _chunks.Length)
            {
                int newLen = Math.Max(_chunks.Length * 2, chunkIdx + 1);
                var nextChunks = new T?[newLen][];
                Array.Copy(_chunks, nextChunks, _chunks.Length);
                nextChunks[chunkIdx] = new T?[ChunkSize];
                Volatile.Write(ref _chunks, nextChunks);
            }
            else if (_chunks[chunkIdx] == null)
            {
                Volatile.Write(ref _chunks[chunkIdx], new T?[ChunkSize]);
            }
        }

        public override bool IsNull(int rowIndex)
        {
            var chunks = Volatile.Read(ref _chunks);
            return chunks[rowIndex >> ChunkShift][rowIndex & ChunkMask] == null;
        }

        public override void SetNull(int rowIndex)
        {
            _chunks[rowIndex >> ChunkShift][rowIndex & ChunkMask] = null;
        }

        protected T? ReadRef(int rowIndex)
        {
            var chunks = Volatile.Read(ref _chunks);
            return chunks[rowIndex >> ChunkShift][rowIndex & ChunkMask];
        }

        protected void WriteRef(int rowIndex, T? value)
        {
            _chunks[rowIndex >> ChunkShift][rowIndex & ChunkMask] = value;
        }

        public override object? GetBoxedValue(int rowIndex) => ReadRef(rowIndex);

        public override void SetBoxedValue(int rowIndex, object value)
        {
            WriteRef(rowIndex, value as T);
        }
    }

    internal sealed class StringColumnStorage : RefColumnStorage<string>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
            => WriteRef(rowIndex, reader.GetString(colIndex));

        public override string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind) => ReadRef(rowIndex);

        public override bool MatchesFilter(int rowIndex, ColumnDisplayKind displayKind, string searchTerm, StringComparison comparison, bool exactMatch, bool wholeWord)
            => MatchesText(ReadRef(rowIndex), searchTerm, comparison, exactMatch, wholeWord);

        public override int[] SortIndices(int count, bool ascending, CancellationToken ct)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullIndices = new List<int>();
            var nonNullIndices = new int[count];
            var keys = new string[count];
            int nonNullCount = 0;

            for (int i = 0; i < count; i++)
            {
                if ((i & 8191) == 0 && ct.IsCancellationRequested)
                    ct.ThrowIfCancellationRequested();

                var val = chunks[i >> ChunkShift][i & ChunkMask];
                if (val == null)
                {
                    nullIndices.Add(i);
                }
                else
                {
                    keys[nonNullCount] = val;
                    nonNullIndices[nonNullCount] = i;
                    nonNullCount++;
                }
            }

            if (nonNullCount > 1)
            {
                Array.Sort(keys, nonNullIndices, 0, nonNullCount, StringComparer.OrdinalIgnoreCase);
                if (!ascending)
                {
                    Array.Reverse(nonNullIndices, 0, nonNullCount);
                }
            }

            var result = new int[count];
            if (ascending)
            {
                if (nullIndices.Count > 0) nullIndices.CopyTo(result, 0);
                if (nonNullCount > 0) Array.Copy(nonNullIndices, 0, result, nullIndices.Count, nonNullCount);
            }
            else
            {
                if (nonNullCount > 0) Array.Copy(nonNullIndices, 0, result, 0, nonNullCount);
                if (nullIndices.Count > 0) nullIndices.CopyTo(result, nonNullCount);
            }
            return result;
        }

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                var val = chunks[r >> ChunkShift][r & ChunkMask];
                if (val != null)
                {
                    count++;
                    distinct.Add(val);
                }
            }

            return new ColumnAggregateResult(count, distinct.Count, null, null, null, null);
        }
    }

    internal sealed class BinaryColumnStorage : RefColumnStorage<byte[]>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
        {
            WriteRef(rowIndex, reader.GetFieldValue<byte[]>(colIndex));
        }

        public override byte[]? GetBinaryBytes(int rowIndex) => ReadRef(rowIndex);

        public override string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind)
        {
            var b = ReadRef(rowIndex);
            return b == null ? null : BinaryDataHelper.FormatBinaryForDisplay(b);
        }

        public override bool MatchesFilter(int rowIndex, ColumnDisplayKind displayKind, string searchTerm, StringComparison comparison, bool exactMatch, bool wholeWord)
        {
            var b = ReadRef(rowIndex);
            if (b == null) return false;
            return MatchesText(BinaryDataHelper.FormatBinaryForDisplay(b), searchTerm, comparison, exactMatch, wholeWord);
        }

        public override int[] SortIndices(int count, bool ascending, CancellationToken ct)
        {
            var chunks = Volatile.Read(ref _chunks);
            var nullIndices = new List<int>();
            var nonNullIndices = new int[count];
            var lengths = new int[count];
            int nonNullCount = 0;

            for (int i = 0; i < count; i++)
            {
                if ((i & 8191) == 0 && ct.IsCancellationRequested)
                    ct.ThrowIfCancellationRequested();

                var val = chunks[i >> ChunkShift][i & ChunkMask];
                if (val == null)
                {
                    nullIndices.Add(i);
                }
                else
                {
                    lengths[nonNullCount] = val.Length;
                    nonNullIndices[nonNullCount] = i;
                    nonNullCount++;
                }
            }

            if (nonNullCount > 1)
            {
                Array.Sort(lengths, nonNullIndices, 0, nonNullCount);
                if (!ascending)
                {
                    Array.Reverse(nonNullIndices, 0, nonNullCount);
                }
            }

            var result = new int[count];
            if (ascending)
            {
                if (nullIndices.Count > 0) nullIndices.CopyTo(result, 0);
                if (nonNullCount > 0) Array.Copy(nonNullIndices, 0, result, nullIndices.Count, nonNullCount);
            }
            else
            {
                if (nonNullCount > 0) Array.Copy(nonNullIndices, 0, result, 0, nonNullCount);
                if (nullIndices.Count > 0) nullIndices.CopyTo(result, nonNullCount);
            }
            return result;
        }

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;

            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                var val = chunks[r >> ChunkShift][r & ChunkMask];
                if (val != null)
                {
                    count++;
                    distinct.Add(Convert.ToHexString(val));
                }
            }

            return new ColumnAggregateResult(count, distinct.Count, null, null, null, null);
        }
    }

    internal sealed class ObjectColumnStorage : RefColumnStorage<object>
    {
        public override void ReadNonNull(DbDataReader reader, SqlDataReader? sqlReader, int colIndex, int rowIndex)
        {
            var val = reader.GetValue(colIndex);
            WriteRef(rowIndex, val is DBNull ? null : val);
        }

        public override byte[]? GetBinaryBytes(int rowIndex) => ReadRef(rowIndex) as byte[];

        public override string? FormatDisplay(int rowIndex, ColumnDisplayKind displayKind)
        {
            var rawVal = ReadRef(rowIndex);
            if (rawVal == null) return null;
            if (rawVal is DateTime dt) return FormatDateTime(dt, displayKind);
            if (rawVal is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz");
            if (rawVal is DateOnly d) return d.ToString("yyyy-MM-dd");
            if (rawVal is TimeOnly to) return to.ToString("HH:mm:ss.fffffff");
            if (rawVal is TimeSpan ts) return displayKind == ColumnDisplayKind.Time ? ts.ToString(@"hh\:mm\:ss\.fffffff") : ts.ToString();
            if (rawVal is byte[] b) return BinaryDataHelper.FormatBinaryForDisplay(b);
            return rawVal.ToString();
        }

        public override bool MatchesFilter(int rowIndex, ColumnDisplayKind displayKind, string searchTerm, StringComparison comparison, bool exactMatch, bool wholeWord)
        {
            var str = FormatDisplay(rowIndex, displayKind);
            return MatchesText(str, searchTerm, comparison, exactMatch, wholeWord);
        }

        public override int[] SortIndices(int count, bool ascending, CancellationToken ct)
        {
            var chunks = Volatile.Read(ref _chunks);
            var indices = new int[count];
            for (int i = 0; i < count; i++) indices[i] = i;

            Array.Sort(indices, (a, b) =>
            {
                var valA = chunks[a >> ChunkShift][a & ChunkMask];
                var valB = chunks[b >> ChunkShift][b & ChunkMask];
                int cmp = CompareObjects(valA, valB);
                return ascending ? cmp : -cmp;
            });

            return indices;
        }

        private static int CompareObjects(object? a, object? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            if (a is IComparable compA && a.GetType() == b.GetType()) return compA.CompareTo(b);
            if (a is byte[] bytesA && b is byte[] bytesB) return bytesA.Length.CompareTo(bytesB.Length);
            return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public override ColumnAggregateResult ComputeAggregates(int activeCount, IReadOnlyList<int>? filteredIndices)
        {
            var chunks = Volatile.Read(ref _chunks);
            var values = new List<object>(Math.Min(activeCount, 100000));
            for (int i = 0; i < activeCount; i++)
            {
                int r = filteredIndices != null ? filteredIndices[i] : i;
                var v = chunks[r >> ChunkShift][r & ChunkMask];
                if (v != null) values.Add(v);
            }

            if (values.Count == 0) return new ColumnAggregateResult(0, 0, null, null, null, null);

            int distinctCount = values.Select(v => v is byte[] b ? Convert.ToHexString(b) : v.ToString()).Distinct().Count();
            double? sum = null;
            double? avg = null;
            object? min = null;
            object? max = null;

            var firstVal = values[0];
            if (firstVal is short or int or long or decimal or float or double or byte)
            {
                double total = 0;
                double dMin = double.PositiveInfinity;
                double dMax = double.NegativeInfinity;
                int numCount = 0;
                foreach (var v in values)
                {
                    if (v is short or int or long or decimal or float or double or byte)
                    {
                        double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                        total += d;
                        if (d < dMin) dMin = d;
                        if (d > dMax) dMax = d;
                        numCount++;
                    }
                }
                if (numCount > 0)
                {
                    sum = total;
                    avg = total / numCount;
                    min = dMin;
                    max = dMax;
                }
            }
            else if (firstVal is DateTime or DateOnly or DateTimeOffset)
            {
                DateTime dtMin = DateTime.MaxValue;
                DateTime dtMax = DateTime.MinValue;
                int dtCount = 0;
                foreach (var v in values)
                {
                    if (v is DateTime or DateOnly or DateTimeOffset)
                    {
                        DateTime dt = v is DateOnly dOnly ? dOnly.ToDateTime(TimeOnly.MinValue) :
                                      v is DateTimeOffset dto ? dto.LocalDateTime :
                                      Convert.ToDateTime(v);
                        if (dt < dtMin) dtMin = dt;
                        if (dt > dtMax) dtMax = dt;
                        dtCount++;
                    }
                }
                if (dtCount > 0)
                {
                    min = dtMin.ToString("g");
                    max = dtMax.ToString("g");
                }
            }

            return new ColumnAggregateResult(values.Count, distinctCount, sum, avg, min, max);
        }
    }
}

/// <summary>
/// Lightweight backward-compatible dynamic row adapter that avoids allocating a full Dictionary.
/// </summary>
public class DynamicRowAdapter : DynamicObject, IDictionary<string, object?>
{
    private readonly object?[] _values;
    private readonly IReadOnlyList<string> _columns;
    private readonly Dictionary<string, int> _columnIndexMap;

    public DynamicRowAdapter(object?[] values, IReadOnlyList<string> columns, Dictionary<string, int> columnIndexMap)
    {
        _values = values;
        _columns = columns;
        _columnIndexMap = columnIndexMap;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        if (_columnIndexMap.TryGetValue(binder.Name, out var idx) && idx < _values.Length)
        {
            result = _values[idx];
            return true;
        }
        result = null;
        return false;
    }

    public object? this[string key]
    {
        get
        {
            if (_columnIndexMap.TryGetValue(key, out var idx) && idx < _values.Length)
                return _values[idx];
            return null;
        }
        set
        {
            if (_columnIndexMap.TryGetValue(key, out var idx) && idx < _values.Length)
                _values[idx] = value;
        }
    }

    public ICollection<string> Keys => _columns.ToList();
    public ICollection<object?> Values => _values;
    public int Count => _columns.Count;
    public bool IsReadOnly => true;

    public void Add(string key, object? value) => throw new NotSupportedException();
    public void Add(KeyValuePair<string, object?> item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Remove(string key) => throw new NotSupportedException();
    public bool Remove(KeyValuePair<string, object?> item) => throw new NotSupportedException();

    public bool ContainsKey(string key) => _columnIndexMap.ContainsKey(key);

    public bool Contains(KeyValuePair<string, object?> item)
    {
        if (_columnIndexMap.TryGetValue(item.Key, out var idx) && idx < _values.Length)
            return Equals(_values[idx], item.Value);
        return false;
    }

    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            array[arrayIndex + i] = new KeyValuePair<string, object?>(_columns[i], i < _values.Length ? _values[i] : null);
        }
    }

    public bool TryGetValue(string key, out object? value)
    {
        if (_columnIndexMap.TryGetValue(key, out var idx) && idx < _values.Length)
        {
            value = _values[idx];
            return true;
        }
        value = null;
        return false;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            yield return new KeyValuePair<string, object?>(_columns[i], i < _values.Length ? _values[i] : null);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
