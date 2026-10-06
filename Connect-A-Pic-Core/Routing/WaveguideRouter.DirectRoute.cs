using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing;

/// <summary>
/// Direct/S-bend-first policy of <see cref="WaveguideRouter"/> (issues #860, #874):
/// building the styled candidate and verifying it before A* runs. Component bodies are
/// checked against the obstacle grid (with the A* pin corridors cleared); sibling
/// waveguides are checked GEOMETRICALLY against their exact registered segments. The
/// rasterized waveguide cells over-approximate each sibling by up to a grid cell plus
/// half the obstacle width (~6 µm at defaults), which walled off dense fan-out arrays:
/// once the first routes were registered, every later styled candidate read as blocked
/// even though it merely ran parallel to a neighbor (field report: only ~25 % of a
/// 166-connection import routed cleanly, the rest degraded to red blocked fallbacks).
/// </summary>
public partial class WaveguideRouter
{
    /// <summary>Pin corridor length as a multiple of the bend radius — the same corridor
    /// <see cref="TryRouteAStar"/> clears. Also used as the endpoint exemption radius of
    /// the sibling clearance check: within it, proximity to fan-out siblings attached to
    /// neighboring pins is dictated by the fixed pin pitch, not by the route.</summary>
    private const double CorridorLengthRadiusFactor = 3.0;

    /// <summary>Endpoint match tolerance (µm) used to recognize a registered obstacle as
    /// this connection's own stale route (same pin pair) so it never blocks its re-route.</summary>
    private const double SameEndpointToleranceMicrometers = 1.0;

    /// <summary>Centerline clearance (µm) below which a candidate and a sibling count as
    /// touching — the drawn waveguide cores physically merge. Enforced everywhere, even
    /// inside the pin fan-out exemption zones where sub-min-spacing proximity is allowed.</summary>
    private const double SiblingContactClearanceMicrometers =
        PhotonicConstants.StandardWaveguideWidthMicrometers;

    /// <summary>
    /// Direct/S-bend-first policy (issue #860): builds the styled candidate for the pin
    /// geometry and accepts it only when it is clean, clear of component bodies on the
    /// grid A* uses (with the same pin corridors cleared), and geometrically compatible
    /// with every registered sibling route — no crossing, and no closer than
    /// <see cref="MinWaveguideSpacingMicrometers"/> outside the pin fan-out zones.
    /// Returns null when no styled geometry fits or the candidate conflicts — A* then
    /// routes as before.
    /// </summary>
    private RoutedPath? TryRouteDirect(PhysicalPin startPin, PhysicalPin endPin, double bendRadius)
    {
        // Snapping to the foundry allowed radii is an optical concern (lower bend loss):
        // electrical (metal) traces are governed solely by the RF process floor and must
        // keep their geometry, mirroring the BendRadiusUpsizer exclusion.
        bool isElectrical = startPin.MatterType == MatterType.Electricity
            || endPin.MatterType == MatterType.Electricity;
        var candidate = InterconnectRouting.DirectRouteFirstPolicy.TryBuildWithStyle(
            startPin, endPin, bendRadius, out var directStyle,
            isElectrical ? null : AllowedBendRadii);
        if (candidate == null)
            return null;

        candidate = AxisAlignPinEnds(candidate, startPin, endPin);
        if (!candidate.IsValid
            || PathIntersectionDetector.HasSelfIntersection(candidate)
            || IsDirectCandidateBlockedByComponents(candidate.Segments, startPin, endPin, bendRadius)
            || DirectCandidateConflictsWithSibling(candidate, startPin, endPin, bendRadius))
        {
            return null;
        }

        candidate.IsDirectStyledRoute = true;
        candidate.DirectStyle = directStyle;
        return candidate;
    }

