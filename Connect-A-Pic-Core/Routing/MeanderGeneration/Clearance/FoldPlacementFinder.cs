namespace CAP_Core.Routing.MeanderGeneration.Clearance;

/// <summary>
/// Finds where along a straight a fold has room. In the straight's local frame (t along it,
/// v across it) the fold occupies a box; every neighbouring route chord and footprint that
/// reaches into the band the fold sweeps blocks an interval of start offsets. The complement
/// is where the fold fits — including the gap inside a neighbour's own meander, which is
/// exactly where a slightly longer parallel route can nest its loop. Positions closest to the
/// preferred one come first. Conservative (the fold's bounding box, not its curves), so every
/// position still goes through the exact collision check.
/// </summary>
internal static class FoldPlacementFinder
{
    private const double Eps = 1e-9;
    private const int MaxPositions = 6;

    /// <summary>
    /// Start offsets (µm from the straight's start) at which the fold fits, best first.
    /// </summary>
    /// <param name="host">The straight that hosts the fold.</param>
    /// <param name="foldGeometry">The fold emitted at offset 0 on this host (world coordinates).</param>
    /// <param name="footprint">Length of host the fold replaces.</param>
    /// <param name="preferredOffset">Offset to stay closest to (e.g. centred).</param>
    /// <param name="obstacles">What the fold must keep clear of.</param>
    public static IReadOnlyList<double> FreeOffsets(PathSegment host, RoutedPath foldGeometry, double footprint,
        double preferredOffset, MeanderObstacles obstacles)
    {
        var frame = new LocalFrame(host);
        double room = frame.Length - footprint;
        if (room < -Eps)
            return [];
        var fold = frame.Extent(ClearanceGeometry.Sample(foldGeometry));
        double spacing = Math.Max(obstacles.MinSpacingMicrometers, obstacles.ContactClearanceMicrometers);
        var (vLow, vHigh) = Band(fold, spacing, obstacles.ContactClearanceMicrometers);

        var blocked = new List<(double From, double To)>();
        var segments = new List<ClearanceIndex.IndexedSegment>();
        var footprints = new List<MeanderBounds>();
        var box = frame.WorldBox(-spacing, frame.Length + spacing, vLow, vHigh);
        obstacles.Index.Query(box.MinX, box.MinY, box.MaxX, box.MaxY, obstacles.IgnoredOwners, segments, footprints);
        foreach (var segment in segments)
            Block(blocked, frame.ToLocal([(segment.Ax, segment.Ay), (segment.Bx, segment.By)]), vLow, vHigh, fold, spacing);
        foreach (var rect in footprints)
            Block(blocked, frame.ToLocal(Corners(rect)), vLow, vHigh, fold, 0.0);

        return Order(FreeIntervals(blocked, Math.Max(0.0, room)), Math.Clamp(preferredOffset, 0.0, Math.Max(0.0, room)));
    }

    /// <summary>The swept band across the host: the fold's side, inflated by the spacing, minus the host line itself.</summary>
    private static (double Low, double High) Band((double TMin, double TMax, double VMin, double VMax) fold,
        double spacing, double contact)
    {
        double margin = Math.Max(contact, Eps);
        return fold.VMax >= -fold.VMin
            ? (margin, Math.Max(fold.VMax, margin) + spacing)
            : (Math.Min(fold.VMin, -margin) - spacing, -margin);
    }

    private static void Block(List<(double From, double To)> blocked, IReadOnlyList<(double T, double V)> shape,
        double vLow, double vHigh, (double TMin, double TMax, double VMin, double VMax) fold, double spacing)
    {
        if (!ClearanceGeometry.BandExtent(shape, vLow, vHigh, out double ta, out double tb))
            return;
        blocked.Add((ta - fold.TMax - spacing, tb - fold.TMin + spacing));
    }

    private static List<(double From, double To)> FreeIntervals(List<(double From, double To)> blocked, double room)
    {
        blocked.Sort((a, b) => a.From.CompareTo(b.From));
        var free = new List<(double From, double To)>();
        double cursor = 0.0;
        foreach (var (from, to) in blocked)
        {
            if (from > cursor + Eps && cursor <= room)
                free.Add((cursor, Math.Min(from, room)));
            cursor = Math.Max(cursor, to);
        }
        if (cursor <= room + Eps)
            free.Add((Math.Min(cursor, room), room));
        return free.Where(f => f.To >= f.From - Eps).ToList();
    }

    private static IReadOnlyList<double> Order(List<(double From, double To)> free, double preferred) => free
        .Select(f => Math.Clamp(preferred, f.From, Math.Max(f.From, f.To)))
        .OrderBy(offset => Math.Abs(offset - preferred))
        .Take(MaxPositions)
        .ToList();

    private static (double X, double Y)[] Corners(MeanderBounds rect) =>
    [
        (rect.MinX, rect.MinY), (rect.MaxX, rect.MinY), (rect.MaxX, rect.MaxY), (rect.MinX, rect.MaxY),
    ];

    /// <summary>Host-aligned coordinates: t along the straight from its start, v to its left.</summary>
    private sealed class LocalFrame
    {
        private readonly double _ox, _oy, _ux, _uy;

        public LocalFrame(PathSegment host)
        {
            (_ox, _oy) = host.StartPoint;
            double dx = host.EndPoint.X - _ox;
            double dy = host.EndPoint.Y - _oy;
            Length = Math.Sqrt(dx * dx + dy * dy);
            (_ux, _uy) = Length > Eps ? (dx / Length, dy / Length) : (1.0, 0.0);
        }

        public double Length { get; }

        public IReadOnlyList<(double T, double V)> ToLocal(IReadOnlyList<(double X, double Y)> points) =>
            points.Select(p => ((p.X - _ox) * _ux + (p.Y - _oy) * _uy, -(p.X - _ox) * _uy + (p.Y - _oy) * _ux)).ToList();

        public (double TMin, double TMax, double VMin, double VMax) Extent(IReadOnlyList<(double X, double Y)> points)
        {
            var local = ToLocal(points);
            return (local.Min(p => p.T), local.Max(p => p.T), local.Min(p => p.V), local.Max(p => p.V));
        }

        public MeanderBounds WorldBox(double tMin, double tMax, double vMin, double vMax)
        {
            var corners = new[] { (tMin, vMin), (tMax, vMin), (tMax, vMax), (tMin, vMax) }
                .Select(c => (X: _ox + c.Item1 * _ux - c.Item2 * _uy, Y: _oy + c.Item1 * _uy + c.Item2 * _ux))
                .ToList();
            return new MeanderBounds(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y));
        }
    }
}
