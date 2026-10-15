using System;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>One of the four edges of a die outline.</summary>
public enum DieEdge
{
    /// <summary>The left (west) edge; outward direction 180°.</summary>
    Left,
    /// <summary>The top (north, screen-up) edge; outward direction 270°.</summary>
    Top,
    /// <summary>The right (east) edge; outward direction 0°.</summary>
    Right,
    /// <summary>The bottom (south, screen-down) edge; outward direction 90°.</summary>
    Bottom,
}

/// <summary>Where an edge coupler goes when it snaps onto a die edge.</summary>
/// <param name="Edge">The die edge the facet lands on.</param>
/// <param name="QuarterTurns">Quarter turns (counter-clockwise in the model convention) that make the facet face outward.</param>
/// <param name="X">Component top-left X after rotation and snapping.</param>
/// <param name="Y">Component top-left Y after rotation and snapping.</param>
public sealed record EdgeSnap(DieEdge Edge, int QuarterTurns, double X, double Y);

/// <summary>
/// Snaps edge couplers onto die edges: within the die's edge zone a coupler turns so its
/// facet faces out of the die and slides onto the nearest edge, keeping its position along
/// that edge. Angles follow the pin convention: direction (cos θ, sin θ) in the Y-down
/// canvas plane, so 0° is east and 90° is south.
/// </summary>
public static class DieEdgeSnapper
{
    private const double FullTurnDegrees = 360.0;
    private const double QuarterTurnDegrees = 90.0;
    private const double DirectionEpsilon = 1e-9;

    /// <summary>The direction (degrees) pointing out of the die through <paramref name="edge"/>.</summary>
    public static double OutwardAngle(DieEdge edge) => edge switch
    {
        DieEdge.Left => 180.0,
        DieEdge.Top => 270.0,
        DieEdge.Right => 0.0,
        _ => 90.0,
    };

    /// <summary>The edge closest to (<paramref name="x"/>, <paramref name="y"/>) and the distance to it.</summary>
    public static (DieEdge Edge, double Distance) NearestEdge(DieOutline outline, double x, double y)
    {
        var best = (Edge: DieEdge.Left, Distance: Math.Abs(x - outline.X));
        Consider(ref best, DieEdge.Right, Math.Abs(outline.Right - x));
        Consider(ref best, DieEdge.Top, Math.Abs(y - outline.Y));
        Consider(ref best, DieEdge.Bottom, Math.Abs(outline.Bottom - y));
        return best;
    }

    /// <summary>
    /// The edge closest to a component footprint and the gap between them (0 when the
    /// footprint touches or crosses that edge).
    /// </summary>
    public static (DieEdge Edge, double Distance) NearestEdge(DieOutline outline, double x, double y, double width, double height)
    {
        var best = (Edge: DieEdge.Left, Distance: Math.Max(0, x - outline.X));
        Consider(ref best, DieEdge.Right, Math.Max(0, outline.Right - (x + width)));
        Consider(ref best, DieEdge.Top, Math.Max(0, y - outline.Y));
        Consider(ref best, DieEdge.Bottom, Math.Max(0, outline.Bottom - (y + height)));
        return best;
    }

    /// <summary>
    /// The snap for <paramref name="coupler"/> on a die with <paramref name="outline"/>, or
    /// null when the coupler has no facet or its footprint lies farther than
    /// <paramref name="edgeZoneMicrometers"/> from every edge.
    /// </summary>
    public static EdgeSnap? Plan(Component coupler, DieOutline outline, double edgeZoneMicrometers)
    {
        ArgumentNullException.ThrowIfNull(coupler);
        var facet = EdgeCouplerFacet.FacetPinOf(coupler);
        if (facet == null)
            return null;

        var (edge, distance) = NearestEdge(outline,
            coupler.PhysicalX, coupler.PhysicalY, coupler.WidthMicrometers, coupler.HeightMicrometers);
        if (distance > edgeZoneMicrometers)
            return null;

        int turns = QuarterTurnsToFace(facet.GetAbsoluteAngle(), OutwardAngle(edge));
        double px = facet.OffsetXMicrometers, py = facet.OffsetYMicrometers;
        double w = coupler.WidthMicrometers, h = coupler.HeightMicrometers;
        for (int i = 0; i < turns; i++)
            (px, py, w, h) = (h - py, px, h, w);

        var (x, y) = SnapPosition(edge, outline, coupler.PhysicalX, coupler.PhysicalY, px, py, w, h);
        return new EdgeSnap(edge, turns, x, y);
    }

    /// <summary>Rotates and moves <paramref name="coupler"/> as planned by <see cref="Plan"/>.</summary>
    public static void Apply(Component coupler, EdgeSnap snap)
    {
        for (int i = 0; i < snap.QuarterTurns; i++)
            ComponentPoseTransform.Rotate90CounterClockwise(coupler);
        coupler.PhysicalX = snap.X;
        coupler.PhysicalY = snap.Y;
    }

    /// <summary>
    /// Distance from the coupler's facet to the die edge it faces, measured along the facet
    /// direction; a facet outside the outline is measured straight to the outline. 0 means
    /// the facet sits exactly on the edge.
    /// </summary>
    public static double FacetDistanceToEdge(Component coupler, DieOutline outline)
    {
        var facet = EdgeCouplerFacet.FacetPinOf(coupler);
        if (facet == null)
            return double.PositiveInfinity;
        var (x, y) = facet.GetAbsolutePosition();
        if (!outline.Contains(x, y))
        {
            double dx = Math.Max(outline.X - x, Math.Max(0, x - outline.Right));
            double dy = Math.Max(outline.Y - y, Math.Max(0, y - outline.Bottom));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        double radians = facet.GetAbsoluteAngle() * Math.PI / 180.0;
        double tx = ExitDistance(x, Math.Cos(radians), outline.X, outline.Right);
        double ty = ExitDistance(y, Math.Sin(radians), outline.Y, outline.Bottom);
        return Math.Min(tx, ty);
    }

    private static int QuarterTurnsToFace(double facetAngle, double outwardAngle)
    {
        double delta = ((outwardAngle - facetAngle) % FullTurnDegrees + FullTurnDegrees) % FullTurnDegrees;
        return (int)Math.Round(delta / QuarterTurnDegrees) % 4;
    }

    private static (double X, double Y) SnapPosition(
        DieEdge edge, DieOutline outline, double x, double y, double px, double py, double w, double h)
    {
        double clampedX = Math.Clamp(x, outline.X, Math.Max(outline.X, outline.Right - w));
        double clampedY = Math.Clamp(y, outline.Y, Math.Max(outline.Y, outline.Bottom - h));
        return edge switch
        {
            DieEdge.Left => (outline.X - px, clampedY),
            DieEdge.Right => (outline.Right - px, clampedY),
            DieEdge.Top => (clampedX, outline.Y - py),
            _ => (clampedX, outline.Bottom - py),
        };
    }

    private static double ExitDistance(double position, double direction, double min, double max)
    {
        if (direction > DirectionEpsilon)
            return (max - position) / direction;
        if (direction < -DirectionEpsilon)
            return (min - position) / direction;
        return double.PositiveInfinity;
    }

    private static void Consider(ref (DieEdge Edge, double Distance) best, DieEdge edge, double distance)
    {
        if (distance < best.Distance)
            best = (edge, distance);
    }
}
