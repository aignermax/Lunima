using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Overview raster for grouped or canvas-level imported routes. Sources are painted
/// in their original order, preserving overlapping translucent fills and layer order.
/// No polygons are merged, and source/export geometry is never changed.
/// </summary>
internal sealed class GroupFrozenPathRasterCache
{
    private const int MinimumPaths = 32;
    private const int MaxEdge = 4096;
    private const int MaximumEntries = 64;
    private const long PixelBudget = 32L * 1024 * 1024;
    private readonly ConditionalWeakTable<object, Snapshot> _snapshots = new();
    private readonly LinkedList<Slot> _lru = new();
    private long _pixels;
    private bool _disabled;

    internal long IssuedImageCount { get; private set; }
    internal long BitmapBuildCount { get; private set; }

    internal sealed class Snapshot
    {
        internal AsDrawnGeometry?[] Sources { get; set; } = Array.Empty<AsDrawnGeometry?>();
        internal Rect Bounds;
        internal bool Eligible;
        internal (int Layer, int DataType)[] Layers = Array.Empty<(int, int)>();
        internal readonly List<Slot> Slots = new();
        internal readonly HashSet<Key> Seen = new();
    }

    internal readonly record struct Key(double Density, string Visibility, Color? Tint);
    internal sealed record Slot(WeakReference<Snapshot> Owner, Key Key, RenderTargetBitmap Bitmap, Rect Bounds, long Pixels)
    {
        internal LinkedListNode<Slot>? Node;
    }

    /// <summary>
    /// Validates mutable routes once per frame. The same source array is reused by
    /// individual drawing when a mixed group, high zoom or oversized raster falls back.
    /// </summary>
    internal Snapshot Prepare(ComponentGroup group) =>
        Prepare(group, group.InternalPaths, static path => path.AsDrawnGeometry);

    internal Snapshot Prepare(IReadOnlyList<CanvasFrozenPathViewModel> paths) =>
        Prepare(paths, paths, static path => path.Path.AsDrawnGeometry);

    internal Snapshot Prepare(IReadOnlyList<AsDrawnGeometry?> sources) =>
        Prepare(sources, sources, static source => source);

    private Snapshot Prepare<T>(object owner, IReadOnlyList<T> paths, Func<T, AsDrawnGeometry?> getSource)
    {
        var snapshot = _snapshots.GetOrCreateValue(owner);
        bool changed = snapshot.Sources.Length != paths.Count;
        if (changed) snapshot.Sources = new AsDrawnGeometry?[paths.Count];
        for (int i = 0; i < snapshot.Sources.Length; i++)
        {
            var source = getSource(paths[i]);
            changed |= !ReferenceEquals(source, snapshot.Sources[i]);
            snapshot.Sources[i] = source;
        }
        if (!changed) return snapshot;

        foreach (var slot in snapshot.Slots.ToArray()) Retire(slot);
        snapshot.Seen.Clear();
        snapshot.Eligible = snapshot.Sources.Length >= MinimumPaths && snapshot.Sources.All(source => source is not null);
        if (!snapshot.Eligible) return snapshot;

        Rect? bounds = null;
        var layers = new HashSet<(int, int)>();
        foreach (var source in snapshot.Sources)
        {
            var (minX, minY, maxX, maxY) = source!.Bounds;
            var sourceBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            bounds = bounds?.Union(sourceBounds) ?? sourceBounds;
            foreach (var polygon in source.Polygons) layers.Add((polygon.Layer, polygon.DataType));
        }
        snapshot.Bounds = bounds!.Value;
        snapshot.Layers = layers.OrderBy(layer => layer.Item1).ThenBy(layer => layer.Item2).ToArray();
        return snapshot;
    }

