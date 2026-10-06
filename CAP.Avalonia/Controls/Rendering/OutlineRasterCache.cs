using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Raster level-of-detail for outline geometry. Zoomed out, a full-chip import shows
/// tens of thousands of polygons a few pixels large; rasterizing them as vectors every
/// frame dropped the canvas to a few frames per second. This cache renders an outline
/// list once into a bitmap at a power-of-two pixel density at or above the current zoom
/// and draws that bitmap afterwards — one image per component (or imported route) per
/// frame. All instances of a template share their outline list and therefore one bitmap.
/// Memory is bounded by a pixel budget with least-recently-used eviction; a list gets a
/// bitmap only from its second request on, so geometry rebuilt every frame (a drag) never
/// churns bitmaps. Zoomed in, or above <see cref="MaxBitmapEdgePx"/>, the caller draws vectors.
/// </summary>
internal sealed class OutlineRasterCache
{
    /// <summary>Largest bitmap edge (px) the cache creates; beyond it vectors are drawn.</summary>
    public const int MaxBitmapEdgePx = 4096;

    /// <summary>Zoom from which on vectors are always drawn (sharp edges once polygons are large).</summary>
    public const double VectorFromZoom = 0.75;

    /// <summary>Pixels all bitmaps of one cache may hold together (×4 bytes).</summary>
    public const long PixelBudget = 32L * 1024 * 1024;

    /// <summary>Delay before an evicted bitmap is disposed: frames already recorded may still draw it.</summary>
    private static readonly TimeSpan DisposeDelay = TimeSpan.FromSeconds(2);

    private readonly ConditionalWeakTable<IReadOnlyList<OutlinePolygon>, Entry> _entries = new();
    private readonly LinkedList<Slot> _lru = new();
    private long _pixels;
    private bool _disabled;

    /// <summary>
    /// Draws the cached bitmap of <paramref name="batches"/> in the current (component-local)
    /// transform, creating it when needed. Returns false when the caller must draw vectors:
    /// the zoom asks for them, the bitmap would be too large, the list is seen for the first
    /// time, or rasterizing failed.
    /// </summary>
    public bool TryDraw(
        DrawingContext context,
        IReadOnlyList<OutlinePolygon> outlines,
        OutlineBatch[] batches,
        double zoom,
        GdsLayerVisibilityState? layerVisibility)
    {
        if (_disabled || batches.Length == 0 || zoom >= VectorFromZoom || zoom <= 0) return false;
        var bounds = Union(batches);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        double density = Math.Pow(2, Math.Ceiling(Math.Log2(zoom)));
        int width = (int)Math.Ceiling(bounds.Width * density);
        int height = (int)Math.Ceiling(bounds.Height * density);
        if (width > MaxBitmapEdgePx || height > MaxBitmapEdgePx) return false;

        var entry = _entries.GetValue(outlines, _ => new Entry());
        var key = new SlotKey(density, VisibilityKey(batches, layerVisibility));
        var slot = entry.Slots.Find(s => s.Key == key);
        if (slot is null)
        {
            if (!entry.SeenKeys.Add(key)) slot = Create(entry, key, batches, bounds, density, width, height, layerVisibility);
            if (slot is null) return false;
        }
        Touch(slot);
        context.DrawImage(slot.Bitmap, new Rect(0, 0, slot.Bitmap.PixelSize.Width, slot.Bitmap.PixelSize.Height),
            new Rect(bounds.X, bounds.Y, width / density, height / density));
        return true;
    }

    private Slot? Create(Entry entry, SlotKey key, OutlineBatch[] batches, Rect bounds, double density,
        int width, int height, GdsLayerVisibilityState? layerVisibility)
    {
        RenderTargetBitmap bitmap;
        try
        {
            bitmap = Render(batches, bounds, density, width, height, layerVisibility);
        }
        catch (Exception)
        {
            // Out of memory or no render surface: vectors still draw correctly, just slower.
            _disabled = true;
            return null;
        }
        var slot = new Slot(entry, key, bitmap, (long)width * height);
        entry.Slots.Add(slot);
        slot.Node = _lru.AddLast(slot);
        _pixels += slot.Pixels;
        EvictOverBudget();
        return slot;
    }

    private void Touch(Slot slot)
    {
        if (slot.Node is null || slot.Node == _lru.Last) return;
        _lru.Remove(slot.Node);
        _lru.AddLast(slot.Node);
    }

    private void EvictOverBudget()
    {
        while (_pixels > PixelBudget && _lru.First is { } oldest && oldest != _lru.Last)
        {
            var slot = oldest.Value;
            _lru.RemoveFirst();
            slot.Owner.Slots.Remove(slot);
            _pixels -= slot.Pixels;
            DisposeLater(slot.Bitmap);
        }
    }

    private static void DisposeLater(RenderTargetBitmap bitmap) =>
        _ = Task.Delay(DisposeDelay).ContinueWith(
            _ => Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background),
            TaskScheduler.Default);

    private static RenderTargetBitmap Render(
        OutlineBatch[] batches, Rect bounds, double density, int width, int height,
        GdsLayerVisibilityState? layerVisibility)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, width), Math.Max(1, height)));
        using var ctx = bitmap.CreateDrawingContext();
        using var _ = ctx.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y) * Matrix.CreateScale(density, density));
        foreach (var batch in batches)
        {
            double opacity = layerVisibility?.EffectiveOpacity(batch.Layer, batch.DataType) ?? 1.0;
            if (opacity <= 0) continue;
            // One device pixel wide: the 1 µm outline pen would be a sub-pixel hairline here.
            var pen = new Pen(batch.Outline.Brush, 1.0 / density);
            using (opacity < 1.0 ? ctx.PushOpacity(opacity) : (IDisposable?)null)
                ctx.DrawGeometry(batch.Fill, pen, batch.Geometry);
        }
        return bitmap;
    }

    /// <summary>The exact opacities the bitmap is rendered with, so any layer toggle re-renders it.</summary>
    private static string VisibilityKey(OutlineBatch[] batches, GdsLayerVisibilityState? layerVisibility) =>
        layerVisibility is null
            ? ""
            : string.Join(";", batches.Select(b => (b.Layer, b.DataType)).Distinct()
                .Select(l => layerVisibility.EffectiveOpacity(l.Layer, l.DataType).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    private static Rect Union(OutlineBatch[] batches)
    {
        var rect = batches[0].Bounds;
        for (int i = 1; i < batches.Length; i++)
            rect = rect.Union(batches[i].Bounds);
        return rect;
    }

    private readonly record struct SlotKey(double Density, string Visibility);

    /// <summary>Bitmaps of one outline list plus the keys requested once (bitmap on the second request).</summary>
    private sealed class Entry
    {
        public List<Slot> Slots { get; } = new();
        public HashSet<SlotKey> SeenKeys { get; } = new();
    }

    private sealed class Slot
    {
        public Slot(Entry owner, SlotKey key, RenderTargetBitmap bitmap, long pixels)
        {
            Owner = owner;
            Key = key;
            Bitmap = bitmap;
            Pixels = pixels;
        }

        public Entry Owner { get; }
        public SlotKey Key { get; }
        public RenderTargetBitmap Bitmap { get; }
        public long Pixels { get; }
        public LinkedListNode<Slot>? Node { get; set; }
    }
}
