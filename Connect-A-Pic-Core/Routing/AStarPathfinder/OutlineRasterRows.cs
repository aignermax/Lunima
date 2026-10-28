using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>Compact scan-converted polygon union; stores horizontal runs, not one object per cell.</summary>
internal sealed class OutlineRasterRows
{
    private readonly Dictionary<int, List<(int Start, int End)>> _rows = new();

    // Build is the only writer. Dictionary/list capacity is included conservatively,
    // so sharing completed rows does not hide retained backing-array allocations.
    internal long RetainedBytes => 64L + _rows.EnsureCapacity(0) * 64L
        + _rows.Values.Sum(spans => 64L + spans.Capacity * 8L);

    public IEnumerable<(int X, int Y)> Cells()
    {
        foreach (var (y, spans) in _rows)
            foreach (var (start, end) in spans)
                for (int x = start; x <= end; x++) yield return (x, y);
    }

    public bool Contains(int x, int y)
    {
        if (!_rows.TryGetValue(y, out var spans)) return false;
        int low = 0, high = spans.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (x < spans[mid].Start) high = mid - 1;
            else if (x > spans[mid].End) low = mid + 1;
            else return true;
        }
        return false;
    }

    public static OutlineRasterRows Build(IEnumerable<OutlinePoint[]> polygons, PathfindingGrid grid, double padding)
    {
        var result = new OutlineRasterRows();
        foreach (var polygon in polygons)
        {
            var intersections = new Dictionary<int, List<double>>();
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[j]; var b = polygon[i];
                // Rasterize each edge's expanded band. Restrict X to the portion crossing
                // this row, so a long diagonal costs rows rather than its entire bounding box.
                int first = Math.Max(0, (int)Math.Floor((Math.Min(a.Y, b.Y) - padding - grid.MinY) / grid.CellSizeMicrometers));
                int last = Math.Min(grid.Height - 1, (int)Math.Floor((Math.Max(a.Y, b.Y) + padding - grid.MinY) / grid.CellSizeMicrometers));
                for (int y = first; y <= last; y++)
                {
                    double lower = grid.MinY + y * grid.CellSizeMicrometers - padding;
                    double upper = lower + grid.CellSizeMicrometers + 2 * padding;
                    double t1 = a.Y == b.Y ? 0 : Math.Clamp((lower - a.Y) / (b.Y - a.Y), 0, 1);
                    double t2 = a.Y == b.Y ? 1 : Math.Clamp((upper - a.Y) / (b.Y - a.Y), 0, 1);
                    double x1 = a.X + t1 * (b.X - a.X), x2 = a.X + t2 * (b.X - a.X);
                    result.Add(y, Math.Min(x1, x2) - padding, Math.Max(x1, x2) + padding, grid);
                    double centerY = grid.MinY + (y + 0.5) * grid.CellSizeMicrometers;
                    if ((a.Y > centerY) != (b.Y > centerY))
                    {
                        if (!intersections.TryGetValue(y, out var xs)) intersections[y] = xs = new();
                        xs.Add(a.X + (centerY - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                    }
                }
            }
            foreach (var (y, xs) in intersections)
            {
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2) result.Add(y, xs[i], xs[i + 1], grid);
            }
        }
        result.Merge();
        return result;
    }

    private void Add(int y, double x1, double x2, PathfindingGrid grid)
    {
        int first = Math.Max(0, (int)Math.Floor((x1 - grid.MinX) / grid.CellSizeMicrometers));
        int last = Math.Min(grid.Width - 1, (int)Math.Floor((x2 - grid.MinX) / grid.CellSizeMicrometers));
        if (first > last) return;
        if (!_rows.TryGetValue(y, out var spans)) _rows[y] = spans = new();
        spans.Add((first, last));
    }

    private void Merge()
    {
        foreach (var spans in _rows.Values)
        {
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            int write = 0;
            for (int read = 1; read < spans.Count; read++)
            {
                if (spans[read].Start <= spans[write].End + 1)
                    spans[write] = (spans[write].Start, Math.Max(spans[write].End, spans[read].End));
                else spans[++write] = spans[read];
            }
            spans.RemoveRange(write + 1, spans.Count - write - 1);
            // The merged union is the immutable result. Do not retain the much larger
            // backing storage used while every polygon edge contributed candidate spans.
            spans.TrimExcess();
        }
    }
}
