using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;

namespace UnitTests.Integration;

/// <summary>
/// Re-route bake for wires frozen inside a group: orphaned crossing cells (no port wired) are
/// taken out, the group's frozen paths become unfrozen connections on a scratch router that
/// holds only the group's children and is clipped to the chip, the production routing pass
/// routes them from scratch — pin breakouts included — and the crossing-chain pass connects
/// what is still blocked. Pieces of existing crossing chains keep their docked geometry.
/// The result replaces the group's frozen paths only when it has strictly fewer blocked paths
/// (and no more crossing wire pairs), or as many blocked paths and fewer crossing pairs.
/// Nested groups are baked first, so an outer group routes around its children's final
/// interiors. Top-level wires are not fenced: they are re-validated against the new
/// interiors when the design reloads.
/// </summary>
internal static class GroupInternalRerouteBake
{
    /// <summary>Margin (µm) around the group's children for the scratch routing grid.</summary>
    private const double GridMarginMicrometers = 100;

    /// <summary>Obstacle clearance (µm) around components — the canvas' routing default.</summary>
    private const double ComponentClearanceMicrometers = 5.0;

    /// <summary>Blocked frozen paths inside <paramref name="group"/> and every group nested in it.</summary>
    public static int CountBlocked(ComponentGroup group) =>
        group.InternalPaths.Count(p => p.Path.IsBlockedFallback)
        + group.ChildComponents.OfType<ComponentGroup>().Sum(CountBlocked);

    /// <summary>
    /// Re-routes the frozen paths of <paramref name="group"/> (children first). Returns how
    /// many groups were rewritten.
    /// </summary>
    public static int Bake(ComponentGroup group, Func<Component?> crossingFactory, double bendRadiusMicrometers,
                           double waveguideWidthMicrometers, (double Width, double Height) chip, Action<string> log)
    {
        int changed = group.ChildComponents.OfType<ComponentGroup>().ToList()
            .Sum(child => Bake(child, crossingFactory, bendRadiusMicrometers, waveguideWidthMicrometers, chip, log));
        changed += RemoveOrphanCrossings(group, log);
        if (group.InternalPaths.Count == 0 || group.InternalPaths.Any(p => p.StartPin == null || p.EndPin == null))
            return changed;
        int before = group.InternalPaths.Count(p => p.Path.IsBlockedFallback);
        int crossingsBefore = CrossingPairs(group.InternalPaths.Where(p => !p.Path.IsBlockedFallback).Select(p => p.Path).ToList());
        if (before == 0 && crossingsBefore == 0)
            return changed;

        var (router, manager) = CreateScratch(group, bendRadiusMicrometers, waveguideWidthMicrometers, chip);
        foreach (var connection in manager.Connections.Where(c => !IsChainPiece(c)).ToList())
        {
            connection.IsRouteFrozen = false;
            manager.RequestReroute(connection);
        }
        manager.RecalculateAllTransmissions();
        int routedBlocked = manager.Connections.Count(c => c.IsBlockedFallback);

        var placed = new List<Component>();
        if (routedBlocked > 0)
            GroupCrossingChainBake.ScratchService(crossingFactory, placed).ConnectBlockedWiresThroughCrossings(manager, router);
        int after = manager.Connections.Count(c => c.IsBlockedFallback);
        int crossingsAfter = CrossingPairs(manager.Connections.Where(c => !c.IsBlockedFallback && c.RoutedPath != null)
            .Select(c => c.RoutedPath!).ToList());
        bool better = after < before ? crossingsAfter <= crossingsBefore
                                     : after == before && crossingsAfter < crossingsBefore;
        log($"[group-bake] {group.GroupName}: internal blocked {before} → {after} (routing {routedBlocked}, " +
            $"crossings placed {placed.Count}), crossing pairs {crossingsBefore} → {crossingsAfter}{(better ? "" : " — kept")}");
        if (!better)
            return changed;

        GroupCrossingChainBake.WriteBack(group, manager, placed);
        return changed + 1;
    }

    /// <summary>A scratch router over the group's children, clipped to the chip, with the group's frozen paths as connections.</summary>
    private static (WaveguideRouter Router, WaveguideConnectionManager Manager) CreateScratch(
        ComponentGroup group, double bendRadiusMicrometers, double waveguideWidthMicrometers, (double Width, double Height) chip)
    {
        var children = group.ChildComponents.ToList();
        var router = new WaveguideRouter { MinBendRadiusMicrometers = bendRadiusMicrometers };
        router.InitializePathfindingGrid(
            Math.Max(0, children.Min(c => c.PhysicalX) - GridMarginMicrometers),
            Math.Max(0, children.Min(c => c.PhysicalY) - GridMarginMicrometers),
            Math.Min(chip.Width, children.Max(c => c.PhysicalX + c.WidthMicrometers) + GridMarginMicrometers),
            Math.Min(chip.Height, children.Max(c => c.PhysicalY + c.HeightMicrometers) + GridMarginMicrometers),
            children);
        router.PathfindingGrid!.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
        var manager = new WaveguideConnectionManager(router) { WaveguideWidthMicrometers = waveguideWidthMicrometers };
        foreach (var path in group.InternalPaths)
        {
            var connection = new WaveguideConnection { StartPin = path.StartPin!, EndPin = path.EndPin! };
            path.ApplySettingsTo(connection);
            connection.RestoreCachedPath(path.Path.DeepCopy());
            connection.IsRouteFrozen = true;
            if (connection.RoutedPath != null && !connection.IsBlockedFallback)
                router.PathfindingGrid.AddWaveguideObstacle(connection.Id, connection.RoutedPath.Segments, waveguideWidthMicrometers);
            manager.Connections.Add(connection);
        }
        return (router, manager);
    }

    /// <summary>Removes crossing cells of <paramref name="group"/> whose ports no frozen path and no group pin uses.</summary>
    private static int RemoveOrphanCrossings(ComponentGroup group, Action<string> log)
    {
        var usedPins = group.InternalPaths.SelectMany(p => new[] { p.StartPin, p.EndPin }).Where(p => p != null).ToHashSet();
        var orphans = group.ChildComponents
            .Where(c => CrossingComponentCatalog.IsCrossing(c) && !c.PhysicalPins.Any(usedPins.Contains)
                        && !group.ExternalPins.Any(e => c.PhysicalPins.Contains(e.InternalPin)))
            .ToList();
        foreach (var orphan in orphans)
            group.RemoveChild(orphan);
        if (orphans.Count > 0)
            log($"[group-bake] {group.GroupName}: removed {orphans.Count} orphaned crossing cell(s)");
        return orphans.Count > 0 ? 1 : 0;
    }

    /// <summary>Pairs of routes that properly cross each other.</summary>
    private static int CrossingPairs(List<RoutedPath> routes)
    {
        int count = 0;
        for (int i = 0; i < routes.Count; i++)
            for (int j = i + 1; j < routes.Count; j++)
                if (PathIntersectionDetector.Crosses(routes[i], routes[j]))
                    count++;
        return count;
    }

    private static bool IsChainPiece(WaveguideConnection connection) =>
        CrossingComponentCatalog.IsCrossing(connection.StartPin.ParentComponent)
        || CrossingComponentCatalog.IsCrossing(connection.EndPin.ParentComponent);
}
