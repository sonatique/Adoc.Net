namespace AdocNet.Caching;

/// <summary>
/// A thread-safe, bounded least-recently-used (LRU) cache.
/// On capacity overflow, the least-recently-accessed entry is evicted.
/// All operations are O(1) via dictionary + doubly-linked list.
/// </summary>
/// <typeparam name="TKey">The type of cache keys.</typeparam>
/// <typeparam name="TValue">The type of cached values.</typeparam>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _list = new();
    private int _capacity;

    /// <summary>
    /// Initializes a new LRU cache with the specified maximum capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of entries. Must be >= 1.</param>
    public LruCache(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
    }

    /// <summary>Gets the current number of entries in the cache.</summary>
    public int Count
    {
        get { lock (_lock) return _map.Count; }
    }

    /// <summary>Gets or sets the maximum cache capacity. Evicts excess entries when reduced.</summary>
    public int Capacity
    {
        get { lock (_lock) return _capacity; }
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Capacity must be at least 1.");
            lock (_lock)
            {
                _capacity = value;
                EvictExcess();
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve a cached value. On hit, the entry is promoted to most-recently-used.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The cached value if found.</param>
    /// <returns>True if the key was found in the cache.</returns>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    /// <summary>
    /// Adds or updates an entry. If the cache is at capacity, the least-recently-used entry is evicted.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                // Update existing: move to front with new value
                _list.Remove(existing);
                existing.Value = new CacheEntry(key, value);
                _list.AddFirst(existing);
                return;
            }

            // Add new entry at front
            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
            _list.AddFirst(node);
            _map[key] = node;

            // Evict if over capacity
            EvictExcess();
        }
    }

    /// <summary>Removes all entries from the cache.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _list.Clear();
        }
    }

    private void EvictExcess()
    {
        while (_map.Count > _capacity)
        {
            var last = _list.Last!;
            _map.Remove(last.Value.Key);
            _list.RemoveLast();
        }
    }

    private readonly struct CacheEntry
    {
        public readonly TKey Key;
        public readonly TValue Value;

        public CacheEntry(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }
}
