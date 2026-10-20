using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using System.Runtime.CompilerServices;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>
/// Shares cell-local geometry across placements. Arrays are traversed as spatial
/// blocks, so even trillion-instance arrays cost only visible screen detail.
/// </summary>
internal sealed class GdsReviewHierarchyCache(GdsLayerGeometryScene scene) : IDisposable
{
    private static readonly string[] Palette = ["#36CFC9", "#819CF4", "#DBA5EC", "#D9B86C", "#86CBA0", "#DD8F87"];
    private static readonly ConditionalWeakTable<GdsReviewSceneBuilder, Dictionary<string, Cell>> SharedCells = new();
    private readonly Dictionary<string, Cell> _cells = SharedCells.GetValue(scene.ReviewSource!,
        _ => new Dictionary<string, Cell>(StringComparer.Ordinal));
    private readonly Dictionary<GdsLayerPair, IBrush> _brushes = scene.Layers
        .Select((l, i) => (l.Pair, Brush: (IBrush)new SolidColorBrush(Color.Parse(Palette[i % Palette.Length]))))
        .ToDictionary(x => x.Pair, x => x.Brush);
    private readonly GdsReviewSceneBuilder _source = scene.ReviewSource!;
    private sealed record Layer(GdsLayerPair Pair, GdsReviewSpatialGeometry Geometry, IReadOnlyList<GdsText> Texts);
    private static readonly ConditionalWeakTable<GdsReviewSceneBuilder, GdsReviewGeometryLru> SharedGeometry = new();
    private readonly GdsReviewGeometryLru _geometry = SharedGeometry.GetValue(scene.ReviewSource!, _ => new());
    private readonly GdsReviewRasterCache _raster = new();
    internal GdsReviewRenderStats LastRenderStats { get; private set; }
    private sealed record Cell(GdsLayerGeometryScene Scene, IReadOnlyList<Layer> Layers, IReadOnlyList<GdsReference> References);

    private readonly Dictionary<(double Scale, GdsLayerPair? Layer, string? Cell, bool Isolate, int Detail), (RenderTargetBitmap Image, Rect Destination)> _overviews = new();
    public void Dispose()
    {
        _raster.Dispose();
        foreach (var tile in _overviews.Values) tile.Image.Dispose();
        _overviews.Clear();
    }

    private Cell GetCell(string name)
    {
        if (_cells.TryGetValue(name, out var cached)) return cached;
        var local = _source.BuildLocalScene(name);
        var layers = new List<Layer>();
        foreach (var layer in local.Layers)
        {
            layers.Add(new(layer.Pair, new GdsReviewSpatialGeometry(layer.Polygons), layer.Texts));
        }
        cached = new(local, layers, _source.GetReferences(name));
        _cells.Add(name, cached);
        return cached;
    }

