using System.Globalization;
using CAP_Core.Analysis.Hierarchy;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis;

/// <summary>
/// Detects physically overlapping component footprints using their placed,
/// rotation-aware bounding rectangles. Groups are descended at any nesting depth
/// and compared leaf by leaf, so a child of a group is checked against its
/// siblings, against loose top-level components and against children of other
/// groups alike. A group's own bounding box never participates: it is the hull of
/// its children and frozen paths and would flag anything parked in an empty
/// corner of the group (frozen paths running through a footprint are covered by
/// <see cref="FrozenPathFootprintChecker"/>).
/// </summary>
public class ComponentFootprintOverlapChecker
{
    /// <summary>
    /// Overlap depth up to which two footprints count as touching, not
    /// overlapping. Sized above the facet-pin inset PDK cells carry (the SiEPIC
    /// and Cornerstone cells model their facet pins up to ~0.1 µm inside the cell
    /// bounding box), so butt-coupled chiplets and pin-to-pin abutted group
    /// children whose coincident pins align the cell edges graze by that inset
    /// without tripping the check; the check targets gross stacking (tens of µm
    /// and up), and genuine waveguide clashes inside a grazing pair remain covered
    /// by the waveguide overlap/spacing rules.
    /// </summary>
    public const double OverlapToleranceMicrometers = 0.5;

    private sealed record PlacedFootprint(
        Component Item, string Name, double MinX, double MinY, double MaxX, double MaxY);

    /// <summary>
    /// Compares every pair of leaf components reachable from the placed items (group
    /// children at any depth included) and returns one
    /// <see cref="DesignIssueType.ComponentFootprintOverlap"/> issue per pair whose
    /// rectangles overlap by more than <see cref="OverlapToleranceMicrometers"/> in
    /// both axes. Nested components are named with their group path ("AND0/mmi_1").
    /// </summary>
    /// <param name="placedItems">
    /// Placed items to check — usually the top-level components and groups; a child
    /// passed alongside its group is checked once.
    /// </param>
    /// <returns>A list of overlap issues, empty if every footprint stands clear.</returns>
    public List<DesignIssue> DetectOverlaps(IEnumerable<Component> placedItems)
    {
        ArgumentNullException.ThrowIfNull(placedItems);

        var footprints = DesignHierarchy.From(placedItems).Leaves
            .Where(HasCheckableFootprint)
            .Select(ToFootprint)
            .OrderBy(f => f.MinX)
            .ToList();

        var issues = new List<DesignIssue>();
        for (int i = 0; i < footprints.Count; i++)
        {
            // Sorted by MinX: once a footprint starts right of footprint i, none after it can overlap.
            for (int j = i + 1; j < footprints.Count && footprints[j].MinX < footprints[i].MaxX; j++)
            {
                var issue = CheckPair(footprints[i], footprints[j]);
                if (issue is not null)
                    issues.Add(issue);
            }
        }
        return issues;
    }

    /// <summary>
    /// True for physical items with a real footprint: virtual analysis tools carry
    /// no fab geometry, and pin-less background art (die frames, logos, ground
    /// plates) is allowed to sit under the circuit by design.
    /// </summary>
    internal static bool HasCheckableFootprint(Component item) =>
        !item.IsAnalysisTool
        && item.IsRoutingObstacle
        && item.WidthMicrometers > 0
        && item.HeightMicrometers > 0;

    /// <summary>
    /// The placed, rotation-aware bounding rectangle. Width/height already carry
    /// the rotation (rotating re-bases them onto the rotated footprint's AABB).
    /// </summary>
    private static PlacedFootprint ToFootprint(Component item) =>
        new(
            item,
            DesignHierarchy.QualifyName(item, item.HumanReadableName ?? item.Identifier),
            item.PhysicalX,
            item.PhysicalY,
            item.PhysicalX + item.WidthMicrometers,
            item.PhysicalY + item.HeightMicrometers);

    /// <summary>
    /// Returns an issue when the two rectangles overlap beyond the tolerance in
    /// both axes, located at the overlap's center; null when they only touch or are
    /// same-group siblings grazing through their cell margins (<see cref="SiblingGrazeRule"/>).
    /// </summary>
    private static DesignIssue? CheckPair(PlacedFootprint a, PlacedFootprint b)
    {
        double overlapX = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
        double overlapY = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
        if (overlapX <= OverlapToleranceMicrometers || overlapY <= OverlapToleranceMicrometers)
            return null;

        var overlap = (Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY),
                       Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY));
        if (SiblingGrazeRule.IsGraze(a.Item, b.Item, overlap))
            return null;

        double centerX = Math.Max(a.MinX, b.MinX) + overlapX / 2;
        double centerY = Math.Max(a.MinY, b.MinY) + overlapY / 2;
        return new DesignIssue(
            DesignIssueType.ComponentFootprintOverlap,
            connection: null,
            centerX,
            centerY,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Overlapping component footprints: '{a.Name}' ↔ '{b.Name}' ({overlapX:F1} × {overlapY:F1} µm)"),
            localizationKey: "DesignChecks.ComponentFootprintOverlap",
            localizationArgs: new object[] { a.Name, b.Name, overlapX, overlapY });
    }
}
