using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Bounded weak delivery of results too large for the geometry cache. A collected result
/// remains a completed marker, so repainting uses the ordinary rectangle fallback instead
/// of repeatedly launching Python. Activation/PDK invalidation allows another attempt.
/// </summary>
internal sealed class OversizedPreviewHandoff
{
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private sealed record Entry(string Key, WeakReference<NazcaPreviewResult> Result);

    // The containing render service serializes access with its environment gate.
    public bool TryGet(string key, out NazcaPreviewResult? result)
    {
        result = null;
        if (!_entries.TryGetValue(key, out var node)) return false;
        _recent.Remove(node);
        _recent.AddFirst(node);
        node.Value.Result.TryGetTarget(out result);
        return true;
    }

    public void Set(string key, NazcaPreviewResult result)
    {
        if (_entries.Remove(key, out var existing)) _recent.Remove(existing);
        while (_entries.Count >= PreviewGeometryCache.MaxEntries && _recent.Last is { } oldest)
        {
            _entries.Remove(oldest.Value.Key);
            _recent.RemoveLast();
        }
        _entries.Add(key, _recent.AddFirst(new Entry(key, new WeakReference<NazcaPreviewResult>(result))));
    }

    public void Clear()
    {
        _entries.Clear();
        _recent.Clear();
    }
}