    internal void Render(DrawingContext context, Matrix screen, Rect viewport, GdsLayerPair? highlight, string? highlightCell, bool isolateSelectedLayer = true, int detailLevel = 0, bool allowOverview = true)
    {
        double viewScale = Math.Max(1e-12, Math.Sqrt(screen.M11 * screen.M11 + screen.M12 * screen.M12));
        if (allowOverview && detailLevel != 2 && Math.Max(scene.Bounds.Width, scene.Bounds.Height) * viewScale <= 1024)
        {
            double rasterScale = Math.Pow(2, Math.Ceiling(Math.Log2(viewScale)));
            var key = (rasterScale, highlight, highlightCell, isolateSelectedLayer, detailLevel);
            if (!_overviews.TryGetValue(key, out var tile))
            {
                int width = Math.Max(1, (int)Math.Ceiling(scene.Bounds.Width * rasterScale)) + 8;
                int height = Math.Max(1, (int)Math.Ceiling(scene.Bounds.Height * rasterScale)) + 8;
                var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                using (var drawing = image.CreateDrawingContext())
                    Render(drawing, new Matrix(rasterScale, 0, 0, rasterScale,
                        4 - scene.Bounds.MinX * rasterScale, 4 - scene.Bounds.MinY * rasterScale),
                        new Rect(0, 0, width, height), highlight, highlightCell, isolateSelectedLayer, detailLevel, false);
                tile = (image, new Rect(scene.Bounds.MinX - 4 / rasterScale, scene.Bounds.MinY - 4 / rasterScale,
                    width / rasterScale, height / rasterScale));
                if (_overviews.Count >= 2)
                {
                    var oldest = _overviews.First(); oldest.Value.Image.Dispose(); _overviews.Remove(oldest.Key);
                }
                _overviews.Add(key, tile);
            }
            else LastRenderStats = new(0, 0, 1, 0);
            using (context.PushTransform(screen)) context.DrawImage(tile.Image, tile.Destination);
            return;
        }
        int vertices = 0, polygons = 0, proxies = 0, culled = 0;
        int blocks = 0;
        int cells = 0;
        var drawn = new HashSet<(string, GdsTransform)>();
        DrawCell(scene.CellName, GdsTransform.Identity);
        LastRenderStats = new(vertices, polygons, proxies, culled);

        void DrawCell(string name, GdsTransform placement)
        {
            if (isolateSelectedLayer && highlight is { } only && !_source.GetLayerPairs(name).Contains(only)) return;
            if (!drawn.Add((name, placement))) return;
            var matrix = MatrixFor(placement) * screen;
            var localBounds = _source.GetBounds(name);
            var bounds = RectFor(localBounds).TransformToAABB(matrix);
            if (!viewport.Intersects(bounds.Inflate(3))) return;
            if (++cells > 20_000 ||
                (localBounds.Width > 0 && localBounds.Height > 0 && Math.Max(bounds.Width, bounds.Height) < (detailLevel == 2 ? .75 : detailLevel == 1 ? 12 : 4)))
            {
                DrawProxy(bounds, name);
                return;
            }
            var cell = GetCell(name);
            using (context.PushTransform(matrix))
            {
                double scale = Math.Max(1e-12, Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12));
                foreach (var layer in cell.Layers)
                {
                    if (isolateSelectedLayer && highlight is { } onlyLayer && layer.Pair != onlyLayer) continue;
                    using var opacity = context.PushOpacity(highlight == layer.Pair ? 1 : highlight.HasValue ? GdsReviewSelectionStyle.ContextOpacity : .68);
                    var fill = highlight == layer.Pair ? GdsReviewSelectionStyle.Selected : highlight.HasValue ? GdsReviewSelectionStyle.Context : _brushes[layer.Pair];
                    var pen = highlight == layer.Pair ? new Pen(Brushes.White, 1 / scale) : null;
                    DrawNode(layer.Geometry.Root);
                    foreach (var text in layer.Texts)
                    {
                        var anchor = new Point(text.Position.X, text.Position.Y);
                        if (viewport.Inflate(3).Contains(matrix.Transform(anchor)))
                            context.DrawRectangle(null, new Pen(fill, 1 / scale), new Rect(anchor.X - 3 / scale, anchor.Y - 3 / scale, 6 / scale, 6 / scale));
                    }

                    void DrawNode(GdsReviewSpatialGeometry.Node? node)
                    {
                        if (node is null) return;
                        var pixels = node.Bounds.TransformToAABB(matrix);
                        if (!viewport.Intersects(pixels.Inflate(2))) { culled++; return; }
                        double extent = Math.Max(pixels.Width, pixels.Height);
                        if (detailLevel != 2 && extent <= (detailLevel == 1 ? 6 : 2))
                        {
                            proxies++;
                            context.DrawRectangle(fill, null, node.Bounds);
                            return;
                        }
                        if (detailLevel != 2 && node.PolygonCount > 32 && extent <= 256)
                        {
                            var tile = _raster.Get(node, scale, (SolidColorBrush)fill, highlight == layer.Pair, detailLevel, _geometry);
                            vertices += tile.NewVertices; polygons += node.PolygonCount;
                            context.DrawImage(tile.Bitmap, tile.Destination);
                            return;
                        }
                        if (node.Polygon is null && (node.PolygonCount > 32 || !viewport.Contains(pixels))) { DrawNode(node.Left); DrawNode(node.Right); return; }
                        int resolution = detailLevel == 2 || extent > (detailLevel == 1 ? 4096 : 1024) ? 0 : extent <= 256 ? 512 : 2048;
                        var geometry = _geometry.Get(node, resolution);
                        vertices += geometry.Vertices; polygons += node.PolygonCount;
                        context.DrawGeometry(fill, pen, geometry.Geometry);
                    }
                }
            }
            foreach (var reference in cell.References)
                DrawArray(reference, placement, 0, EffectiveColumns(reference), 0, EffectiveRows(reference));
            if (name == highlightCell)
            {
                using var transform = context.PushTransform(matrix);
                double scale = Math.Max(1e-12, Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12));
                context.DrawRectangle(null, new Pen(Brushes.Yellow, 2 / scale), RectFor(localBounds));
            }
        }

        void DrawArray(GdsReference reference, GdsTransform parent, int c0, int c1, int r0, int r1)
        {
            if (isolateSelectedLayer && highlight is { } only && !_source.GetLayerPairs(reference.CellName).Contains(only)) return;
            var block = ArrayBounds(_source.GetBounds(reference.CellName), reference, parent, c0, c1, r0, r1);
            var pixels = RectFor(block).TransformToAABB(screen);
            if (!viewport.Intersects(pixels.Inflate(3))) return;
            if (c1 - c0 == 1 && r1 - r0 == 1)
            {
                DrawCell(reference.CellName, GdsTransform.FromReference(reference, c0, r0).Then(parent));
                return;
            }
            // Aggregate only subpixel blocks, or a bounded number of visible
            // blocks in pathological dense arrays. No source detail is discarded:
            // a zoomed viewport subdivides these same blocks again.
            if (Math.Max(pixels.Width, pixels.Height) <= 1 || ++blocks > 20_000)
            {
                DrawProxy(pixels, reference.CellName);
                return;
            }
            Split(c0, c1, r0, r1, (a, b, c, d) => DrawArray(reference, parent, a, b, c, d));
        }

        void DrawProxy(Rect pixels, string name)
        {
            if (pixels.Width < 1 || pixels.Height < 1)
                pixels = new Rect(pixels.Center.X - Math.Max(1, pixels.Width) / 2,
                    pixels.Center.Y - Math.Max(1, pixels.Height) / 2,
                    Math.Max(1, pixels.Width), Math.Max(1, pixels.Height));
            var pairs = _source.GetLayerPairs(name);
            if (isolateSelectedLayer && highlight is { } only && !pairs.Contains(only)) return;
            proxies++;
            bool emphasized = highlight is { } selected && pairs.Contains(selected);
            using var opacity = context.PushOpacity(emphasized ? 1 : highlight.HasValue ? GdsReviewSelectionStyle.ContextOpacity : .68);
            if (pairs.Count > 0)
                context.FillRectangle(emphasized ? GdsReviewSelectionStyle.Selected : highlight.HasValue ? GdsReviewSelectionStyle.Context : _brushes[pairs[0]], pixels);
            if (emphasized)
                context.DrawRectangle(null, new Pen(Brushes.White, 1), pixels);
            if (name == highlightCell)
                context.DrawRectangle(null, new Pen(Brushes.Yellow, 2), pixels);
        }
    }

    internal bool TryPick(double x, double y, double tolerance, out GdsLayerPair pair, out string? cellName,
        out IReadOnlyList<string> cellPath, GdsLayerPair? onlyLayer = null)
    {
        var point = new GdsPoint(x, y);
        GdsLayerPair result = default;
        string? selectedCell = null;
        IReadOnlyList<string> selectedPath = [];
        var path = new List<string>();
        double bestArea = double.PositiveInfinity, bestTextDistance = double.PositiveInfinity;
        var seen = new HashSet<(string, GdsTransform)>();
        int visited = 0;
        PickCell(scene.CellName, GdsTransform.Identity);
        pair = result;
        cellName = selectedCell;
        cellPath = selectedPath;
        return selectedCell is not null;

        void PickCell(string name, GdsTransform placement)
        {
            if (onlyLayer is { } only && !_source.GetLayerPairs(name).Contains(only)) return;
            if (!seen.Add((name, placement)) || ++visited > 20_000) return;
            var cell = GetCell(name);
            var determinant = placement.A * placement.E - placement.B * placement.D;
            if (Math.Abs(determinant) < 1e-30) return;
            path.Add(name);
            double dx = point.X - placement.C, dy = point.Y - placement.F;
            double localX = (placement.E * dx - placement.B * dy) / determinant;
            double localY = (-placement.D * dx + placement.A * dy) / determinant;
            var scale = Math.Sqrt(placement.A * placement.A + placement.D * placement.D);
            var pickScene = onlyLayer is { } filter ? cell.Scene with { Layers = cell.Scene.Layers.Where(l => l.Pair == filter).ToArray() } : cell.Scene;
            if (GdsLayerHitTester.TryPick(pickScene, localX, localY, tolerance / scale,
                out var candidate, out double area, out double textDistance))
            {
                area *= Math.Abs(determinant);
                textDistance *= Math.Abs(determinant);
                if (textDistance < bestTextDistance ||
                    (double.IsPositiveInfinity(bestTextDistance) && area < bestArea))
                {
                    result = candidate;
                    selectedCell = name;
                    selectedPath = path.ToArray();
                    bestArea = area;
                    bestTextDistance = textDistance;
                }
            }
            foreach (var reference in cell.References)
                PickArray(reference, placement, 0, EffectiveColumns(reference), 0, EffectiveRows(reference));
            path.RemoveAt(path.Count - 1);
        }

        void PickArray(GdsReference reference, GdsTransform parent, int c0, int c1, int r0, int r1)
        {
            if (onlyLayer is { } only && !_source.GetLayerPairs(reference.CellName).Contains(only)) return;
            if (++visited > 20_000) return;
            var bounds = ArrayBounds(_source.GetBounds(reference.CellName), reference, parent, c0, c1, r0, r1);
            if (x < bounds.MinX - tolerance || x > bounds.MaxX + tolerance ||
                y < bounds.MinY - tolerance || y > bounds.MaxY + tolerance) return;
            if (c1 - c0 == 1 && r1 - r0 == 1)
            {
                PickCell(reference.CellName, GdsTransform.FromReference(reference, c0, r0).Then(parent));
                return;
            }
            Split(c0, c1, r0, r1, (a, b, c, d) => PickArray(reference, parent, a, b, c, d));
        }
    }

    private static int EffectiveColumns(GdsReference r) => r.ColumnSpacingMicrometers == 0 ? 1 : r.Columns;
    private static int EffectiveRows(GdsReference r) => r.RowSpacingMicrometers == 0 ? 1 : r.Rows;

    private static void Split(int c0, int c1, int r0, int r1, Action<int, int, int, int> visit)
    {
        if (c1 - c0 >= r1 - r0)
        {
            int mid = c0 + (c1 - c0) / 2;
            visit(c0, mid, r0, r1);
            visit(mid, c1, r0, r1);
        }
        else
        {
            int mid = r0 + (r1 - r0) / 2;
            visit(c0, c1, r0, mid);
            visit(c0, c1, mid, r1);
        }
    }

    private static GdsBoundingBox ArrayBounds(GdsBoundingBox child, GdsReference reference, GdsTransform parent,
        int c0, int c1, int r0, int r1)
    {
        GdsBoundingBox? bounds = null;
        foreach (int column in new[] { c0, c1 - 1 })
            foreach (int row in new[] { r0, r1 - 1 })
            {
                var matrix = MatrixFor(GdsTransform.FromReference(reference, column, row).Then(parent));
                var rect = RectFor(child).TransformToAABB(matrix);
                var box = new GdsBoundingBox(rect.Left, rect.Top, rect.Right, rect.Bottom);
                bounds = bounds is { } existing ? existing.Union(box) : box;
            }
        return bounds!.Value;
    }

    private static Rect RectFor(GdsBoundingBox b) => new(b.MinX, b.MinY, b.Width, b.Height);
    private static Matrix MatrixFor(GdsTransform t) => new(t.A, t.D, t.B, t.E, t.C, t.F);
}
