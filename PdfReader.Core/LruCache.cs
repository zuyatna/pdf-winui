using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PdfReader.Core;

public sealed class LruCache<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private sealed record CacheEntry(TKey Key, TValue Value, long ByteSize);

    private readonly object _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _entries = new();
    private readonly LinkedList<CacheEntry> _lruList = new();
    private readonly long _maxBytes;
    private readonly Action<TValue>? _onEvict;
    private long _currentBytes;
    private bool _disposed;

    public LruCache(long maxBytes, Action<TValue>? onEvict = null)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum byte capacity must be positive.");
        }

        _maxBytes = maxBytes;
        _onEvict = onEvict;
    }

    public long MaxBytes => _maxBytes;

    public long CurrentBytes
    {
        get
        {
            lock (_gate)
            {
                return _currentBytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    public void Set(TKey key, TValue value, long byteSize)
    {
        if (byteSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteSize), "Entry byte size must be positive.");
        }

        var evicted = new List<TValue>();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? existingNode))
            {
                _lruList.Remove(existingNode);
                _entries.Remove(key);
                _currentBytes -= existingNode.Value.ByteSize;

                if (!EqualityComparer<TValue>.Default.Equals(existingNode.Value.Value, value))
                {
                    evicted.Add(existingNode.Value.Value);
                }
            }

            if (byteSize <= _maxBytes)
            {
                while (_currentBytes + byteSize > _maxBytes && _lruList.Last is { } lruNode)
                {
                    _lruList.RemoveLast();
                    _entries.Remove(lruNode.Value.Key);
                    _currentBytes -= lruNode.Value.ByteSize;
                    evicted.Add(lruNode.Value.Value);
                }

                var entry = new CacheEntry(key, value, byteSize);
                LinkedListNode<CacheEntry> newNode = _lruList.AddFirst(entry);
                _entries[key] = newNode;
                _currentBytes += byteSize;
            }
        }

        foreach (TValue item in evicted)
        {
            EvictValue(item);
        }
    }

    public bool Remove(TKey key)
    {
        TValue? removedValue = default;
        bool removed = false;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                _lruList.Remove(node);
                _entries.Remove(key);
                _currentBytes -= node.Value.ByteSize;
                removedValue = node.Value.Value;
                removed = true;
            }
        }

        if (removed && removedValue is not null)
        {
            EvictValue(removedValue);
        }

        return removed;
    }

    public void Clear()
    {
        List<TValue> valuesToEvict;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            valuesToEvict = DrainEntriesLocked();
        }

        foreach (TValue item in valuesToEvict)
        {
            EvictValue(item);
        }
    }

    public void Dispose()
    {
        List<TValue> valuesToEvict;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            valuesToEvict = DrainEntriesLocked();
        }

        foreach (TValue item in valuesToEvict)
        {
            EvictValue(item);
        }
    }

    private List<TValue> DrainEntriesLocked()
    {
        var values = new List<TValue>(_entries.Count);
        foreach (CacheEntry entry in _lruList)
        {
            values.Add(entry.Value);
        }

        _lruList.Clear();
        _entries.Clear();
        _currentBytes = 0;
        return values;
    }

    private void EvictValue(TValue value)
    {
        if (_onEvict is not null)
        {
            _onEvict(value);
        }
        else if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
