using System;
using System.Collections.Generic;
using System.Threading;

namespace ExploreDB.Services.Profiler.Buffers;

/// <summary>
/// High-performance thread-safe circular ring buffer for bounded live event storage.
/// </summary>
public sealed class CircularEventBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private int _head;
    private int _count;
    private readonly ReaderWriterLockSlim _lock = new();

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public CircularEventBuffer(int capacity = 100_000)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        _lock.EnterWriteLock();
        try
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity)
            {
                _count++;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void AddRange(IEnumerable<T> items)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var item in items)
            {
                _buffer[_head] = item;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity)
                {
                    _count++;
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public List<T> ToList()
    {
        _lock.EnterReadLock();
        try
        {
            var list = new List<T>(_count);
            int start = _count < _capacity ? 0 : _head;

            for (int i = 0; i < _count; i++)
            {
                int index = (start + i) % _capacity;
                list.Add(_buffer[index]);
            }

            return list;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            Array.Clear(_buffer, 0, _capacity);
            _head = 0;
            _count = 0;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
