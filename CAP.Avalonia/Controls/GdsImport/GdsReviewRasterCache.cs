using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>Rasterizes reusable spatial blocks at a scale at least as sharp as the current view.
/// Zooming beyond a bucket creates a sharper tile; large blocks descend to their spatial children.
/// Retention is bounded by both pixels and entry count, independently of source size.</summary>
internal sealed class GdsReviewRasterCache : IDisposable
{
    private const long PixelBudget = 32_000_000;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private long _pixels;
    private readonly record struct Key(GdsReviewSpatialGeometry.Node Node, double Scale, Color Color, bool Selected, int Detail);
    private sealed record Entry(Key Key, RenderTargetBitmap Bitmap, Rect Destination, long Pixels);

    internal (RenderTargetBitmap Bitmap, Rect Destination, int NewVertices) Get(
        GdsReviewSpatialGeometry.Node node, double scale, SolidColorBrush fill, bool selected, int detail,
        GdsReviewGeometryLru geometry)
    {
        double rasterScale = Math.Pow(2, Math.Ceiling(Math.Log2(scale)));
        var key = new Key(node, rasterScale, fill.Color, selected, detail);
        if (_entries.TryGetValue(key, out var found))
        {
            _recent.Remove(found); _recent.AddLast(found);
            return (found.Value.Bitmap, found.Value.Destination, 0);
        }
        int width = Math.Max(1, (int)Math.Ceiling(node.Bounds.Width * rasterScale)) + 4;
        int height = Math.Max(1, (int)Math.Ceiling(node.Bounds.Height * rasterScale)) + 4;
        long pixels = (long)width * height;
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        var shape = geometry.Get(node, detail == 1 ? 512 : 2048);
        using (var drawing = bitmap.CreateDrawingContext())
        using (drawing.PushTransform(new Matrix(rasterScale, 0, 0, rasterScale,
            2 - node.Bounds.X * rasterScale, 2 - node.Bounds.Y * rasterScale)))
            drawing.DrawGeometry(fill, selected ? new Pen(Brushes.White, 1 / rasterScale) : null, shape.Geometry);
        var destination = new Rect(node.Bounds.X - 2 / rasterScale, node.Bounds.Y - 2 / rasterScale,
            width / rasterScale, height / rasterScale);
        while (_recent.First is { } first && (_entries.Count >= 2048 || _pixels + pixels > PixelBudget))
        {
            _entries.Remove(first.Value.Key); _pixels -= first.Value.Pixels;
            _recent.RemoveFirst(); first.Value.Bitmap.Dispose();
        }
        var entry = new Entry(key, bitmap, destination, pixels);
        _entries.Add(key, _recent.AddLast(entry)); _pixels += pixels;
        return (bitmap, destination, shape.Vertices);
    }
    public void Dispose()
    {
        foreach (var entry in _recent) entry.Bitmap.Dispose();
        _recent.Clear(); _entries.Clear(); _pixels = 0;
    }
}
