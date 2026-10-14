using System;
using System.Collections.Generic;
using System.Linq;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Dies;

/// <summary>
/// Upgrades designs saved before dies existed. Only genuine multi-chiplet designs become
/// assemblies: top-level groups joined by a cross-chiplet edge-coupler link, or top-level
/// groups bound to two or more different processes. Every other design — including one
/// whose ordinary groups merely carry a derived process binding — stays a single-die
/// design and loads exactly as before.
/// </summary>
public static class LegacyDieMigration
{
    /// <summary>
    /// Clearance added around migrated content on sides that carry no edge-coupler facet;
    /// facet sides keep the content edge so linked couplers stay exactly on the die edge.
    /// </summary>
    public const double ContentMarginMicrometers = 25.0;

    /// <summary>
    /// Turns the qualifying legacy chiplet groups into dies sized to their content.
    /// No-op when any top-level group already is a die.
    /// </summary>
    /// <returns>The groups that became dies (empty when the design stays single-die).</returns>
    public static IReadOnlyList<ComponentGroup> Migrate(
        IReadOnlyCollection<Component> topLevelComponents,
        IEnumerable<WaveguideConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(topLevelComponents);
        ArgumentNullException.ThrowIfNull(connections);

        var groups = topLevelComponents.OfType<ComponentGroup>().ToList();
        if (groups.Any(g => g.IsDie))
            return Array.Empty<ComponentGroup>();

        var chiplets = SelectLegacyChiplets(groups, connections.ToList());
        foreach (var chiplet in chiplets)
            DieOrigin.Assign(chiplet, DefinitionFromContent(chiplet));
        return chiplets;
    }

    private static List<ComponentGroup> SelectLegacyChiplets(
        List<ComponentGroup> groups, List<WaveguideConnection> connections)
    {
        var linked = new HashSet<ComponentGroup>();
        foreach (var connection in connections)
        {
            if (!ChipletInterfaceChecker.TryGetFacet(connection.StartPin, out var start)
                || !ChipletInterfaceChecker.TryGetFacet(connection.EndPin, out var end)
                || ReferenceEquals(start.Chiplet, end.Chiplet))
                continue;
            linked.Add(start.Chiplet);
            linked.Add(end.Chiplet);
        }

        var bound = groups.Where(g => g.ProcessBinding is { IsPlayground: false }).ToList();
        bool multiProcess = bound
            .Select(g => g.ProcessBinding!.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= 2;

        return groups.Where(g => linked.Contains(g) || (multiProcess && bound.Contains(g))).ToList();
    }

    /// <summary>
    /// A die outline enclosing the group's current content, with
    /// <see cref="ContentMarginMicrometers"/> of clearance on every side without a facet.
    /// </summary>
    public static DieDefinition DefinitionFromContent(ComponentGroup group)
    {
        group.UpdateGroupBounds();
        double left = group.PhysicalX + group.MinChildOffsetX;
        double top = group.PhysicalY + group.MinChildOffsetY;
        double right = left + group.WidthMicrometers;
        double bottom = top + group.HeightMicrometers;

        var facets = FacetPinPositions(group);
        double margin = ContentMarginMicrometers;
        double tolerance = ChipletInterfaceChecker.EdgeToleranceMicrometers;
        double newLeft = facets.Any(p => Math.Abs(p.X - left) <= tolerance) ? left : left - margin;
        double newTop = facets.Any(p => Math.Abs(p.Y - top) <= tolerance) ? top : top - margin;
        double newRight = facets.Any(p => Math.Abs(p.X - right) <= tolerance) ? right : right + margin;
        double newBottom = facets.Any(p => Math.Abs(p.Y - bottom) <= tolerance) ? bottom : bottom + margin;

        return new DieDefinition(
            Math.Max(DieDefinition.MinDimensionMicrometers, newRight - newLeft),
            Math.Max(DieDefinition.MinDimensionMicrometers, newBottom - newTop),
            newLeft - group.PhysicalX,
            newTop - group.PhysicalY);
    }

    private static List<(double X, double Y)> FacetPinPositions(ComponentGroup group) =>
        group.GetAllComponentsRecursive()
            .Where(EdgeCouplerFacet.IsEdgeCoupler)
            .SelectMany(c => c.PhysicalPins)
            .Select(p => p.GetAbsolutePosition())
            .ToList();
}
