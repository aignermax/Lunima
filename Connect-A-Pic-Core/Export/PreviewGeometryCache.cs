namespace CAP_Core.Export;

/// <summary>
/// Weighted LRU owner shared by render backends and presentation adapters.
/// The budget estimates coordinate payload and UTF-16 strings, not managed heap size.
/// </summary>
public sealed class PreviewGeometryCache
{
    public const int MaxEntries = 200;
    public const long MaxPayloadBytes = 32L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private long _bytes;
    private long _generation;
    private sealed record Entry(string Key, NazcaPreviewResult? Result, long Bytes);

    public int Count { get { lock (_gate) return _entries.Count; } }
    public long Generation { get { lock (_gate) return _generation; } }

    public bool TryGet(string key, out NazcaPreviewResult? result)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                result = node.Value.Result;
                return true;
            }
            result = null;
            return false;
        }
    }

    public void Set(string key, NazcaPreviewResult? result, long? expectedGeneration = null)
    {
        var bytes = EstimatePayloadBytes(key, result);
        lock (_gate)
        {
            if (expectedGeneration.HasValue && expectedGeneration != _generation) return;
            if (_entries.TryGetValue(key, out var existing)) Remove(existing);
            if (bytes > MaxPayloadBytes) return;
            while (_recent.Last is { } oldest &&
                   (_entries.Count >= MaxEntries || _bytes + bytes > MaxPayloadBytes))
                Remove(oldest);
            _entries.Add(key, _recent.AddFirst(new Entry(key, result, bytes)));
            _bytes += bytes;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            _entries.Clear();
            _recent.Clear();
            _bytes = 0;
        }
    }

    /// <summary>Counts coordinates, pin data, bounding box, keys and retained strings.</summary>
    public static long EstimatePayloadBytes(string key, NazcaPreviewResult? result)
    {
        long bytes = 32 + 2L * key.Length;
        if (result == null) return bytes;
        bytes += TextBytes(result.Source) + TextBytes(result.Error) + TextBytes(result.PolygonWarning);
        foreach (var polygon in result.Polygons) bytes += 12L + 16L * polygon.Vertices.Count;
        foreach (var pin in result.Pins) bytes += 48L + TextBytes(pin.Name);
        return bytes;
    }

    private static long TextBytes(string? value) => 2L * (value?.Length ?? 0);
    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _recent.Remove(node);
        _bytes -= node.Value.Bytes;
    }
}