    /// <summary>
    /// Component blocked-cell test for the direct styled candidate, on the SAME grid state
    /// A* would route on: the pin corridors <see cref="TryRouteAStar"/> clears (start
    /// outward, end facing and end terminal — 3·radius long, radius wide) are cleared
    /// for the test and restored afterwards. A styled path may therefore dip into the
    /// endpoint components' own cells at the pin exit/entry — but a path that keeps
    /// running THROUGH a component body beyond the corridor (field report: the S-bend
    /// flowed straight through the target component whose pin faced away) stays
    /// blocked and defers to A*, which routes around the body. Sibling waveguides are
    /// deliberately NOT judged by cells here — their exact geometry is checked in
    /// <see cref="DirectCandidateConflictsWithSibling"/> instead.
    /// </summary>
    private bool IsDirectCandidateBlockedByComponents(
        IReadOnlyList<PathSegment> segments, PhysicalPin startPin, PhysicalPin endPin, double bendRadius)
    {
        if (PathfindingGrid == null) return false;

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double startAngle = startPin.GetAbsoluteAngle();
        double endFacingAngle = endPin.GetAbsoluteAngle();
        double endInputAngle = AngleUtilities.NormalizeAngle(endFacingAngle + 180);
        double corridorLength = bendRadius * CorridorLengthRadiusFactor;
        double corridorWidth = bendRadius;

        var clearedStart = PathfindingGrid.ClearPinCorridor(
            startX, startY, startAngle, corridorLength, corridorWidth);
        var clearedEndApproach = PathfindingGrid.ClearPinCorridor(
            endX, endY, endFacingAngle, corridorLength, corridorWidth);
        var clearedEndTerminal = PathfindingGrid.ClearPinCorridor(
            endX, endY, endInputAngle, corridorLength, corridorWidth);
        try
        {
            return IsPathBlocked(segments, PathfindingGrid.IsBlockedByComponent);
        }
        finally
        {
            PathfindingGrid.RestoreCells(clearedStart);
            PathfindingGrid.RestoreCells(clearedEndApproach);
            PathfindingGrid.RestoreCells(clearedEndTerminal);
        }
    }

    /// <summary>
    /// Exact geometric verdict against every registered sibling route (issue #874): the
    /// candidate is rejected when it properly CROSSES a sibling, when it comes closer
    /// than <see cref="MinWaveguideSpacingMicrometers"/> outside the pin fan-out zones
    /// (within 3·radius of the candidate's own pins, proximity to neighbors is dictated
    /// by the fixed pin pitch and tolerated — matching the A* pin-corridor allowance),
    /// or when it makes CONTACT with a sibling anywhere — including inside the fan-out
    /// zones. Contact means the centerlines come within one waveguide width: the drawn
    /// cores physically merge, the exported GDS polygons touch, and a re-import can no
    /// longer disentangle the routes (they collapse into one frozen junction network).
    /// A registered obstacle with this connection's own endpoints is its stale previous
    /// route and is skipped.
    /// </summary>
    private bool DirectCandidateConflictsWithSibling(
        RoutedPath candidate, PhysicalPin startPin, PhysicalPin endPin, double bendRadius)
    {
        if (PathfindingGrid == null) return false;
        var siblings = PathfindingGrid.GetWaveguideGeometries();
        if (siblings.Count == 0) return false;

        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();
        double exemptionRadius = bendRadius * CorridorLengthRadiusFactor;

        foreach (var segments in siblings)
        {
            if (segments.Count == 0 || IsOwnStaleRoute(segments, startX, startY, endX, endY))
                continue;

            var sibling = new RoutedPath();
            sibling.Segments.AddRange(segments);
            if (PathIntersectionDetector.Crosses(candidate, sibling))
                return true;
            if (PathIntersectionDetector.ComesCloserThan(
                    candidate, sibling, MinWaveguideSpacingMicrometers, exemptionRadius))
                return true;
            if (PathIntersectionDetector.ComesCloserThan(
                    candidate, sibling, SiblingContactClearanceMicrometers,
                    endpointExemptionRadiusMicrometers: 0))
                return true;
        }
        return false;
    }

    /// <summary>Length (µm) of the axis-aligned stubs a degenerate single-diagonal direct
    /// route gets at both pins.</summary>
    private const double PinEndStubMicrometers = 1.0;

