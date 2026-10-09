using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.CrossingInsertion;

namespace UnitTests.Integration;

/// <summary>
/// Example-authoring step for a top-level wire that ends deep inside a group (a RAM word
/// cell's gate pin behind the cell's frozen wiring): a route can never pass a group's frozen
/// paths, so the wire stays blocked however open the rest of the way is. A short straight
/// feed-through is placed just inside the group's edge facing the wire's other end (the group's
/// outline does not grow) and becomes part of the group; a lead from the gate pin to the feed-through is routed inside the group (where
/// the group's own wires are ordinary, crossable wires); the top-level wire then only has to
/// reach the feed-through. A two-pin feed-through is transparent to the logic network. Every
/// step is undone when the top-level wire still cannot connect.
/// </summary>
internal static class GroupPortLeadBake
{
    /// <summary>Distance (µm) of the feed-through from the group's outline, inside it.</summary>
    private const double EdgeGapMicrometers = 20;

    /// <summary>Vertical search step and range (µm) for a free feed-through row.</summary>
    private const double RowStepMicrometers = 12;
    private const double RowSearchMicrometers = 240;

    /// <summary>
    /// Tries the feed-through for every blocked top-level wire with an end inside a group.
    /// Returns the wires connected and the top-level crossings placed for them.
    /// </summary>
    public static (int Connected, List<Component> Placed) Run(
        WaveguideConnectionManager manager, WaveguideRouter router, IReadOnlyList<ComponentGroup> topLevelGroups,
        ComponentTemplate feedThroughTemplate, Func<Component?> crossingFactory, CrossingRouteSettings settings)
    {
        var placed = new List<Component>();
        int connected = 0;
        var blockedWires = manager.Connections.Where(c => c.IsBlockedFallback).ToList();
        foreach (var wire in blockedWires)
            router.PathfindingGrid!.RemoveWaveguideObstacle(wire.Id);
        foreach (var wire in blockedWires)
        {
            foreach (var pin in new[] { wire.EndPin, wire.StartPin })
            {
                var group = topLevelGroups.FirstOrDefault(g => g.GetAllComponentsRecursive().Contains(pin.ParentComponent));
                if (group == null) continue;
                if (TryLead(wire, pin, group, manager, router, feedThroughTemplate, crossingFactory, settings, placed))
                {
                    connected++;
                    break;
                }
            }
        }
        foreach (var wire in blockedWires.Where(c => manager.Connections.Contains(c) && c.IsBlockedFallback))
            router.PathfindingGrid!.AddWaveguideObstacle(wire.Id, wire.RoutedPath!.Segments, manager.WaveguideWidthMicrometers);
        return (connected, placed);
    }

    private static bool TryLead(
        WaveguideConnection wire, PhysicalPin innerEnd, ComponentGroup group, WaveguideConnectionManager manager,
        WaveguideRouter router, ComponentTemplate template, Func<Component?> crossingFactory, CrossingRouteSettings settings,
        List<Component> placed)
    {
        var outerEnd = wire.StartPin == innerEnd ? wire.EndPin : wire.StartPin;
        var (ox, _) = outerEnd.GetAbsolutePosition();
        bool leftSide = ox < group.PhysicalX;
        if (!leftSide && ox <= group.PhysicalX + group.WidthMicrometers) return false;

        var oldPaths = group.InternalPaths.ToList();
        var oldChildren = group.ChildComponents.ToList();
        var outer = RouteLeadInside(group, innerEnd, leftSide, wire.EndPin == innerEnd, template, crossingFactory, router, manager);
        if (outer == null) return false;

        router.RemoveComponentObstacle(group);
        router.AddComponentObstacle(group);
        var shortWire = CrossingPlacement.CreateSubConnection(wire,
            wire.StartPin == innerEnd ? outer : wire.StartPin, wire.EndPin == innerEnd ? outer : wire.EndPin);
        shortWire.RestoreCachedPath(Placeholder(shortWire));
        lock (manager.SyncRoot)
        {
            manager.Connections.Remove(wire);
            manager.Connections.Add(shortWire);
        }
        var crossings = new CrossingChainInserter().TryInsert(shortWire, manager, router, crossingFactory, settings, CancellationToken.None);
        if (crossings != null)
        {
            placed.AddRange(crossings);
            return true;
        }

        lock (manager.SyncRoot)
        {
            manager.Connections.Remove(shortWire);
            manager.Connections.Add(wire);
        }
        router.RemoveComponentObstacle(group);
        foreach (var path in group.InternalPaths.ToList())
            group.RemoveInternalPath(path);
        group.AddInternalPaths(oldPaths);
        group.RemoveChildren(group.ChildComponents.Except(oldChildren).ToList());
        router.AddComponentObstacle(group);
        return false;
    }

