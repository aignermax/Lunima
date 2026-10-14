using System;
using System.Collections.Generic;
using System.Linq;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>One edge-coupler facet pair a die snap brings face to face.</summary>
/// <param name="MovingFacet">Facet pin on the die being dragged.</param>
/// <param name="FixedFacet">Facet pin on the neighbouring die.</param>
public sealed record DieLinkPair(PhysicalPin MovingFacet, PhysicalPin FixedFacet);

/// <summary>Result of snapping a dragged die onto a neighbour.</summary>
/// <param name="DeltaX">Total X move of the dragged die (pointer delta plus snap correction).</param>
/// <param name="DeltaY">Total Y move of the dragged die.</param>
/// <param name="Pairs">Every facet pair that ends up aligned at the link gap.</param>
/// <param name="GapMicrometers">The facet-to-facet gap the snap aligns to.</param>
public sealed record DieLinkSnap(double DeltaX, double DeltaY, IReadOnlyList<DieLinkPair> Pairs, double GapMicrometers);

/// <summary>
/// Die-to-die snapping: while a die is dragged toward another, its edge-coupler facets that
/// sit on the die edge snap onto facing facets of the neighbour — on axis, at
/// <see cref="DefaultGapMicrometers"/> of air. The snap aligns the nearest facing pair
/// within <see cref="SnapRadiusMicrometers"/>; every other pair that is then aligned too
/// (couplers at the same pitch on both dies) is reported so all of them get linked.
/// </summary>
public static class DieLinkSnapper
{
    /// <summary>Default facet-to-facet air gap of a die-to-die link (butt-coupling with clearance).</summary>
    public const double DefaultGapMicrometers = 2.0;

    /// <summary>How close a facing facet pair must come before the die snaps.</summary>
    public const double SnapRadiusMicrometers = 150.0;

    private const double FacingToleranceDegrees = 1.0;
    private const double AlignedToleranceMicrometers = 0.01;
    private const double FacetOnEdgeToleranceMicrometers = 1.0;

    /// <summary>The facet pins of <paramref name="die"/>'s edge couplers that sit on its edge.</summary>
    public static IReadOnlyList<PhysicalPin> EdgeFacets(ComponentGroup die)
    {
        var outline = DieAssembly.OutlineOf(die);
        return die.GetAllComponentsRecursive()
            .Where(EdgeCouplerFacet.IsEdgeCoupler)
            .Where(c => DieEdgeSnapper.FacetDistanceToEdge(c, outline) <= FacetOnEdgeToleranceMicrometers)
            .Select(EdgeCouplerFacet.FacetPinOf)
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();
    }

    /// <summary>
    /// The snap for <paramref name="moving"/> moved by (<paramref name="deltaX"/>,
    /// <paramref name="deltaY"/>) from where it is now, or null when no facing facet of
    /// <paramref name="others"/> comes within <paramref name="snapRadiusMicrometers"/>.
    /// </summary>
    public static DieLinkSnap? Plan(
        ComponentGroup moving, double deltaX, double deltaY, IEnumerable<ComponentGroup> others,
        double gapMicrometers = DefaultGapMicrometers, double snapRadiusMicrometers = SnapRadiusMicrometers)
    {
        ArgumentNullException.ThrowIfNull(moving);
        var movingFacets = EdgeFacets(moving);
        var fixedFacets = others.Where(d => d != moving).SelectMany(EdgeFacets).ToList();
        if (movingFacets.Count == 0 || fixedFacets.Count == 0)
            return null;

        (double X, double Y)? best = null;
        double bestDistance = double.MaxValue;
        foreach (var a in movingFacets)
        {
            foreach (var b in fixedFacets)
            {
                if (Correction(a, b, deltaX, deltaY, gapMicrometers) is not { } c)
                    continue;
                double distance = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                if (distance <= snapRadiusMicrometers && distance < bestDistance)
                {
                    best = c;
                    bestDistance = distance;
                }
            }
        }
        if (best is not { } correction)
            return null;

        double totalX = deltaX + correction.X, totalY = deltaY + correction.Y;
        var pairs = new List<DieLinkPair>();
        foreach (var a in movingFacets)
        {
            var match = fixedFacets.FirstOrDefault(b =>
                Correction(a, b, totalX, totalY, gapMicrometers) is { } c
                && Math.Abs(c.X) <= AlignedToleranceMicrometers && Math.Abs(c.Y) <= AlignedToleranceMicrometers);
            if (match != null)
                pairs.Add(new DieLinkPair(a, match));
        }
        return new DieLinkSnap(totalX, totalY, pairs, gapMicrometers);
    }

    /// <summary>
    /// The move of <paramref name="movingFacet"/>'s die that puts it on
    /// <paramref name="fixedFacet"/>'s axis at <paramref name="gapMicrometers"/>, whatever the
    /// current distance — used when a wire is drawn between two facing facets.
    /// </summary>
    public static (double DeltaX, double DeltaY) AlignmentFor(
        PhysicalPin movingFacet, PhysicalPin fixedFacet, double gapMicrometers = DefaultGapMicrometers)
    {
        ArgumentNullException.ThrowIfNull(movingFacet);
        ArgumentNullException.ThrowIfNull(fixedFacet);
        double radians = movingFacet.GetAbsoluteAngle() * Math.PI / 180.0;
        var (ax, ay) = movingFacet.GetAbsolutePosition();
        var (bx, by) = fixedFacet.GetAbsolutePosition();
        return (bx - gapMicrometers * Math.Cos(radians) - ax, by - gapMicrometers * Math.Sin(radians) - ay);
    }

    /// <summary>
    /// Extra move that puts facet <paramref name="a"/> (shifted by the drag delta) on
    /// facet <paramref name="b"/>'s axis at the link gap, or null when the two do not face
    /// each other (antiparallel and <paramref name="b"/> in front of <paramref name="a"/>).
    /// </summary>
    private static (double X, double Y)? Correction(PhysicalPin a, PhysicalPin b, double deltaX, double deltaY, double gap)
    {
        double angleA = a.GetAbsoluteAngle();
        if (AntiparallelDeviation(angleA, b.GetAbsoluteAngle()) > FacingToleranceDegrees)
            return null;
        double radians = angleA * Math.PI / 180.0;
        double dirX = Math.Cos(radians), dirY = Math.Sin(radians);
        var (ax, ay) = a.GetAbsolutePosition();
        ax += deltaX;
        ay += deltaY;
        var (bx, by) = b.GetAbsolutePosition();
        if ((bx - ax) * dirX + (by - ay) * dirY < -SnapRadiusMicrometers)
            return null;
        double targetX = bx - gap * dirX, targetY = by - gap * dirY;
        return (targetX - ax, targetY - ay);
    }

    private static double AntiparallelDeviation(double angleA, double angleB)
    {
        double diff = Math.Abs(angleA - angleB) % 360.0;
        if (diff > 180.0)
            diff = 360.0 - diff;
        return 180.0 - diff;
    }
}
