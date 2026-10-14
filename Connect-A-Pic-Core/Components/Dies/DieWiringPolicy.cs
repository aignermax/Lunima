using CAP_Core.Analysis.Hierarchy;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>How a wire request between two pins is handled on a die assembly.</summary>
public enum DieWiringVerdict
{
    /// <summary>Both pins are on the same die (or the design has no dies): an ordinary waveguide.</summary>
    Waveguide,

    /// <summary>Two edge-coupler facets on facing edges of different dies: a die-to-die link.</summary>
    FacetLink,

    /// <summary>The pins are on different dies (or one is on no die) and are not a facing facet pair.</summary>
    RefusedNotCouplers,

    /// <summary>Both pins are edge-coupler facets on different dies, but the facets do not face each other.</summary>
    RefusedNotFacing,
}

/// <summary>Outcome of <see cref="DieWiringPolicy.Evaluate"/>.</summary>
/// <param name="Verdict">What to do with the request.</param>
/// <param name="StartDie">The die of the start pin (null when it is on no die).</param>
/// <param name="EndDie">The die of the end pin (null when it is on no die).</param>
public sealed record DieWiringDecision(DieWiringVerdict Verdict, ComponentGroup? StartDie, ComponentGroup? EndDie)
{
    /// <summary>Localization key of the hint shown for a refused request, null when allowed.</summary>
    public string? RefusalKey => Verdict switch
    {
        DieWiringVerdict.RefusedNotCouplers => "Dies.Wiring.CouplersOnly",
        DieWiringVerdict.RefusedNotFacing => "Dies.Wiring.NotFacing",
        _ => null,
    };
}

/// <summary>
/// A waveguide never crosses a die edge: two dies are separate pieces of material and light
/// passes between them only through edge couplers whose facets face each other across the
/// gap. A wire request between pins on different dies therefore becomes a die-to-die link
/// when both pins are such facets, and is refused otherwise.
/// </summary>
public static class DieWiringPolicy
{
    private const double FacingToleranceDegrees = 1.0;

    /// <summary>Decides what a wire request from <paramref name="start"/> to <paramref name="end"/> becomes.</summary>
    public static DieWiringDecision Evaluate(PhysicalPin start, PhysicalPin end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        var startDie = ConnectionOwnership.DieOf(start);
        var endDie = ConnectionOwnership.DieOf(end);
        if (startDie == null && endDie == null)
            return new DieWiringDecision(DieWiringVerdict.Waveguide, null, null);
        if (ReferenceEquals(startDie, endDie))
            return new DieWiringDecision(DieWiringVerdict.Waveguide, startDie, endDie);
        if (startDie == null || endDie == null || !IsFacet(start) || !IsFacet(end))
            return new DieWiringDecision(DieWiringVerdict.RefusedNotCouplers, startDie, endDie);
        return Faces(start, end) && IsOnEdge(start, startDie) && IsOnEdge(end, endDie)
            ? new DieWiringDecision(DieWiringVerdict.FacetLink, startDie, endDie)
            : new DieWiringDecision(DieWiringVerdict.RefusedNotFacing, startDie, endDie);
    }

    /// <summary>True when the facet sits on its die's edge (a facet inside the die couples to nothing).</summary>
    private static bool IsOnEdge(PhysicalPin facet, ComponentGroup die)
    {
        var leaf = DesignHierarchy.ResolveToLeafPin(facet);
        return DieLinkSnapper.EdgeFacets(die).Contains(leaf);
    }

    /// <summary>True when <paramref name="pin"/> is the facet pin of an edge coupler.</summary>
    public static bool IsFacet(PhysicalPin pin)
    {
        var leaf = DesignHierarchy.ResolveToLeafPin(pin);
        return EdgeCouplerFacet.IsEdgeCoupler(leaf.ParentComponent)
               && ReferenceEquals(EdgeCouplerFacet.FacetPinOf(leaf.ParentComponent), leaf);
    }

    /// <summary>
    /// True when the facets point at each other: antiparallel, and each lies in front of the
    /// other (the end facet is ahead of the start facet along its emission direction).
    /// </summary>
    public static bool Faces(PhysicalPin start, PhysicalPin end)
    {
        double angleStart = start.GetAbsoluteAngle();
        double deviation = Math.Abs(angleStart - end.GetAbsoluteAngle()) % 360.0;
        if (deviation > 180.0)
            deviation = 360.0 - deviation;
        if (180.0 - deviation > FacingToleranceDegrees)
            return false;
        double radians = angleStart * Math.PI / 180.0;
        var (sx, sy) = start.GetAbsolutePosition();
        var (ex, ey) = end.GetAbsolutePosition();
        return (ex - sx) * Math.Cos(radians) + (ey - sy) * Math.Sin(radians) >= 0;
    }
}
