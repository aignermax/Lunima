namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Crossing support of <see cref="PathfindingGrid"/>: which routed waveguide's straight,
/// axis-aligned segment covers a point, and whether a cell belongs to a given waveguide.
/// A crossing-aware search asks this for every blocked cell it might jump, so the straight
/// segments are bucketed once per waveguide-geometry version instead of scanned per query.
/// </summary>
public partial class PathfindingGrid
{
    /// <summary>Bucket edge (grid cells) of the straight-segment index.</summary>
    private const int CrossingIndexBucketCells = 64;

    /// <summary>Largest coordinate deviation (µm) for a segment to count as axis-aligned.</summary>
    private const double AxisAlignmentToleranceMicrometers = 1e-6;

    private readonly Dictionary<Guid, double> _waveguideHalfWidths = new();
    private Dictionary<(int, int), List<StraightWaveguideSegment>>? _straightSegmentIndex;
    private int _straightSegmentIndexVersion = -1;

    /// <summary>One axis-aligned straight piece of a routed waveguide, with its corridor half width.</summary>
    /// <param name="Owner">The connection the segment belongs to.</param>
    /// <param name="Start">Segment start (µm).</param>
    /// <param name="End">Segment end (µm).</param>
    /// <param name="IsHorizontal">True for a segment along X.</param>
    /// <param name="HalfWidth">Half the blocked corridor width (µm).</param>
    public readonly record struct StraightWaveguideSegment(
        Guid Owner, (double X, double Y) Start, (double X, double Y) End, bool IsHorizontal, double HalfWidth)
    {
        /// <summary>The segment's coordinate range along its axis.</summary>
        public (double Min, double Max) AxisRange =>
            IsHorizontal ? (Math.Min(Start.X, End.X), Math.Max(Start.X, End.X))
                         : (Math.Min(Start.Y, End.Y), Math.Max(Start.Y, End.Y));

        /// <summary>The fixed coordinate across the axis (Y for a horizontal segment).</summary>
        public double CrossCoordinate => IsHorizontal ? Start.Y : Start.X;
    }

    /// <summary>
    /// The straight, axis-aligned waveguide segments whose blocked corridor covers
    /// (<paramref name="x"/>, <paramref name="y"/>) — usually none or one.
    /// </summary>
    public IReadOnlyList<StraightWaveguideSegment> StraightSegmentsAt(double x, double y)
    {
        var index = StraightSegmentIndex();
        if (!index.TryGetValue(BucketOf(x, y), out var bucket))
            return Array.Empty<StraightWaveguideSegment>();
        // A cell counts as blocked as soon as its square overlaps the corridor, so its
        // centre can sit up to half a cell diagonal (~0.71 cells) beyond the half width.
        double reach = CellSizeMicrometers;
        return bucket.Where(s => Covers(s, x, y, reach)).ToList();
    }

    /// <summary>True when grid cell (<paramref name="gridX"/>, <paramref name="gridY"/>) is blocked by <paramref name="owner"/>.</summary>
    public bool IsCellOfWaveguide(Guid owner, int gridX, int gridY)
    {
        lock (_waveguideCellsLock)
        {
            return _waveguideCells.TryGetValue(owner, out var cells) && cells.Contains((gridX, gridY));
        }
    }

    private static bool Covers(StraightWaveguideSegment segment, double x, double y, double reach)
    {
        var (min, max) = segment.AxisRange;
        double along = segment.IsHorizontal ? x : y;
        double across = segment.IsHorizontal ? y : x;
        return along >= min - reach && along <= max + reach
            && Math.Abs(across - segment.CrossCoordinate) <= segment.HalfWidth + reach;
    }

    private Dictionary<(int, int), List<StraightWaveguideSegment>> StraightSegmentIndex()
    {
        if (_straightSegmentIndex != null && _straightSegmentIndexVersion == WaveguideVersion)
            return _straightSegmentIndex;

        var index = new Dictionary<(int, int), List<StraightWaveguideSegment>>();
        foreach (var segment in CollectStraightSegments())
        {
            double pad = segment.HalfWidth + CellSizeMicrometers;
            double minX = Math.Min(segment.Start.X, segment.End.X) - pad, maxX = Math.Max(segment.Start.X, segment.End.X) + pad;
            double minY = Math.Min(segment.Start.Y, segment.End.Y) - pad, maxY = Math.Max(segment.Start.Y, segment.End.Y) + pad;
            var (bx1, by1) = BucketOf(minX, minY);
            var (bx2, by2) = BucketOf(maxX, maxY);
            for (int bx = bx1; bx <= bx2; bx++)
            for (int by = by1; by <= by2; by++)
            {
                if (!index.TryGetValue((bx, by), out var bucket))
                    index[(bx, by)] = bucket = new List<StraightWaveguideSegment>();
                bucket.Add(segment);
            }
        }
        _straightSegmentIndex = index;
        _straightSegmentIndexVersion = WaveguideVersion;
        return index;
    }

    private List<StraightWaveguideSegment> CollectStraightSegments()
    {
        var result = new List<StraightWaveguideSegment>();
        lock (_waveguideCellsLock)
        {
            foreach (var (owner, segments) in _waveguideGeometry)
            {
                double halfWidth = _waveguideHalfWidths.TryGetValue(owner, out var hw) ? hw : CellSizeMicrometers / 2;
                foreach (var segment in segments.OfType<StraightSegment>())
                {
                    bool horizontal = Math.Abs(segment.StartPoint.Y - segment.EndPoint.Y) <= AxisAlignmentToleranceMicrometers;
                    bool vertical = Math.Abs(segment.StartPoint.X - segment.EndPoint.X) <= AxisAlignmentToleranceMicrometers;
                    if (horizontal == vertical) continue; // diagonal, or a degenerate point
                    result.Add(new StraightWaveguideSegment(owner, segment.StartPoint, segment.EndPoint, horizontal, halfWidth));
                }
            }
        }
        return result;
    }

    private (int, int) BucketOf(double x, double y)
    {
        double bucketSize = CrossingIndexBucketCells * CellSizeMicrometers;
        return ((int)Math.Floor((x - MinX) / bucketSize), (int)Math.Floor((y - MinY) / bucketSize));
    }
}
