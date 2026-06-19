namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Process-lifetime LRU cache for GDS preview data.
/// Key format: <c>"{nazcaFunctionName}|{width:F2}|{height:F2}"</c>.
/// A <c>null</c> value means the fetch succeeded but yielded no polygons (or
/// failed), so no retry should be attempted for that template in this session.
/// </summary>
/// <remarks>
/// A typical canvas contains 5–15 unique component templates, so the default
/// capacity of 50 entries provides a very high cache-hit rate in practice.
/// </remarks>
internal sealed class GdsPreviewCache
{
    /// <summary>Maximum number of entries retained in the cache.</summary>
    internal const int MaxEntries = 50;

    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly object _lock = new();

    private readonly record struct CacheEntry(string Key, GdsPreviewData? Value);

    /// <summary>
    /// Attempts to retrieve a cached entry.
    /// Returns <c>true</c> when the key is present (even when the stored value
    /// is <c>null</c>, which indicates a completed-but-empty result).
    /// </summary>
    public bool TryGet(string key, out GdsPreviewData? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Stores a preview result. Evicts the least-recently-used entry when
    /// <see cref="MaxEntries"/> is exceeded.
    /// </summary>
    public void Set(string key, GdsPreviewData? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _map.Remove(key);
            }

            while (_lru.Count >= MaxEntries && _lru.Last != null)
            {
                var evicted = _lru.Last.Value;
                _lru.RemoveLast();
                _map.Remove(evicted.Key);
            }

            var node = _lru.AddFirst(new CacheEntry(key, value));
            _map[key] = node;
        }
    }

    /// <summary>Gets the current number of entries in the cache.</summary>
    public int Count
    {
        get { lock (_lock) return _map.Count; }
    }
}
