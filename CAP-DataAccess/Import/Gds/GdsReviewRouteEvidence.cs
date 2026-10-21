namespace CAP_DataAccess.Import.Gds;

/// <summary>Advisory shape evidence only; geometry cannot establish optical/electrical material.</summary>
public sealed record GdsReviewRouteSuggestion(int Layer, int Datatype, int ShapeCount, int CellCount);

public static class GdsReviewRouteEvidence
{
    public static IReadOnlyList<GdsReviewRouteSuggestion> Suggest(GdsLibrary library, string topCell,
        CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(topCell);
        var shapes = new Dictionary<(int Layer, int Datatype), int>();
        var cells = new Dictionary<(int Layer, int Datatype), HashSet<string>>();
        while (pending.TryPop(out var name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(name) || !library.Cells.TryGetValue(name, out var cell)) continue;
            foreach (var element in cell.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (element)
                {
                    case GdsReference reference:
                        pending.Push(reference.CellName);
                        break;
                    case GdsPath path when path.Points.Count >= 2 && path.WidthMicrometers > 0
                        && double.IsFinite(path.WidthMicrometers):
                        Add(path.Layer, path.DataType);
                        break;
                    case GdsPolygon polygon when IsThin(polygon, cancellationToken):
                        Add(polygon.Layer, polygon.DataType);
                        break;
                }
            }
            void Add(int layer, int datatype)
            {
                var pair = (layer, datatype);
                shapes[pair] = shapes.GetValueOrDefault(pair) + 1;
                if (!cells.TryGetValue(pair, out var owners)) cells[pair] = owners = new(StringComparer.Ordinal);
                owners.Add(name);
            }
        }
        return shapes.OrderBy(x => x.Key.Layer).ThenBy(x => x.Key.Datatype)
            .Select(x => new GdsReviewRouteSuggestion(x.Key.Layer, x.Key.Datatype, x.Value, cells[x.Key].Count)).ToArray();
    }

    private static bool IsThin(GdsPolygon polygon, CancellationToken token)
    {
        if (polygon.Points.Count < 3 || GdsReviewPortEvidence.TryArrow(polygon, out _, out _)) return false;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var point in polygon.Points)
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
            minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
        }
        var shortSide = Math.Min(maxX - minX, maxY - minY);
        return shortSide > 0 && Math.Max(maxX - minX, maxY - minY) / shortSide >= 8;
    }
}
