using CAP_Core.Routing;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis.Hierarchy;

/// <summary>
/// Flags frozen group paths (any nesting depth) whose centerline runs through the
/// footprint of a component — a loose top-level one, a sibling inside the same group,
/// or a child of another group. A frozen path legitimately ends on its own endpoint
/// components' pins, so those components are exempt; for pin-less frozen geometry
/// (GDS-imported route outlines) every component holding one of the path's ends is
/// exempt instead. Footprints are shrunk by
/// <see cref="ComponentFootprintOverlapChecker.OverlapToleranceMicrometers"/> so a path
/// grazing an edge or ending on an edge pin of an abutted neighbour stays silent.
/// Blocked fallbacks are skipped: their straight placeholder is already reported as a
/// blocked path.
/// </summary>
public class FrozenPathFootprintChecker
{
    private const double Inset = ComponentFootprintOverlapChecker.OverlapToleranceMicrometers;

    private sealed record Footprint(Component Component, double MinX, double MinY, double MaxX, double MaxY)
    {
        public bool Contains((double X, double Y) p) => p.X > MinX && p.X < MaxX && p.Y > MinY && p.Y < MaxY;
    }

    /// <summary>
    /// Checks every frozen path reachable from <paramref name="placedItems"/> against every
    /// leaf footprint reachable from them and returns one
    /// <see cref="DesignIssueType.WaveguideThroughComponent"/> issue per (path, component) pair.
    /// </summary>
    /// <param name="placedItems">Top-level components and groups of the design.</param>
    /// <returns>The offending path/component pairs, empty when every frozen route stands clear.</returns>
    public List<DesignIssue> Check(IEnumerable<Component> placedItems)
    {
        ArgumentNullException.ThrowIfNull(placedItems);

        var hierarchy = DesignHierarchy.From(placedItems);
        var footprints = hierarchy.Leaves
            .Where(ComponentFootprintOverlapChecker.HasCheckableFootprint)
            .Select(ToInsetFootprint)
            .Where(f => f.MaxX > f.MinX && f.MaxY > f.MinY)
            .ToList();

        var issues = new List<DesignIssue>();
        foreach (var path in hierarchy.FrozenPaths)
        {
            if (path.Frozen.Path is not { Segments.Count: > 0, IsBlockedFallback: false } routed)
                continue;
            CheckPath(path, routed, footprints, issues);
        }
        return issues;
    }

    private static void CheckPath(
        HierarchicalFrozenPath path, RoutedPath routed, List<Footprint> footprints, List<DesignIssue> issues)
    {
        var bounds = routed.Segments.Select(s => PathSegmentBounds.Of(s)).ToList();
        double minX = bounds.Min(b => b.MinX), minY = bounds.Min(b => b.MinY);
        double maxX = bounds.Max(b => b.MaxX), maxY = bounds.Max(b => b.MaxY);
        var exempt = ExemptComponents(path, routed, footprints);

        foreach (var footprint in footprints)
        {
            if (footprint.MinX > maxX || footprint.MaxX < minX || footprint.MinY > maxY || footprint.MaxY < minY)
                continue;
            if (exempt.Contains(footprint.Component))
                continue;
            if (!PathIntersectionDetector.IntersectsRectangle(
                    routed, footprint.MinX, footprint.MinY, footprint.MaxX, footprint.MaxY))
                continue;
            issues.Add(CreateIssue(path, footprint, (minX, minY, maxX, maxY)));
        }
    }

    /// <summary>
    /// The components a frozen path may touch: its (leaf-resolved) endpoint pins' owners,
    /// or — for pin-less geometry — every component holding one of the path's ends.
    /// </summary>
    private static HashSet<Component> ExemptComponents(
        HierarchicalFrozenPath path, RoutedPath routed, List<Footprint> footprints)
    {
        var exempt = new HashSet<Component>(ReferenceEqualityComparer.Instance);
        foreach (var pin in new[] { path.Frozen.StartPin, path.Frozen.EndPin })
        {
            if (pin is not null)
                exempt.Add(DesignHierarchy.ResolveToLeafPin(pin).ParentComponent);
        }

        bool startPinless = path.Frozen.StartPin is null;
        bool endPinless = path.Frozen.EndPin is null;
        if (!startPinless && !endPinless)
            return exempt;

        var start = (routed.Segments[0].StartPoint.X, routed.Segments[0].StartPoint.Y);
        var end = (routed.Segments[^1].EndPoint.X, routed.Segments[^1].EndPoint.Y);
        foreach (var footprint in footprints)
        {
            if ((startPinless && footprint.Contains(start)) || (endPinless && footprint.Contains(end)))
                exempt.Add(footprint.Component);
        }
        return exempt;
    }

    private static Footprint ToInsetFootprint(Component component) =>
        new(component,
            component.PhysicalX + Inset,
            component.PhysicalY + Inset,
            component.PhysicalX + component.WidthMicrometers - Inset,
            component.PhysicalY + component.HeightMicrometers - Inset);

    private static DesignIssue CreateIssue(
        HierarchicalFrozenPath path, Footprint footprint, (double MinX, double MinY, double MaxX, double MaxY) pathBounds)
    {
        // Center of the region shared by the path's extent and the footprint: on screen
        // that is where the waveguide runs through the component.
        double x = (Math.Max(footprint.MinX, pathBounds.MinX) + Math.Min(footprint.MaxX, pathBounds.MaxX)) / 2;
        double y = (Math.Max(footprint.MinY, pathBounds.MinY) + Math.Min(footprint.MaxY, pathBounds.MaxY)) / 2;
        var component = footprint.Component;
        var componentName = DesignHierarchy.QualifyName(component, component.HumanReadableName ?? component.Identifier);
        return new DesignIssue(
            DesignIssueType.WaveguideThroughComponent,
            connection: null,
            x,
            y,
            $"Frozen waveguide in group '{path.GroupIdentifierPath}' passes through component '{componentName}'",
            localizationKey: "DesignChecks.WaveguideThroughComponent",
            localizationArgs: new object[] { path.GroupIdentifierPath, componentName });
    }
}
