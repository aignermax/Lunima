using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
/// Above <see cref="MaxBitmapEdgePx"/> (zoomed in) the caller draws vectors as before.
/// </summary>
internal sealed class OutlineRasterCache
{
    /// <summary>Largest bitmap edge (px) the cache creates; beyond it vectors are drawn.</summary>
    public const int MaxBitmapEdgePx = 8192;

    /// <summary>Zoom from which on vectors are always drawn (sharp edges once polygons are large).</summary>
    public const double VectorFromZoom = 0.75;

    /// <summary>Densities kept per outline list; older ones are dropped.</summary>
    private const int MaxLevelsPerOutline = 2;

    private readonly ConditionalWeakTable<IReadOnlyList<OutlinePolygon>, Entry> _entries = new();

    /// <summary>
    /// Draws the cached bitmap of <paramref name="batches"/> in the current (component-local)
    /// transform, creating it when needed. Returns false when the zoom asks for vectors or
    /// the bitmap would be too large — the caller then draws the batches itself.
    /// </summary>
    /// <param name="context">Drawing context with the component-local transform pushed.</param>
    /// <param name="outlines">The outline list (cache key, shared by all instances of a template).</param>
    /// <param name="batches">The batched geometry of <paramref name="outlines"/>.</param>
    /// <param name="zoom">Current canvas zoom (screen px per µm).</param>
    /// <param name="layerVisibility">Layer view filter baked into the bitmap; part of the cache key.</param>
    public bool TryDraw(
        DrawingContext context,
        IReadOnlyList<OutlinePolygon> outlines,
        OutlineBatch[] batches,
        double zoom,
        GdsLayerVisibilityState? layerVisibility)
    {
        if (batches.Length == 0 || zoom >= VectorFromZoom || zoom <= 0) return false;
        var bounds = Union(batches);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        double density = Math.Pow(2, Math.Ceiling(Math.Log2(zoom)));
        int width = (int)Math.Ceiling(bounds.Width * density);
        int height = (int)Math.Ceiling(bounds.Height * density);
        if (width > MaxBitmapEdgePx || height > MaxBitmapEdgePx) return false;

        var entry = _entries.GetValue(outlines, _ => new Entry());
        var key = (density, VisibilityKey(batches, layerVisibility));
        var bitmap = entry.Get(key) ?? entry.Put(key, Render(batches, bounds, density, width, height, layerVisibility));
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
            new Rect(bounds.X, bounds.Y, width / density, height / density));
        return true;
    }

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

    /// <summary>Hash of the opacities the bitmap was rendered with, so a layer toggle re-renders it.</summary>
    private static int VisibilityKey(OutlineBatch[] batches, GdsLayerVisibilityState? layerVisibility)
    {
        if (layerVisibility is null) return 0;
        var hash = new HashCode();
        foreach (var batch in batches)
            hash.Add(layerVisibility.EffectiveOpacity(batch.Layer, batch.DataType));
        return hash.ToHashCode();
    }

    private static Rect Union(OutlineBatch[] batches)
    {
        var rect = batches[0].Bounds;
        for (int i = 1; i < batches.Length; i++)
            rect = rect.Union(batches[i].Bounds);
        return rect;
    }

    /// <summary>The bitmaps of one outline list, most recently used last.</summary>
    private sealed class Entry
    {
        private readonly List<((double Density, int Visibility) Key, RenderTargetBitmap Bitmap)> _levels = new();

        public RenderTargetBitmap? Get((double, int) key)
        {
            int index = _levels.FindIndex(l => l.Key == key);
            if (index < 0) return null;
            var hit = _levels[index];
            _levels.RemoveAt(index);
            _levels.Add(hit);
            return hit.Bitmap;
        }

        public RenderTargetBitmap Put((double, int) key, RenderTargetBitmap bitmap)
        {
            _levels.Add((key, bitmap));
            // Evicted bitmaps are NOT disposed: an already recorded frame may still draw
            // them on the compositor thread; the finalizer releases them safely.
            while (_levels.Count > MaxLevelsPerOutline)
                _levels.RemoveAt(0);
            return bitmap;
        }
    }
}
