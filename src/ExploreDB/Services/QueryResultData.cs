using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace ExploreDB.Services;

/// <summary>
/// Ultra-high performance, compact columnar row-store capable of holding 5,000,000+ rows
/// with minimal RAM overhead (no per-row Dictionary or ExpandoObject allocation).
/// </summary>
public class QueryResultTable : IEnumerable<object>
{
    private readonly List<object?[]> _rows = new();
    private readonly List<string> _columns = new();
    private readonly List<string> _columnTypes = new();
    private readonly Dictionary<string, int> _columnIndexMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public IReadOnlyList<string> Columns => _columns;
    public IReadOnlyList<string> ColumnTypes => _columnTypes;
    public int RowCount
    {
        get
        {
            lock (_lock)
            {
                return _rows.Count;
            }
        }
    }

    public bool IsComplete { get; set; } = false;
    public string? TableName { get; set; }

    public QueryResultTable(IEnumerable<string> columns, IEnumerable<string>? columnTypes = null)
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
    }

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

    public void AddRow(object?[] row)
    {
        lock (_lock)
        {
            _rows.Add(row);
        }
    }

    public void AddRows(IEnumerable<object?[]> rows)
    {
        lock (_lock)
        {
            _rows.AddRange(rows);
        }
    }

    public object?[] GetRow(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _rows.Count)
                return _rows[index];
            return Array.Empty<object?>();
        }
    }

    public object? GetValue(int rowIndex, int colIndex)
    {
        lock (_lock)
        {
            if (rowIndex >= 0 && rowIndex < _rows.Count)
            {
                var row = _rows[rowIndex];
                if (colIndex >= 0 && colIndex < row.Length)
                    return row[colIndex];
            }
            return null;
        }
    }

    public object? GetValue(int rowIndex, string columnName)
    {
        if (_columnIndexMap.TryGetValue(columnName, out var colIndex))
        {
            return GetValue(rowIndex, colIndex);
        }
        return null;
    }

    public int GetColumnIndex(string columnName)
    {
        return _columnIndexMap.TryGetValue(columnName, out var idx) ? idx : -1;
    }

    public List<object?[]> GetRowsSnapshot()
    {
        lock (_lock)
        {
            return new List<object?[]>(_rows);
        }
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
