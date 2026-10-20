using Avalonia;
using Avalonia.Media;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using System.Runtime.CompilerServices;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>
/// Shares cell-local geometry across placements. Arrays are traversed as spatial
/// blocks, so even trillion-instance arrays cost only visible screen detail.
/// </summary>
internal sealed class GdsReviewHierarchyCache(GdsLayerGeometryScene scene)
{
    private static readonly string[] Palette = ["#36CFC9", "#819CF4", "#DBA5EC", "#D9B86C", "#86CBA0", "#DD8F87"];
    private static readonly ConditionalWeakTable<GdsReviewSceneBuilder, Dictionary<string, Cell>> SharedCells = new();
    private readonly Dictionary<string, Cell> _cells = SharedCells.GetValue(scene.ReviewSource!,
        _ => new Dictionary<string, Cell>(StringComparer.Ordinal));
    private readonly Dictionary<GdsLayerPair, IBrush> _brushes = scene.Layers
        .Select((l, i) => (l.Pair, Brush: (IBrush)new SolidColorBrush(Color.Parse(Palette[i % Palette.Length]))))
        .ToDictionary(x => x.Pair, x => x.Brush);
    private readonly GdsReviewSceneBuilder _source = scene.ReviewSource!;
    private sealed record Layer(GdsLayerPair Pair, Geometry Geometry, IReadOnlyList<GdsText> Texts);
    private sealed record Cell(GdsLayerGeometryScene Scene, IReadOnlyList<Layer> Layers, IReadOnlyList<GdsReference> References);

    private Cell GetCell(string name)
    {
        if (_cells.TryGetValue(name, out var cached)) return cached;
        var local = _source.BuildLocalScene(name);
        var layers = new List<Layer>();
        foreach (var layer in local.Layers)
        {
            var geometry = new StreamGeometry();
            using (var builder = geometry.Open())
            {
                // Same winding normalization as the main canvas batcher: overlapping
                // polygons of a layer union instead of cancelling each other.
                builder.SetFillRule(FillRule.NonZero);
                foreach (var polygon in layer.Polygons)
                {
                    var points = polygon.Points;
                    if (points.Count < 3) continue;
                    double area = 0;
                    for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                        area += points[j].X * points[i].Y - points[i].X * points[j].Y;
                    GdsPoint At(int i) => points[area < 0 ? points.Count - 1 - i : i];
                    var first = At(0);
                    builder.BeginFigure(new Point(first.X, first.Y), true);
                    for (int i = 1; i < points.Count; i++)
                    {
                        var p = At(i);
                        builder.LineTo(new Point(p.X, p.Y));
                    }
                    builder.EndFigure(true);
                }
            }
            layers.Add(new(layer.Pair, geometry, layer.Texts));
        }
        cached = new(local, layers, _source.GetReferences(name));
        _cells.Add(name, cached);
        return cached;
    }

    internal void Render(DrawingContext context, Matrix screen, Rect viewport, GdsLayerPair? highlight, string? highlightCell)
    {
        int blocks = 0;
        int cells = 0;
        var drawn = new HashSet<(string, GdsTransform)>();
        DrawCell(scene.CellName, GdsTransform.Identity);

        void DrawCell(string name, GdsTransform placement)
        {
            if (!drawn.Add((name, placement))) return;
            var matrix = MatrixFor(placement) * screen;
            var localBounds = _source.GetBounds(name);
            var bounds = RectFor(localBounds).TransformToAABB(matrix);
            if (!viewport.Intersects(bounds.Inflate(3))) return;
            if (++cells > 20_000 ||
                (localBounds.Width > 0 && localBounds.Height > 0 && Math.Max(bounds.Width, bounds.Height) < .75))
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
                    using var opacity = context.PushOpacity(highlight == layer.Pair ? .95 : highlight.HasValue ? .22 : .68);
                    var fill = _brushes[layer.Pair];
                    var pen = highlight == layer.Pair ? new Pen(Brushes.White, 1.5 / scale) : null;
                    context.DrawGeometry(fill, pen, layer.Geometry);
                    foreach (var text in layer.Texts)
                        context.DrawEllipse(fill, pen, new Point(text.Position.X, text.Position.Y), 3 / scale, 3 / scale);
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
            bool emphasized = highlight is { } selected && pairs.Contains(selected);
            using var opacity = context.PushOpacity(emphasized ? .95 : highlight.HasValue ? .22 : .68);
            if (pairs.Count > 0)
                context.FillRectangle(_brushes[emphasized ? highlight!.Value : pairs[0]], pixels);
            if (name == highlightCell)
                context.DrawRectangle(null, new Pen(Brushes.Yellow, 2), pixels);
        }
    }

    internal bool TryPick(double x, double y, double tolerance, out GdsLayerPair pair, out string? cellName,
        out IReadOnlyList<string> cellPath)
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
            if (!seen.Add((name, placement)) || ++visited > 20_000) return;
            var cell = GetCell(name);
            var determinant = placement.A * placement.E - placement.B * placement.D;
            if (Math.Abs(determinant) < 1e-30) return;
            path.Add(name);
            double dx = point.X - placement.C, dy = point.Y - placement.F;
            double localX = (placement.E * dx - placement.B * dy) / determinant;
            double localY = (-placement.D * dx + placement.A * dy) / determinant;
            var scale = Math.Sqrt(placement.A * placement.A + placement.D * placement.D);
            if (GdsLayerHitTester.TryPick(cell.Scene, localX, localY, tolerance / scale,
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