    internal bool TryDraw(DrawingContext context, Snapshot snapshot, double zoom,
        GdsLayerVisibilityState? visibility, IBrush? highlightBrush = null)
    {
        if (_disabled || !snapshot.Eligible || !double.IsFinite(zoom) || zoom <= 0 || zoom >= OutlineRasterCache.VectorFromZoom
            || highlightBrush is not null and not ISolidColorBrush)
            return false;
        double density = Math.Pow(2, Math.Ceiling(Math.Log2(zoom)));
        double extent = Math.Max(snapshot.Bounds.Width, snapshot.Bounds.Height);
        double cappedDensity = Math.Pow(2, Math.Floor(Math.Log2((MaxEdge - 2.0) / extent)));
        if (density > cappedDensity)
        {
            // Avoid a vector cliff just above a density boundary. At most 1.25 screen
            // pixels per cached pixel are allowed; closer inspection still draws vectors.
            if (cappedDensity < zoom / 1.25) return false;
            density = cappedDensity;
        }
        // Include the outline stroke around the geometry instead of clipping it at the mask bounds.
        var bounds = snapshot.Bounds.Inflate(1.0 / density);
        double pixelWidth = Math.Ceiling(bounds.Width * density), pixelHeight = Math.Ceiling(bounds.Height * density);
        if (!double.IsFinite(pixelWidth) || !double.IsFinite(pixelHeight)
            || pixelWidth <= 0 || pixelHeight <= 0 || pixelWidth > MaxEdge || pixelHeight > MaxEdge) return false;
        int width = (int)pixelWidth, height = (int)pixelHeight;
        var key = new Key(density, VisibilityKey(snapshot.Layers, visibility), (highlightBrush as ISolidColorBrush)?.Color);
        var slot = snapshot.Slots.Find(candidate => candidate.Key == key);
        if (slot is null)
        {
            var sharper = snapshot.Slots.Where(candidate => candidate.Key.Density > density && candidate.Key.Visibility == key.Visibility && candidate.Key.Tint == key.Tint)
                .OrderBy(candidate => candidate.Key.Density).FirstOrDefault();
            bool canTintCached = highlightBrush is not null && snapshot.Slots.Any(s => s.Key == key with { Tint = null });
            if (sharper is null && !canTintCached && snapshot.Seen.Add(key)) return false;
            slot = Build(snapshot, key, bounds, width, height, visibility, sharper, highlightBrush);
            if (slot is null) return false;
        }
        Touch(slot);
        OutlineRasterCache.DrawImage(context, slot.Bitmap, slot.Bounds, null);
        IssuedImageCount++;
        return true;
    }

    private Slot? Build(Snapshot snapshot, Key key, Rect bounds, int width, int height,
        GdsLayerVisibilityState? visibility, Slot? sharper, IBrush? highlightBrush)
    {
        RenderTargetBitmap? bitmap = null;
        try
        {
            bitmap = new RenderTargetBitmap(new PixelSize(width, height));
            using (var context = bitmap.CreateDrawingContext())
            using (context.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y) * Matrix.CreateScale(key.Density, key.Density)))
            {
                var source = sharper ?? snapshot.Slots.Find(s => s.Key == key with { Tint = null });
                if (source is not null)
                    context.DrawImage(source.Bitmap, new Rect(0, 0, source.Bitmap.PixelSize.Width, source.Bitmap.PixelSize.Height), source.Bounds);
                else
                    foreach (var geometry in snapshot.Sources)
                        AsDrawnGeometryRenderer.Draw(context, geometry!, key.Density, visibility);
            }
            if (highlightBrush is not null && sharper is null)
            {
                var tinted = OutlineRasterCache.TintBitmap(bitmap, highlightBrush);
                bitmap.Dispose();
                bitmap = tinted;
            }
        }
        catch (Exception)
        {
            bitmap?.Dispose();
            _disabled = true;
            return null;
        }
        // Rounding up the bitmap dimensions adds a fractional world pixel at its right/bottom.
        var drawnBounds = new Rect(bounds.X, bounds.Y, width / key.Density, height / key.Density);
        var slot = new Slot(new WeakReference<Snapshot>(snapshot), key, bitmap, drawnBounds, (long)width * height);
        snapshot.Slots.Add(slot);
        slot.Node = _lru.AddLast(slot);
        _pixels += slot.Pixels;
        BitmapBuildCount++;
        while ((_pixels > PixelBudget || _lru.Count > MaximumEntries) && _lru.First is { } oldest && oldest != _lru.Last)
            Retire(oldest.Value);
        return slot;
    }

    private void Touch(Slot slot)
    {
        if (slot.Node is null || slot.Node == _lru.Last) return;
        _lru.Remove(slot.Node);
        _lru.AddLast(slot.Node);
    }

    private void Retire(Slot slot)
    {
        if (slot.Node is not null) _lru.Remove(slot.Node);
        slot.Node = null;
        if (slot.Owner.TryGetTarget(out var owner)) owner.Slots.Remove(slot);
        _pixels -= slot.Pixels;
        // Already recorded frames may still reference the image.
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(
            _ => Dispatcher.UIThread.Post(slot.Bitmap.Dispose, DispatcherPriority.Background), TaskScheduler.Default);
    }

    private static string VisibilityKey((int Layer, int DataType)[] layers, GdsLayerVisibilityState? visibility) =>
        visibility is null ? "" : string.Join(";", layers.Select(layer => visibility.EffectiveOpacity(layer.Layer, layer.DataType)
            .ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
}
