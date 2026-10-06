namespace CAP_Core.Routing.ImportedGeometry;

/// <summary>
/// Recovers the centerline of a drawn waveguide polygon. Layout tools such as
/// Nazca and gdsfactory write every straight and every arc of a route as its own
/// ribbon polygon: two caps and two long sides. A ribbon whose sides are parallel
/// lines becomes one exact <see cref="StraightSegment"/>, one whose sides are
/// concentric arcs becomes one exact <see cref="BendSegment"/>, and anything else
/// (an Euler bend, a taper) becomes a chain of short straights through the side
/// midpoints — still the right length and position, only not an analytic shape.
/// </summary>
public static class RibbonCenterlineFitter
{
    /// <summary>
    /// Default shape tolerance (µm): GDS coordinates are snapped to a 1 nm grid, so
    /// a true straight or arc deviates by far less than this.
    /// </summary>
    public const double DefaultToleranceUm = 0.01;

    /// <summary>Minimum side samples used for the polyline fallback.</summary>
    private const int MinPolylineSamples = 8;

    /// <summary>Fewest side points for which the end vertices are left out of the circle fit.</summary>
    private const int MinPointsForInteriorFit = 5;

    /// <summary>
    /// How far (µm) a side's end vertex may sit off the circle fitted to its interior —
    /// the discretization compensation of layout tools stays well below this; a clothoid
    /// or taper deviates by far more.
    /// </summary>
    private const double ArcEndToleranceUm = 0.2;

    /// <summary>
    /// Fits the centerline of <paramref name="outline"/>, or returns null when the
    /// polygon is not a ribbon (no recognizable pair of end caps).
    /// </summary>
    /// <param name="outline">Closed polygon outline (µm).</param>
    /// <param name="toleranceUm">Maximum deviation for an exact straight/arc fit.</param>
    public static RibbonFit? Fit(IReadOnlyList<(double X, double Y)> outline, double toleranceUm = DefaultToleranceUm)
    {
        var sides = RibbonSides.Split(outline);
        if (sides is null) return null;
        return TryStraight(sides, toleranceUm)
            ?? TryArc(sides, toleranceUm)
            ?? Polyline(sides);
    }

    private static RibbonFit? TryStraight(RibbonSides sides, double tol)
    {
        var start = sides.CapStartMidpoint;
        var end = sides.CapEndMidpoint;
        double length = RibbonSides.Distance(start, end);
        if (length <= tol) return null;
        double width = (sides.CapStartLength + sides.CapEndLength) / 2.0;
        double ux = (end.X - start.X) / length, uy = (end.Y - start.Y) / length;
        foreach (var p in sides.SideA.Concat(sides.SideB))
        {
            double offset = Math.Abs((p.X - start.X) * -uy + (p.Y - start.Y) * ux);
            if (Math.Abs(offset - width / 2.0) > tol) return null;
        }

        double angle = Math.Atan2(uy, ux) * 180.0 / Math.PI;
        var segment = new StraightSegment(start.X, start.Y, end.X, end.Y, PathSegmentReversal.NormalizeDegrees(angle));
        return new RibbonFit(new[] { segment }, width, RibbonFitKind.Straight);
    }

    private static RibbonFit? TryArc(RibbonSides sides, double tol)
    {
        var circleA = CircleFit.Fit(InteriorOf(sides.SideA));
        var circleB = CircleFit.Fit(InteriorOf(sides.SideB));
        if (circleA is not { } a || circleB is not { } b) return null;
        if (a.MaxResidual > tol || b.MaxResidual > tol) return null;
        if (Math.Abs(a.CenterX - b.CenterX) > tol || Math.Abs(a.CenterY - b.CenterY) > tol) return null;

        double cx = (a.CenterX + b.CenterX) / 2.0, cy = (a.CenterY + b.CenterY) / 2.0;
        if (!EndsOnCircle(sides.SideA, cx, cy, a.Radius) || !EndsOnCircle(sides.SideB, cx, cy, b.Radius)) return null;

        var start = sides.CapStartMidpoint;
        var end = sides.CapEndMidpoint;
        // The cap midpoints are where the neighbouring pieces attach: taking the radius
        // from them makes the bend start and end exactly there.
        double radius = (RibbonSides.Distance(start, (cx, cy)) + RibbonSides.Distance(end, (cx, cy))) / 2.0;
        double phiStart = Math.Atan2(start.Y - cy, start.X - cx) * 180.0 / Math.PI;
        double phiEnd = Math.Atan2(end.Y - cy, end.X - cx) * 180.0 / Math.PI;
        var middle = sides.SideA[sides.SideA.Count / 2];
        double phiMiddle = Math.Atan2(middle.Y - cy, middle.X - cx) * 180.0 / Math.PI;
        double sweep = SignedSweep(phiStart, phiEnd, phiMiddle);

        double tangent = PathSegmentReversal.NormalizeDegrees(phiStart + 90.0 * Math.Sign(sweep));
        var bend = new BendSegment(cx, cy, radius, tangent, sweep);
        double width = (sides.CapStartLength + sides.CapEndLength) / 2.0;
        return new RibbonFit(new[] { bend }, width, RibbonFitKind.Arc);
    }

