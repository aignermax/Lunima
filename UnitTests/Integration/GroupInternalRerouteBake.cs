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
                           double waveguideWidthMicrometers, (double Width, double Height) chip,
                           IReadOnlySet<PhysicalPin> topLevelPins, IReadOnlySet<PhysicalPin> blockedTopLevelPins,
                           Action<string> log)
    {
        int changed = group.ChildComponents.OfType<ComponentGroup>().ToList()
            .Sum(child => Bake(child, crossingFactory, bendRadiusMicrometers, waveguideWidthMicrometers, chip,
                topLevelPins, blockedTopLevelPins, log));
        changed += RemoveOrphanCrossings(group, log);
        if (group.InternalPaths.Count == 0 || group.InternalPaths.Any(p => p.StartPin == null || p.EndPin == null))
            return changed;
        int before = group.InternalPaths.Count(p => p.Path.IsBlockedFallback);
        int crossingsBefore = CrossingPairs(group.InternalPaths.Where(p => !p.Path.IsBlockedFallback).Select(p => p.Path).ToList());
        var members = group.GetAllComponentsRecursive().ToHashSet();
        bool sealsTopLevelWire = blockedTopLevelPins.Any(p => members.Contains(p.ParentComponent));
        if (before == 0 && crossingsBefore == 0 && !sealsTopLevelWire)
            return changed;

        var (router, manager) = CreateScratch(group, bendRadiusMicrometers, waveguideWidthMicrometers, chip);
        var lanes = KeepTopLevelExitsOpen(group, router, topLevelPins, waveguideWidthMicrometers);
        var dissolved = DissolveChains(manager, router);
        foreach (var connection in manager.Connections.Where(c => !IsChainPiece(c)).ToList())
        {
            connection.IsRouteFrozen = false;
            manager.RequestReroute(connection);
        }
        manager.RecalculateAllTransmissions();
        int routedBlocked = manager.Connections.Count(c => c.IsBlockedFallback);

        var placed = new List<Component>();
        if (routedBlocked > 0)
        {
            var service = GroupCrossingChainBake.ScratchService(crossingFactory, placed);
            var inserter = (CrossingChainInserter?)typeof(CrossingInsertionService)
                .GetField("_chainInserter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(service);
            var reasons = new Dictionary<string, int>();
            if (inserter != null) inserter.Rejected = reason => reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            service.ConnectBlockedWiresThroughCrossings(manager, router);
            if (manager.Connections.Any(c => c.IsBlockedFallback))
            {
                // A second chain sweep on the plain grid: a breakout region reserved for a
                // sibling can be exactly where the leftover wire's crossing has to land.
                router.UsePinBreakouts = false;
                router.ClearPinBreakouts();
                manager.UnroutableWires.Clear();
                service.ConnectBlockedWiresThroughCrossings(manager, router);
            }
            if (reasons.Count > 0)
                log($"[group-bake]   {group.GroupName} chain rejections: {string.Join(", ", reasons.Select(r => $"{r.Key}×{r.Value}"))}");
        }
        int after = manager.Connections.Count(c => c.IsBlockedFallback);
        int crossingsAfter = CrossingPairs(manager.Connections.Where(c => !c.IsBlockedFallback && c.RoutedPath != null)
            .Select(c => c.RoutedPath!).ToList());
        // A group that seals a top-level wire is rewritten whenever its own wiring stays
        // crossing-free: the fenced exits are the gain, and the caller keeps the result only
        // when the whole design ends up with fewer problems.
        bool better = sealsTopLevelWire ? crossingsAfter <= crossingsBefore
                    : after < before ? crossingsAfter <= crossingsBefore
                    : after == before && crossingsAfter < crossingsBefore;
        int laneCrossings = manager.Connections.Where(c => !c.IsBlockedFallback && c.RoutedPath != null)
            .Sum(c => lanes.Count(l => PathIntersectionDetector.Crosses(c.RoutedPath!, l)));
        log($"[group-bake] {group.GroupName}: exit lanes {lanes.Count} (crossed {laneCrossings}), internal blocked {before} → {after} (routing {routedBlocked}, " +
            $"crossings placed {placed.Count}), crossing pairs {crossingsBefore} → {crossingsAfter}{(better ? "" : " — kept")}");
        if (!better)
            return changed;

        GroupCrossingChainBake.WriteBack(group, manager, placed);
        foreach (var crossing in dissolved)
            group.RemoveChild(crossing);
        return changed + 1;
    }

    /// <summary>A scratch router over the group's children, clipped to the chip, with the group's frozen paths as connections.</summary>
    private static (WaveguideRouter Router, WaveguideConnectionManager Manager) CreateScratch(
        ComponentGroup group, double bendRadiusMicrometers, double waveguideWidthMicrometers, (double Width, double Height) chip)
    {
        var children = group.ChildComponents.ToList();
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = bendRadiusMicrometers,
            UsePinBreakouts = Environment.GetEnvironmentVariable("CAP_BAKE_NO_BREAKOUT") != "1",
        };
        router.InitializePathfindingGrid(
            Math.Max(0, children.Min(c => c.PhysicalX) - GridMarginMicrometers),
            Math.Max(0, children.Min(c => c.PhysicalY) - GridMarginMicrometers),
            Math.Min(chip.Width, children.Max(c => c.PhysicalX + c.WidthMicrometers) + GridMarginMicrometers),
            Math.Min(chip.Height, children.Max(c => c.PhysicalY + c.HeightMicrometers) + GridMarginMicrometers),
            children);
        router.PathfindingGrid!.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
        // Parallel cascade attempts route on grid clones rebuilt from components only — they
        // would not see the exit lanes fenced below.
        var manager = new WaveguideConnectionManager(router)
        {
            WaveguideWidthMicrometers = waveguideWidthMicrometers,
            UseParallelOrderingCascade = false,
        };
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

    /// <summary>
    /// Fences a straight lane from every pin a top-level wire ends at out to the edge of the
    /// scratch grid: the group's own wires must leave the way out open (a wire running
    /// along the group's edge would otherwise seal the pin from the outside for good).
    /// </summary>
    private static List<RoutedPath> KeepTopLevelExitsOpen(ComponentGroup group, WaveguideRouter router,
        IReadOnlySet<PhysicalPin> topLevelPins, double waveguideWidthMicrometers)
    {
        var members = group.GetAllComponentsRecursive().ToHashSet();
        var children = group.ChildComponents.ToList();
        double minX = children.Min(c => c.PhysicalX), minY = children.Min(c => c.PhysicalY);
        double maxX = children.Max(c => c.PhysicalX + c.WidthMicrometers), maxY = children.Max(c => c.PhysicalY + c.HeightMicrometers);
        var lanes = new List<RoutedPath>();
        foreach (var pin in topLevelPins.Where(p => members.Contains(p.ParentComponent)))
        {
            var (x, y) = pin.GetAbsolutePosition();
            double rad = pin.GetAbsoluteAngle() * Math.PI / 180.0;
            double dx = Math.Round(Math.Cos(rad), 9), dy = Math.Round(Math.Sin(rad), 9);
            double reach = dx > 0 ? maxX - x : dx < 0 ? x - minX : dy > 0 ? maxY - y : y - minY;
            reach = Math.Max(0, reach) + GridMarginMicrometers;
            var lane = new RoutedPath();
            lane.Segments.Add(new StraightSegment(x + dx * ComponentClearanceMicrometers, y + dy * ComponentClearanceMicrometers,
                x + dx * reach, y + dy * reach, pin.GetAbsoluteAngle()));
            lanes.Add(lane);
        }
        ForeignWireFence.Mark(router.PathfindingGrid!, lanes, waveguideWidthMicrometers);
        return lanes;
    }

    /// <summary>
    /// Merges every crossing chain of the scratch design back into whole wires (the crossing
    /// cells leave the scratch grid), so they re-route pin to pin like every other wire; the
    /// crossing pass places new crossings where they are still needed. Returns the cells taken out.
    /// </summary>
    private static List<Component> DissolveChains(WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var dissolved = new List<Component>();
        var stuck = new HashSet<WaveguideConnection>();
        while (manager.Connections.FirstOrDefault(c => IsChainPiece(c) && !stuck.Contains(c)) is { } piece)
        {
            var merged = ChainDissolver.TryDissolveChain(piece, manager, router, out var gone, out _,
                CrossingComponentCatalog.IsCrossing);
            if (merged == null)
            {
                stuck.Add(piece);
                continue;
            }
            dissolved.AddRange(gone);
        }
        return dissolved;
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