    /// <summary>
    /// Places the feed-through just inside the group's edge and routes the lead from the gate
    /// pin to it inside the group; writes both back on success and returns the feed-through's
    /// outward pin (null when no free spot or no lead was found).
    /// </summary>
    private static PhysicalPin? RouteLeadInside(
        ComponentGroup group, PhysicalPin gatePin, bool leftSide, bool towardGate, ComponentTemplate template,
        Func<Component?> crossingFactory, WaveguideRouter mainRouter, WaveguideConnectionManager mainManager)
    {
        var topLevelWires = mainManager.Connections
            .Where(c => !c.IsBlockedFallback && c.RoutedPath != null).Select(c => c.RoutedPath!).ToList();
        var (router, manager) = GroupCrossingChainBake.CreateScratch(
            group, Array.Empty<Component>(), mainRouter.MinBendRadiusMicrometers, mainManager.WaveguideWidthMicrometers, topLevelWires);
        var feed = PlaceFeedThrough(group, gatePin, leftSide, router.PathfindingGrid!, template);
        if (feed == null) return null;
        router.AddComponentObstacle(feed);
        var outer = feed.PhysicalPins.First(p => Math.Abs(p.GetAbsoluteAngle() - (leftSide ? 180 : 0)) < 1);
        var feedPin = feed.PhysicalPins.First(p => p != outer);

        var before = manager.Connections.ToDictionary(c => c, c => c.RoutedPath);
        var lead = new WaveguideConnection { StartPin = towardGate ? feedPin : gatePin, EndPin = towardGate ? gatePin : feedPin };
        lead.RestoreCachedPath(Placeholder(lead));
        manager.Connections.Add(lead);
        var placedInside = new List<Component>();
        GroupCrossingChainBake.ScratchService(crossingFactory, placedInside).ConnectBlockedWiresThroughCrossings(manager, router);
        bool leadConnected = manager.Connections.Any(c => !c.IsBlockedFallback && (c.StartPin == feedPin || c.EndPin == feedPin))
            && !manager.Connections.Any(c => c.IsBlockedFallback && (c.StartPin == gatePin || c.EndPin == gatePin));
        if (!leadConnected
            || ForeignWireFence.AnyCrossing(GroupCrossingChainBake.NewRoutes(manager, before), topLevelWires))
            return null;
        GroupCrossingChainBake.WriteBack(group, manager, placedInside);
        group.AddChild(feed);
        return outer;
    }

    /// <summary>
    /// A feed-through just inside the group's edge on the side facing the wire's other end — so
    /// the group's outline does not grow over its surroundings — on the gate pin's row or the
    /// nearest row free in the group's own grid. Null when no free row is found.
    /// </summary>
    private static Component? PlaceFeedThrough(ComponentGroup group, PhysicalPin gatePin, bool leftSide,
                                               PathfindingGrid groupGrid, ComponentTemplate template)
    {
        var (_, gy) = gatePin.GetAbsolutePosition();
        double x = leftSide
            ? group.PhysicalX + EdgeGapMicrometers
            : group.PhysicalX + group.WidthMicrometers - EdgeGapMicrometers - template.WidthMicrometers;
        for (double offset = 0; offset <= RowSearchMicrometers; offset += RowStepMicrometers)
        {
            foreach (double dy in offset == 0 ? new[] { 0.0 } : new[] { -offset, offset })
            {
                double y = gy - template.HeightMicrometers / 2 + dy;
                if (!IsFree(groupGrid, x, y, template.WidthMicrometers, template.HeightMicrometers)) continue;
                var feed = ComponentTemplates.CreateFromTemplate(template, x, y);
                feed.PhysicalX = x;
                feed.PhysicalY = y;
                return feed;
            }
        }
        return null;
    }

    private static bool IsFree(PathfindingGrid grid, double x, double y, double width, double height)
    {
        double margin = grid.CellSizeMicrometers * 2;
        var (gx1, gy1) = grid.PhysicalToGrid(x - margin, y - margin);
        var (gx2, gy2) = grid.PhysicalToGrid(x + width + margin, y + height + margin);
        for (int gx = gx1; gx <= gx2; gx++)
        for (int gy = gy1; gy <= gy2; gy++)
            if (grid.GetCellState(gx, gy) != 0) return false;
        return true;
    }

    private static RoutedPath Placeholder(WaveguideConnection connection)
    {
        var (sx, sy) = connection.StartPin.GetAbsolutePosition();
        var (ex, ey) = connection.EndPin.GetAbsolutePosition();
        var path = new RoutedPath { IsBlockedFallback = true };
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, connection.StartPin.GetAbsoluteAngle()));
        return path;
    }
}
