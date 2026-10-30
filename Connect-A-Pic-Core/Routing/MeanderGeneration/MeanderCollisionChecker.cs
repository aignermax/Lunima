using CAP_Core.Routing.MeanderGeneration.Clearance;

namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// Exact geometric verdict for a meander candidate against <see cref="MeanderObstacles"/>,
/// using the same three rules the direct-route policy applies to sibling routes: no crossing,
/// no contact anywhere, and the minimum spacing outside the pin fan-out zones.
/// Only the geometry the meander actually changed is judged, so a conflict the existing route
/// already had is not blamed on the meander. Neighbours come from the obstacles' spatial index,
/// so the cost follows the candidate's surroundings, not the size of the design.
/// </summary>
internal static class MeanderCollisionChecker
{
    /// <summary>Footprint shrink (µm) so a route ending on a component edge never counts as entering it.</summary>
    private const double FootprintEdgeToleranceMicrometers = 1e-3;

    /// <summary>Chord length (µm) the probe is split into, so the pin exemption is judged locally.</summary>
    private const double ExemptionResolutionMicrometers = 4.0;

    /// <summary>
    /// True when <paramref name="changed"/> conflicts with any obstacle.
    /// </summary>
    /// <param name="changed">The new geometry of the candidate (the whole path when it was rebuilt pin to pin).</param>
    /// <param name="obstacles">Routes and footprints to keep clear of.</param>
    /// <param name="pinExemptionRadiusMicrometers">
    /// Radius around the probe's own endpoints where sub-spacing proximity is tolerated (the pins'
    /// fixed pitch); 0 when the probe starts and ends mid-route.
    /// </param>
    public static bool Collides(RoutedPath changed, MeanderObstacles obstacles, double pinExemptionRadiusMicrometers)
    {
        if (changed.Segments.Count == 0 || obstacles.IsEmpty)
            return false;

        var points = ClearanceGeometry.Sample(changed);
        var probe = new Probe(points[0], points[^1], pinExemptionRadiusMicrometers, obstacles);
        for (int i = 0; i < points.Count - 1; i++)
        {
            foreach (var (a, b) in Split(points[i], points[i + 1], pinExemptionRadiusMicrometers))
            {
                if (probe.Hits(a, b))
                    return true;
            }
        }
        return false;
    }

    private static IEnumerable<((double X, double Y) A, (double X, double Y) B)> Split(
        (double X, double Y) a, (double X, double Y) b, double exemption)
    {
        double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int pieces = exemption > 0 ? Math.Max(1, (int)Math.Ceiling(length / ExemptionResolutionMicrometers)) : 1;
        var previous = a;
        for (int k = 1; k <= pieces; k++)
        {
            double t = (double)k / pieces;
            var next = (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            yield return (previous, next);
            previous = next;
        }
    }

    private sealed class Probe
    {
        private readonly (double X, double Y) _start;
        private readonly (double X, double Y) _end;
        private readonly double _exemption;
        private readonly MeanderObstacles _obstacles;
        private readonly double _reach;
        private readonly List<ClearanceIndex.IndexedSegment> _segments = new();
        private readonly List<MeanderBounds> _footprints = new();

        public Probe((double X, double Y) start, (double X, double Y) end, double exemption, MeanderObstacles obstacles)
        {
            (_start, _end, _exemption, _obstacles) = (start, end, exemption, obstacles);
            _reach = Math.Max(obstacles.MinSpacingMicrometers, obstacles.ContactClearanceMicrometers);
        }

        public bool Hits((double X, double Y) a, (double X, double Y) b)
        {
            _obstacles.Index.Query(Math.Min(a.X, b.X) - _reach, Math.Min(a.Y, b.Y) - _reach,
                Math.Max(a.X, b.X) + _reach, Math.Max(a.Y, b.Y) + _reach,
                _obstacles.IgnoredOwners, _segments, _footprints);
            foreach (var footprint in _footprints)
            {
                if (ClearanceGeometry.SegmentTouchesRectangle(a.X, a.Y, b.X, b.Y, Shrunk(footprint)))
                    return true;
            }
            foreach (var other in _segments)
            {
                var (distance, x, y) = ClearanceGeometry.SegmentDistance(
                    a.X, a.Y, b.X, b.Y, other.Ax, other.Ay, other.Bx, other.By);
                if (distance <= 0 || distance < _obstacles.ContactClearanceMicrometers)
                    return true;
                if (distance < _obstacles.MinSpacingMicrometers && !IsExempt(x, y))
                    return true;
            }
            return false;
        }

        private bool IsExempt(double x, double y) =>
            _exemption > 0 && (Near(_start, x, y) || Near(_end, x, y));

        private bool Near((double X, double Y) pin, double x, double y) =>
            (pin.X - x) * (pin.X - x) + (pin.Y - y) * (pin.Y - y) <= _exemption * _exemption;

        private static MeanderBounds Shrunk(MeanderBounds footprint) => new(
            footprint.MinX + FootprintEdgeToleranceMicrometers, footprint.MinY + FootprintEdgeToleranceMicrometers,
            footprint.MaxX - FootprintEdgeToleranceMicrometers, footprint.MaxY - FootprintEdgeToleranceMicrometers);
    }
}
