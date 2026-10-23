using Avalonia.Media.Imaging;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Bounded raster LRU. Geometry and wrappers are weak: the shared geometry cache is
/// their sole cache owner. Raster pixels have a separate 32 MiB estimated payload budget.
/// </summary>
internal sealed class GdsPreviewCache
{
    internal const int MaxEntries = 50;
    private readonly long _maxBitmapBytes;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly object _gate = new();
    private long _bytes;

    private sealed class Entry(string key, GdsPreviewData? value)
    {
        public string Key { get; } = key;
        public bool Empty { get; } = value == null;
        public WeakReference<GdsPreviewData>? Wrapper { get; } = value == null ? null : new(value);
        public WeakReference<NazcaPreviewResult>? Geometry { get; } = value == null ? null : new(value.Result);
        public RenderTargetBitmap? Bitmap { get; } = value?.Bitmap;
        public double Width { get; } = value?.WidthMicrometers ?? 0;
        public double Height { get; } = value?.HeightMicrometers ?? 0;
        public GdsPreviewLayerRoles Roles { get; } = value?.LayerRoles ?? GdsPreviewLayerRoles.Default;
        public long Bytes { get; } = value?.Bitmap is { } bitmap
            ? 4L * bitmap.PixelSize.Width * bitmap.PixelSize.Height : 0;
    }

    public GdsPreviewCache(long maxBitmapBytes = PreviewGeometryCache.MaxPayloadBytes)
    {
        if (maxBitmapBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBitmapBytes));
        _maxBitmapBytes = maxBitmapBytes;
    }

    public bool TryGet(string key, out GdsPreviewData? value)
    {
        lock (_gate)
        {
            value = null;
            if (!_map.TryGetValue(key, out var node)) return false;
            var entry = node.Value;
            if (!entry.Empty)
            {
                if (!entry.Geometry!.TryGetTarget(out var geometry))
                {
                    Remove(node);
                    return false;
                }
                if (!entry.Wrapper!.TryGetTarget(out value))
                {
                    value = new GdsPreviewData(geometry, entry.Width, entry.Height)
                    {
                        Bitmap = entry.Bitmap, LayerRoles = entry.Roles
                    };
                    entry.Wrapper.SetTarget(value);
                }
            }
            _lru.Remove(node);
            _lru.AddFirst(node);
            return true;
        }
    }

    public void Set(string key, GdsPreviewData? value)
    {
        var entry = new Entry(key, value);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing)) Remove(existing);
            // Geometry is weakly referenced, including oversized results owned by callers.
            if (entry.Bytes > _maxBitmapBytes) return;
            while (_lru.Last is { } oldest && (_lru.Count >= MaxEntries || _bytes + entry.Bytes > _maxBitmapBytes))
                Remove(oldest);
            _map.Add(key, _lru.AddFirst(entry));
            _bytes += entry.Bytes;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
            _bytes = 0;
        }
    }

    public int Count { get { lock (_gate) return _map.Count; } }

    private void Remove(LinkedListNode<Entry> node)
    {
        _map.Remove(node.Value.Key);
        _lru.Remove(node);
        _bytes -= node.Value.Bytes;
    }
}
