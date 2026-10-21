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

    /// <summary>Draws an existing image level without requesting source geometry.</summary>
    public bool TryDrawCached(DrawingContext context, IReadOnlyList<OutlinePolygon> outlines,
        double zoom, GdsLayerVisibilityState? layerVisibility, IBrush? highlightBrush = null) =>
        TryDraw(context, outlines, Array.Empty<OutlineBatch>(), zoom, layerVisibility, highlightBrush);

    /// <summary>Draws or builds a raster level; false asks the caller to draw its vector batches.</summary>
    public bool TryDraw(
        DrawingContext context,
        IReadOnlyList<OutlinePolygon> outlines,
        OutlineBatch[] batches,
        double zoom,
        GdsLayerVisibilityState? layerVisibility,
        IBrush? highlightBrush = null)
    {
        if (_disabled || zoom >= VectorFromZoom || zoom <= 0 || highlightBrush is not null and not ISolidColorBrush) return false;
        if (!_entries.TryGetValue(outlines, out var entry))
        {
            if (batches.Length == 0) return false;
            entry = _entries.GetValue(outlines, _ => new Entry(batches));
        }
        var bounds = entry.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        double density = Math.Pow(2, Math.Ceiling(Math.Log2(zoom)));
        int width = (int)Math.Ceiling(bounds.Width * density);
        int height = (int)Math.Ceiling(bounds.Height * density);
        if (width > MaxBitmapEdgePx || height > MaxBitmapEdgePx) return false;

        var key = new SlotKey(density, VisibilityKey(entry.Layers, layerVisibility), (highlightBrush as ISolidColorBrush)?.Color);
        // Keep a real image pyramid: derive coarse levels from a sharper image,
        // avoiding both full-resolution sampling and source vertex resubmission.
        var slot = entry.Slots.Find(s => s.Key == key);
        if (slot is null)
        {
            var sharper = entry.Slots.Where(s => s.Key.Density > density && s.Key.Visibility == key.Visibility && s.Key.Tint == key.Tint)
                .OrderBy(s => s.Key.Density).FirstOrDefault();
            if (sharper is not null)
                slot = Downsample(entry, key, sharper, density, width, height) ?? sharper;
        }
        if (slot is null && highlightBrush is not null && entry.Slots.Any(s => s.Key == key with { Tint = null }))
            slot = Create(entry, key, batches, bounds, density, width, height, layerVisibility, highlightBrush);
        if (slot is null)
        {
            if (batches.Length == 0) return false;
            if (!entry.SeenKeys.Add(key)) slot = Create(entry, key, batches, bounds, density, width, height, layerVisibility, highlightBrush);
            if (slot is null) return false;
        }
        Touch(slot);
        DrawImage(context, slot.Bitmap,
            new Rect(bounds.X, bounds.Y, slot.Bitmap.PixelSize.Width / slot.Key.Density,
                slot.Bitmap.PixelSize.Height / slot.Key.Density), null);
        return true;
    }

    /// <summary>Draws the source image or tints its alpha while building a cached emphasis bitmap.</summary>
    internal static void DrawImage(DrawingContext context, RenderTargetBitmap bitmap, Rect bounds, IBrush? highlightBrush)
    {
        if (highlightBrush is null)
        {
            context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), bounds);
            return;
        }
        using (context.PushOpacityMask(new ImageBrush(bitmap) { Stretch = Stretch.Fill }, bounds))
            context.FillRectangle(highlightBrush, bounds);
    }

    /// <summary>Bakes an emphasis color once; drawing an opacity layer every frame is expensive.</summary>
    internal static RenderTargetBitmap TintBitmap(RenderTargetBitmap source, IBrush tint)
    {
        var bitmap = new RenderTargetBitmap(source.PixelSize);
        using var context = bitmap.CreateDrawingContext();
        DrawImage(context, source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height), tint);
        return bitmap;
    }

    private Slot? Downsample(Entry entry, SlotKey key, Slot source, double density, int width, int height)
    {
        try
        {
            var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, width), Math.Max(1, height)));
            using (var context = bitmap.CreateDrawingContext())
                context.DrawImage(source.Bitmap, new Rect(0, 0,
                    source.Bitmap.PixelSize.Width * density / source.Key.Density,
                    source.Bitmap.PixelSize.Height * density / source.Key.Density));
            var slot = new Slot(entry, key, bitmap, (long)Math.Max(1, width) * Math.Max(1, height));
            entry.Slots.Add(slot);
            slot.Node = _lru.AddLast(slot);
            _pixels += slot.Pixels;
            EvictOverBudget();
            return slot;
        }
        catch (Exception)
        {
            return null; // Retain the sharper image if a smaller surface cannot be allocated.
        }
    }
    private Slot? Create(Entry entry, SlotKey key, OutlineBatch[] batches, Rect bounds, double density,
        int width, int height, GdsLayerVisibilityState? layerVisibility, IBrush? highlightBrush)
    {
        RenderTargetBitmap bitmap;
        try
        {
            var sourceSlot = entry.Slots.Find(s => s.Key == key with { Tint = null });
            if (highlightBrush is not null && sourceSlot is not null)
                bitmap = TintBitmap(sourceSlot.Bitmap, highlightBrush);
            else
            {
                bitmap = Render(batches, bounds, density, width, height, layerVisibility);
                if (highlightBrush is not null)
                {
                    var tinted = TintBitmap(bitmap, highlightBrush);
                    bitmap.Dispose();
                    bitmap = tinted;
                }
            }
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
    private static string VisibilityKey((int Layer, int DataType)[] layers, GdsLayerVisibilityState? layerVisibility) =>
        layerVisibility is null
            ? ""
            : string.Join(";", layers
                .Select(l => layerVisibility.EffectiveOpacity(l.Layer, l.DataType).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    private static Rect Union(OutlineBatch[] batches)
    {
        var rect = batches[0].Bounds;
        for (int i = 1; i < batches.Length; i++)
            rect = rect.Union(batches[i].Bounds);
        return rect;
    }

    private readonly record struct SlotKey(double Density, string Visibility, Color? Tint);

    /// <summary>Bitmaps of one outline list plus the keys requested once (bitmap on the second request).</summary>
    private sealed class Entry
    {
        public Entry(OutlineBatch[] batches)
        {
            Bounds = Union(batches);
            Layers = batches.Select(b => (b.Layer, b.DataType)).Distinct().ToArray();
        }

        public Rect Bounds { get; }
        public (int Layer, int DataType)[] Layers { get; }
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