    /// <summary>
    /// The side's points the circle is fitted to. Layout tools place the interior vertices
    /// of a discretized arc on a slightly enlarged circle (so the polygon edges straddle
    /// the true arc) but the end vertices on the true one; fitting all of them would mix
    /// two radii. With too few interior points the whole side is used.
    /// </summary>
    private static IReadOnlyList<(double X, double Y)> InteriorOf(IReadOnlyList<(double X, double Y)> side) =>
        side.Count >= MinPointsForInteriorFit ? side.Skip(1).Take(side.Count - 2).ToList() : side;

    /// <summary>True when the side's two end vertices sit within <see cref="ArcEndToleranceUm"/> of the fitted circle.</summary>
    private static bool EndsOnCircle(IReadOnlyList<(double X, double Y)> side, double cx, double cy, double radius) =>
        Math.Abs(RibbonSides.Distance(side[0], (cx, cy)) - radius) <= ArcEndToleranceUm
        && Math.Abs(RibbonSides.Distance(side[^1], (cx, cy)) - radius) <= ArcEndToleranceUm;

    /// <summary>
    /// The signed sweep from <paramref name="phiStart"/> to <paramref name="phiEnd"/>
    /// that passes through <paramref name="phiMiddle"/> (counter-clockwise positive).
    /// </summary>
    private static double SignedSweep(double phiStart, double phiEnd, double phiMiddle)
    {
        double ccw = PathSegmentReversal.NormalizeDegrees(phiEnd - phiStart);
        double toMiddle = PathSegmentReversal.NormalizeDegrees(phiMiddle - phiStart);
        return toMiddle <= ccw ? ccw : ccw - 360.0;
    }

    private static RibbonFit Polyline(RibbonSides sides)
    {
        int samples = Math.Max(MinPolylineSamples, Math.Max(sides.SideA.Count, sides.SideB.Count));
        var a = Resample(sides.SideA, samples);
        var b = Resample(sides.SideB, samples);
        var segments = new List<PathSegment>();
        var previous = RibbonSides.Mid(a[0], b[0]);
        for (int i = 1; i < samples; i++)
        {
            var point = RibbonSides.Mid(a[i], b[i]);
            if (RibbonSides.Distance(previous, point) <= 1e-9) continue;
            double angle = Math.Atan2(point.Y - previous.Y, point.X - previous.X) * 180.0 / Math.PI;
            segments.Add(new StraightSegment(previous.X, previous.Y, point.X, point.Y, PathSegmentReversal.NormalizeDegrees(angle)));
            previous = point;
        }

        double width = (sides.CapStartLength + sides.CapEndLength) / 2.0;
        return new RibbonFit(segments, width, RibbonFitKind.Polyline);
    }

    /// <summary>Resamples a polyline into <paramref name="count"/> points evenly spaced by arc length.</summary>
    private static List<(double X, double Y)> Resample(IReadOnlyList<(double X, double Y)> line, int count)
    {
        var cumulative = new double[line.Count];
        for (int i = 1; i < line.Count; i++)
            cumulative[i] = cumulative[i - 1] + RibbonSides.Distance(line[i - 1], line[i]);
        double total = cumulative[^1];
        var result = new List<(double X, double Y)>(count);
        int segment = 1;
        for (int k = 0; k < count; k++)
        {
            double target = total * k / (count - 1);
            while (segment < line.Count - 1 && cumulative[segment] < target) segment++;
            double span = cumulative[segment] - cumulative[segment - 1];
            double t = span <= 0 ? 0 : (target - cumulative[segment - 1]) / span;
            var p0 = line[segment - 1];
            var p1 = line[segment];
            result.Add((p0.X + (p1.X - p0.X) * t, p0.Y + (p1.Y - p0.Y) * t));
        }
        return result;
    }
}
