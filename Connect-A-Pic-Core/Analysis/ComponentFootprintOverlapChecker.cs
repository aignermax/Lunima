using System.Globalization;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis;

/// <summary>
/// Detects physically overlapping footprints of top-level placed items
/// (components and ComponentGroups) using their placed, rotation-aware
/// bounding rectangles. Children inside the same group are not compared
/// with each other — a group's internal layout is the group's business;
/// the group participates as one footprint.
/// </summary>
public class ComponentFootprintOverlapChecker
{
    /// <summary>
    /// Overlap depth up to which two footprints count as touching, not
    /// overlapping. Sized above the facet-pin inset PDK cells carry (the SiEPIC
    /// and Cornerstone cells model their facet pins up to ~0.1 µm inside the cell
    /// bounding box), so butt-coupled chiplets whose coincident pins align the
    /// cell edges graze by that inset without tripping the check; the check
    /// targets gross stacking (tens of µm and up), and genuine waveguide clashes
    /// inside a grazing pair remain covered by the waveguide overlap/spacing rules.
    /// </summary>
    public const double OverlapToleranceMicrometers = 0.5;

    private sealed record PlacedFootprint(
        string Name, double MinX, double MinY, double MaxX, double MaxY);

    /// <summary>
    /// Compares every pair of top-level placed items and returns one
    /// <see cref="DesignIssueType.ComponentFootprintOverlap"/> issue per pair
    /// whose rectangles overlap by more than
    /// <see cref="OverlapToleranceMicrometers"/> in both axes.
    /// </summary>
    /// <param name="placedItems">
    /// Placed items to check; items belonging to a group
    /// (<see cref="Component.ParentGroup"/> set) are skipped — only the group
    /// itself participates.
    /// </param>
    /// <returns>A list of overlap issues, empty if every footprint stands clear.</returns>
    public List<DesignIssue> DetectOverlaps(IEnumerable<Component> placedItems)
    {
        ArgumentNullException.ThrowIfNull(placedItems);

        var footprints = placedItems
            .Where(HasCheckableFootprint)
            .Select(ToFootprint)
            .ToList();

        var issues = new List<DesignIssue>();
        for (int i = 0; i < footprints.Count; i++)
        {
            for (int j = i + 1; j < footprints.Count; j++)
            {
                var issue = CheckPair(footprints[i], footprints[j]);
                if (issue is not null)
                    issues.Add(issue);
            }
        }
        return issues;
    }

    /// <summary>
    /// True for physical, top-level items with a real footprint: virtual analysis
    /// tools carry no fab geometry, and pin-less background art (die frames,
    /// logos, ground plates) is allowed to sit under the circuit by design.
    /// </summary>
    private static bool HasCheckableFootprint(Component item) =>
        item.ParentGroup is null
        && !item.IsAnalysisTool
        && item.IsRoutingObstacle
        && item.WidthMicrometers > 0
        && item.HeightMicrometers > 0;

    /// <summary>
    /// The placed, rotation-aware bounding rectangle. Width/height already carry
    /// the rotation (rotating re-bases them onto the rotated footprint's AABB);
    /// a group's rectangle additionally starts at its minimum child corner.
    /// </summary>
    private static PlacedFootprint ToFootprint(Component item)
    {
        double minX = item.PhysicalX;
        double minY = item.PhysicalY;
        if (item is ComponentGroup group)
        {
            minX += group.MinChildOffsetX;
            minY += group.MinChildOffsetY;
        }

        return new PlacedFootprint(
            DisplayName(item),
            minX,
            minY,
            minX + item.WidthMicrometers,
            minY + item.HeightMicrometers);
    }

    /// <summary>
    /// Returns an issue when the two rectangles overlap beyond the tolerance in
    /// both axes, located at the overlap's center; null when they only touch.
    /// </summary>
    private static DesignIssue? CheckPair(PlacedFootprint a, PlacedFootprint b)
    {
        double overlapX = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
        double overlapY = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
        if (overlapX <= OverlapToleranceMicrometers || overlapY <= OverlapToleranceMicrometers)
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
                $"Overlapping component footprints: '{a.Name}' ↔ '{b.Name}' ({overlapX:F1} × {overlapY:F1} µm)"));
    }

    private static string DisplayName(Component item) =>
        item switch
        {
            ComponentGroup group when !string.IsNullOrWhiteSpace(group.GroupName) => group.GroupName,
            _ => item.HumanReadableName ?? item.Identifier,
        };
}
