using CAP_DataAccess.Import.Gds;

namespace UnitTests.Import.Gds;

/// <summary>
/// The pre-grid sequential detector, kept verbatim as the semantic
/// reference: the production detector's spatial pruning must reproduce its
/// pins — values and ordering — exactly.
/// </summary>
internal static class SequentialReferenceDetector
{
    private enum CellEdge
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
    }

    private readonly record struct Candidate(CellEdge Edge, DetectedPin Pin);

    private sealed record AnchorGeometry(
        double SegmentDistanceSquared,
        GdsPolygon Polygon,
        GdsPoint P1,
        GdsPoint P2,
        bool TouchesWaveguide,
        bool TouchesMetal);

    private const double InteriorProbeOffsetUm = 0.001;

    private static readonly string[] ElectricalLabelMarkers =
        ["anode", "cathode", "elec", "pad", "gnd", "vcc", "vdd"];

    public static IReadOnlyList<DetectedPin> Detect(
        FlattenedGdsCell flattened,
        GdsBoundingBox cellBBox,
        GdsPinDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(flattened);
        options ??= new GdsPinDetectionOptions();
        options.Validate();

        var result = new List<DetectedPin>();
        if (cellBBox.Width <= 0 || cellBBox.Height <= 0)
            return result;

        double tolerance = options.EdgeTouchToleranceUm;

        var labelAnchors = new List<GdsPoint>();
        var candidates = new List<Candidate>();
        double geometryToleranceSquared =
            options.LabelGeometryTouchToleranceUm * options.LabelGeometryTouchToleranceUm;
        foreach (var text in flattened.Texts)
        {
            if (!ContainsLayer(options.PortLayers, text.Layer, text.TextType))
                continue;

            CellEdge edge = NearestEdge(text.Position, cellBBox);
            var geometry = ProbeAnchorGeometry(text.Position, flattened.Polygons, options);
            labelAnchors.Add(text.Position);
            candidates.Add(new Candidate(edge, new DetectedPin
            {
                Name = text.Text,
                XUm = ToAppX(text.Position.X, cellBBox),
                YUm = ToAppY(text.Position.Y, cellBBox),
                AngleDegrees = geometry is not null && geometry.SegmentDistanceSquared <= geometryToleranceSquared
                    ? SegmentOutwardAngleDegrees(geometry.Polygon, geometry.P1, geometry.P2)
                    : OutwardAngleDegrees(edge),
                WidthUm = 0,
                Layer = text.Layer,
                Source = DetectedPinSource.Label,
                IsElectrical = InferLabelPinKind(text.Text, geometry),
            }));
        }

        var waveguidePolygons = flattened.Polygons
            .Where(p => ContainsLayer(options.WaveguideLayers, p.Layer, p.DataType))
            .ToList();
        int? waveguideLayer = waveguidePolygons.Count > 0
            && waveguidePolygons.All(p => p.Layer == waveguidePolygons[0].Layer)
                ? waveguidePolygons[0].Layer
                : null;

        var touches = new SortedList<CellEdge, List<(double Start, double End)>>();
        foreach (var polygon in waveguidePolygons)
        {
            foreach (var (p1, p2) in Segments(polygon))
            {
                CellEdge? edge = TouchingEdge(p1, p2, cellBBox, tolerance);
                if (edge is null)
                    continue;

                (double start, double end) = edge is CellEdge.Left or CellEdge.Right
                    ? (Math.Min(p1.Y, p2.Y), Math.Max(p1.Y, p2.Y))
                    : (Math.Min(p1.X, p2.X), Math.Max(p1.X, p2.X));
                if (!touches.TryGetValue(edge.Value, out var list))
                    touches.Add(edge.Value, list = new List<(double, double)>());
                list.Add((start, end));
            }
        }

        foreach (var (edge, intervals) in touches)
        {
            foreach (var (start, end) in MergeIntervals(intervals, tolerance))
            {
                double width = end - start;
                if (width < options.MinPinWidthUm || width > options.MaxPinWidthUm)
                    continue;

                GdsPoint midpoint = MidpointOnEdge(edge, (start + end) / 2.0, cellBBox);
                if (IsCoveredByLabel(midpoint, labelAnchors, tolerance))
                    continue;

                candidates.Add(new Candidate(edge, new DetectedPin
                {
                    Name = string.Empty,
                    XUm = ToAppX(midpoint.X, cellBBox),
                    YUm = ToAppY(midpoint.Y, cellBBox),
                    AngleDegrees = OutwardAngleDegrees(edge),
                    WidthUm = width,
                    Layer = waveguideLayer,
                    Source = DetectedPinSource.EdgeHeuristic,
                }));
            }
        }

        int heuristicCount = 0;
        foreach (var candidate in candidates
            .OrderBy(c => (int)c.Edge)
            .ThenBy(c => c.Edge is CellEdge.Left or CellEdge.Right ? c.Pin.YUm : c.Pin.XUm))
        {
            var pin = candidate.Pin;
            if (pin.Source == DetectedPinSource.EdgeHeuristic)
                pin = pin with { Name = $"heur_{++heuristicCount}" };
            result.Add(pin);
        }

        return result;
    }

    private static double ToAppX(double gdsX, GdsBoundingBox bbox) => gdsX - bbox.MinX;

    private static double ToAppY(double gdsY, GdsBoundingBox bbox) => bbox.MaxY - gdsY;

    private static double OutwardAngleDegrees(CellEdge edge) => edge switch
    {
        CellEdge.Left => 180.0,
        CellEdge.Top => 270.0,
        CellEdge.Right => 0.0,
        CellEdge.Bottom => 90.0,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown cell edge."),
    };

    private static CellEdge NearestEdge(GdsPoint point, GdsBoundingBox bbox)
    {
        var best = CellEdge.Left;
        double bestDistance = Math.Abs(point.X - bbox.MinX);

        double top = Math.Abs(bbox.MaxY - point.Y);
        if (top < bestDistance) { best = CellEdge.Top; bestDistance = top; }

        double right = Math.Abs(bbox.MaxX - point.X);
        if (right < bestDistance) { best = CellEdge.Right; bestDistance = right; }

        double bottom = Math.Abs(point.Y - bbox.MinY);
        if (bottom < bestDistance) { best = CellEdge.Bottom; }

        return best;
    }

    private static AnchorGeometry? ProbeAnchorGeometry(
        GdsPoint anchor, IReadOnlyList<GdsPolygon> polygons, GdsPinDetectionOptions options)
    {
        double toleranceSquared = options.LabelGeometryTouchToleranceUm * options.LabelGeometryTouchToleranceUm;
        double bestDistanceSquared = double.PositiveInfinity;
        GdsPolygon? bestPolygon = null;
        GdsPoint bestP1 = default, bestP2 = default;
        bool touchesWaveguide = false, touchesMetal = false;

        foreach (var polygon in polygons)
        {
            bool isMetal = ContainsLayer(options.ElectricalLayers, polygon.Layer, polygon.DataType);
            bool isWaveguide = !isMetal && ContainsLayer(options.WaveguideLayers, polygon.Layer, polygon.DataType);
            if (!isMetal && !isWaveguide)
                continue;

            double polygonBestSquared = double.PositiveInfinity;
            GdsPoint polygonBestP1 = default, polygonBestP2 = default;
            foreach (var (p1, p2) in Segments(polygon))
            {
                if (p1.Equals(p2))
                    continue;
                double distanceSquared = DistanceToSegmentSquared(anchor, p1, p2);
                if (distanceSquared < polygonBestSquared)
                {
                    polygonBestSquared = distanceSquared;
                    polygonBestP1 = p1;
                    polygonBestP2 = p2;
                }
            }
            if (polygonBestSquared == double.PositiveInfinity)
                continue;

            if (polygonBestSquared <= toleranceSquared || PointInPolygon(polygon.Points, anchor))
            {
                touchesMetal |= isMetal;
                touchesWaveguide |= isWaveguide;
            }
            if (polygonBestSquared < bestDistanceSquared)
            {
                bestDistanceSquared = polygonBestSquared;
                bestPolygon = polygon;
                bestP1 = polygonBestP1;
                bestP2 = polygonBestP2;
            }
        }

        return bestPolygon is null
            ? null
            : new AnchorGeometry(bestDistanceSquared, bestPolygon, bestP1, bestP2, touchesWaveguide, touchesMetal);
    }

    private static double SegmentOutwardAngleDegrees(GdsPolygon polygon, GdsPoint p1, GdsPoint p2)
    {
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        double nx = -dy / length;
        double ny = dx / length;
        var probe = new GdsPoint(
            ((p1.X + p2.X) / 2.0) + (nx * InteriorProbeOffsetUm),
            ((p1.Y + p2.Y) / 2.0) + (ny * InteriorProbeOffsetUm));
        if (PointInPolygon(polygon.Points, probe))
        {
            nx = -nx;
            ny = -ny;
        }
        return GdsInstancePinProjector.Normalize360(Math.Atan2(-ny, nx) * 180.0 / Math.PI);
    }

    private static bool? InferLabelPinKind(string label, AnchorGeometry? geometry)
    {
        if (geometry is not null)
        {
            if (geometry.TouchesMetal)
                return true;
            if (geometry.TouchesWaveguide)
                return null;
        }

        foreach (string marker in ElectricalLabelMarkers)
        {
            if (label.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return null;
    }

    private static bool PointInPolygon(IReadOnlyList<GdsPoint> polygon, GdsPoint point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            if ((pi.Y > point.Y) != (pj.Y > point.Y)
                && point.X < ((pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y)) + pi.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static double DistanceToSegmentSquared(GdsPoint point, GdsPoint a, GdsPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        double t = lengthSquared == 0
            ? 0
            : Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
        double cx = a.X + (t * dx) - point.X;
        double cy = a.Y + (t * dy) - point.Y;
        return (cx * cx) + (cy * cy);
    }

    private static CellEdge? TouchingEdge(GdsPoint p1, GdsPoint p2, GdsBoundingBox bbox, double tolerance)
    {
        if (Math.Abs(p1.X - bbox.MinX) <= tolerance && Math.Abs(p2.X - bbox.MinX) <= tolerance)
            return CellEdge.Left;
        if (Math.Abs(p1.Y - bbox.MaxY) <= tolerance && Math.Abs(p2.Y - bbox.MaxY) <= tolerance)
            return CellEdge.Top;
        if (Math.Abs(p1.X - bbox.MaxX) <= tolerance && Math.Abs(p2.X - bbox.MaxX) <= tolerance)
            return CellEdge.Right;
        if (Math.Abs(p1.Y - bbox.MinY) <= tolerance && Math.Abs(p2.Y - bbox.MinY) <= tolerance)
            return CellEdge.Bottom;
        return null;
    }

    private static IEnumerable<(GdsPoint P1, GdsPoint P2)> Segments(GdsPolygon polygon)
    {
        var points = polygon.Points;
        for (int i = 0; i + 1 < points.Count; i++)
            yield return (points[i], points[i + 1]);

        if (points.Count > 2 && !points[0].Equals(points[^1]))
            yield return (points[^1], points[0]);
    }

    private static List<(double Start, double End)> MergeIntervals(
        List<(double Start, double End)> intervals, double tolerance)
    {
        intervals.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(double Start, double End)>();
        foreach (var (start, end) in intervals)
        {
            if (merged.Count > 0 && start <= merged[^1].End + tolerance)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            else
                merged.Add((start, end));
        }
        return merged;
    }

    private static GdsPoint MidpointOnEdge(CellEdge edge, double along, GdsBoundingBox bbox) => edge switch
    {
        CellEdge.Left => new GdsPoint(bbox.MinX, along),
        CellEdge.Right => new GdsPoint(bbox.MaxX, along),
        CellEdge.Top => new GdsPoint(along, bbox.MaxY),
        CellEdge.Bottom => new GdsPoint(along, bbox.MinY),
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown cell edge."),
    };

    private static bool IsCoveredByLabel(GdsPoint midpoint, List<GdsPoint> labelAnchors, double tolerance)
    {
        foreach (var anchor in labelAnchors)
        {
            double dx = anchor.X - midpoint.X;
            double dy = anchor.Y - midpoint.Y;
            if (dx * dx + dy * dy <= tolerance * tolerance)
                return true;
        }
        return false;
    }

    private static bool ContainsLayer(
        IReadOnlyList<(int Layer, int Datatype)> layers, int layer, int datatype)
    {
        foreach (var (l, d) in layers)
        {
            if (l == layer && d == datatype)
                return true;
        }
        return false;
    }
}
