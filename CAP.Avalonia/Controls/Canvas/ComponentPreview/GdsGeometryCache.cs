using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Process-lifetime LRU cache for resolution-independent GDS preview geometry,
/// keyed by <see cref="GdsPreviewKey.Hash"/>.
/// A <c>null</c> value means the render completed but yielded no polygons (or
/// failed), so no retry should be attempted for that key in this session.
/// </summary>
/// <remarks>
/// The component library exposes many more templates than a single canvas, so the
/// capacity is bumped to 200 entries to keep a high cache-hit rate across the
/// whole library.
/// </remarks>
internal sealed class GdsGeometryCache
{
    /// <summary>Maximum number of entries retained in the cache.</summary>
    internal const int MaxEntries = 200;

    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly object _lock = new();

    private readonly record struct CacheEntry(string Key, NazcaPreviewResult? Value);

    /// <summary>
    /// Attempts to retrieve a cached entry.
    /// Returns <c>true</c> when the key is present (even when the stored value
    /// is <c>null</c>, which indicates a completed-but-empty result).
    /// </summary>
    public bool TryGet(string key, out NazcaPreviewResult? value)
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
    /// Stores a geometry result. Evicts the least-recently-used entry when
    /// <see cref="MaxEntries"/> is exceeded.
    /// </summary>
    public void Set(string key, NazcaPreviewResult? value)
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