    /// <summary>Angular tolerance (degrees) for "this end segment already runs along the pin axis".
    /// Tighter than the tilt whose sin()×half-width end-cap poke reaches one export dbu.</summary>
    private const double PinAxisToleranceDegrees = 0.05;

    /// <summary>
    /// A styled candidate between facing pins with a sub-bend-radius lateral offset degrades
    /// to a single diagonal straight — the only straight that joins the pins — and its end
    /// segments then leave/arrive off-axis. On export the tilted end cap pokes a rounding
    /// sliver past the partner component's DevRec plane, which the SiEPIC verification
    /// counts as an overlapping component. Give such a degenerate route a short axis-aligned
    /// stub at each misaligned end; multi-segment styled paths already leave and arrive
    /// along the pin axes and are returned unchanged.
    /// </summary>
    private static RoutedPath AxisAlignPinEnds(RoutedPath candidate, PhysicalPin startPin, PhysicalPin endPin)
    {
        if (candidate.Segments.Count != 1 || candidate.Segments[0] is not StraightSegment)
            return candidate;

        double startAngle = startPin.GetAbsoluteAngle();
        double arrivalAngle = AngleUtilities.NormalizeAngle(endPin.GetAbsoluteAngle() + 180.0);
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        bool startAligned = IsAxisParallel(startX, startY, endX, endY, startAngle);
        bool endAligned = IsAxisParallel(startX, startY, endX, endY, arrivalAngle);
        if (startAligned && endAligned)
            return candidate;

        double dx = endX - startX;
        double dy = endY - startY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double stub = Math.Min(PinEndStubMicrometers, length / 4.0);

        var points = new List<(double X, double Y)> { (startX, startY) };
        if (!startAligned)
            points.Add((startX + stub * CosDegrees(startAngle), startY + stub * SinDegrees(startAngle)));
        if (!endAligned)
            points.Add((endX - stub * CosDegrees(arrivalAngle), endY - stub * SinDegrees(arrivalAngle)));
        points.Add((endX, endY));

        var aligned = new RoutedPath();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            aligned.Segments.Add(new StraightSegment(
                points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y,
                SegmentAngleDegrees(points[i], points[i + 1])));
        }
        return aligned;
    }

    /// <summary>True when the segment from (x1,y1) to (x2,y2) runs along the given axis.</summary>
    private static bool IsAxisParallel(double x1, double y1, double x2, double y2, double axisAngleDegrees) =>
        Math.Abs(AngleUtilities.NormalizeAngle(SegmentAngleDegrees((x1, y1), (x2, y2)) - axisAngleDegrees))
        <= PinAxisToleranceDegrees;

    private static double SegmentAngleDegrees((double X, double Y) from, (double X, double Y) to) =>
        Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI;

    private static double CosDegrees(double angleDegrees) => Math.Cos(angleDegrees * Math.PI / 180.0);

    private static double SinDegrees(double angleDegrees) => Math.Sin(angleDegrees * Math.PI / 180.0);

    /// <summary>True when the registered segments run between the same two pin positions
    /// (either orientation) — i.e. they are this connection's own previous route.</summary>
    private static bool IsOwnStaleRoute(
        IReadOnlyList<PathSegment> segments,
        double startX, double startY, double endX, double endY)
    {
        var first = segments[0].StartPoint;
        var last = segments[^1].EndPoint;
        return (IsSamePoint(first.X, first.Y, startX, startY) && IsSamePoint(last.X, last.Y, endX, endY))
            || (IsSamePoint(first.X, first.Y, endX, endY) && IsSamePoint(last.X, last.Y, startX, startY));
    }

    /// <summary>True when two points coincide within <see cref="SameEndpointToleranceMicrometers"/>.</summary>
    private static bool IsSamePoint(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return dx * dx + dy * dy
            <= SameEndpointToleranceMicrometers * SameEndpointToleranceMicrometers;
    }
}
