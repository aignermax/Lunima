using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;

namespace UnitTests.Integration;

/// <summary>
/// Crossing bake for wires frozen inside a group (e.g. the intra-cell wires of a RAM word
/// cell): the group's frozen paths become connections on a scratch router holding only the
/// group's children, the production crossing-chain pass connects the blocked ones, and the
/// result is written back as the group's frozen paths with the placed crossings as its
/// children. Nested groups are baked first, so an outer group sees its children final.
/// </summary>
internal static class GroupCrossingChainBake
{
    /// <summary>Margin (µm) around the group's children for the scratch routing grid.</summary>
    private const double GridMarginMicrometers = 100;

    /// <summary>Obstacle clearance (µm) around components — the canvas' routing default.</summary>
    private const double ComponentClearanceMicrometers = 5.0;

    /// <summary>
    /// Connects blocked frozen paths inside <paramref name="group"/> and every group nested in
    /// it through crossings. Returns how many blocked paths were connected.
    /// </summary>
    /// <param name="group">The group whose frozen paths are baked (modified in place).</param>
    /// <param name="crossingFactory">Creates a fresh crossing component.</param>
    /// <param name="bendRadiusMicrometers">The design's minimum bend radius.</param>
    /// <param name="waveguideWidthMicrometers">The routing waveguide width.</param>
    /// <param name="topLevelWires">The design's routed top-level wires — internal routes must never cross them.</param>
    public static int Bake(ComponentGroup group, Func<Component?> crossingFactory,
                           double bendRadiusMicrometers, double waveguideWidthMicrometers,
                           IReadOnlyList<RoutedPath> topLevelWires)
    {
        int connected = group.ChildComponents.OfType<ComponentGroup>().ToList()
            .Sum(child => Bake(child, crossingFactory, bendRadiusMicrometers, waveguideWidthMicrometers, topLevelWires));
        if (!group.InternalPaths.Any(p => p.Path.IsBlockedFallback && p.StartPin != null && p.EndPin != null))
            return connected;

        var (router, manager) = CreateScratch(group, Array.Empty<Component>(), bendRadiusMicrometers, waveguideWidthMicrometers, topLevelWires);
        var before = manager.Connections.ToDictionary(c => c, c => c.RoutedPath);
        var placed = new List<Component>();
        var service = ScratchService(crossingFactory, placed);
        int groupConnected = service.ConnectBlockedWiresThroughCrossings(manager, router);
        if (groupConnected == 0 || ForeignWireFence.AnyCrossing(NewRoutes(manager, before), topLevelWires))
            return connected;
        WriteBack(group, manager, placed);
        return connected + groupConnected;
    }

    /// <summary>The routes the scratch pass created or changed.</summary>
    internal static IEnumerable<RoutedPath> NewRoutes(
        WaveguideConnectionManager manager, IReadOnlyDictionary<WaveguideConnection, RoutedPath?> before) =>
        manager.Connections
            .Where(c => !c.IsBlockedFallback && c.RoutedPath != null
                        && (!before.TryGetValue(c, out var old) || !ReferenceEquals(old, c.RoutedPath)))
            .Select(c => c.RoutedPath!);

    /// <summary>
    /// A scratch router holding the group's children (plus <paramref name="extraChildren"/>)
    /// and its frozen paths as connections, the way the group bake routes inside a group.
    /// </summary>
    internal static (WaveguideRouter Router, WaveguideConnectionManager Manager) CreateScratch(
        ComponentGroup group, IReadOnlyCollection<Component> extraChildren, double bendRadiusMicrometers, double waveguideWidthMicrometers,
        IReadOnlyList<RoutedPath> topLevelWires)
    {
        var children = group.ChildComponents.Concat(extraChildren).ToList();
        var router = new WaveguideRouter { MinBendRadiusMicrometers = bendRadiusMicrometers };
        router.InitializePathfindingGrid(
            children.Min(c => c.PhysicalX) - GridMarginMicrometers,
            children.Min(c => c.PhysicalY) - GridMarginMicrometers,
            children.Max(c => c.PhysicalX + c.WidthMicrometers) + GridMarginMicrometers,
            children.Max(c => c.PhysicalY + c.HeightMicrometers) + GridMarginMicrometers,
            children);
        router.PathfindingGrid!.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
        var manager = new WaveguideConnectionManager(router) { WaveguideWidthMicrometers = waveguideWidthMicrometers };
        foreach (var path in group.InternalPaths.Where(p => p.StartPin != null && p.EndPin != null))
            manager.Connections.Add(ToConnection(path, manager, router));
        ForeignWireFence.Mark(router.PathfindingGrid, topLevelWires, waveguideWidthMicrometers);
        return (router, manager);
    }

    /// <summary>The crossing service the scratch passes use, collecting placed crossings.</summary>
    internal static CrossingInsertionService ScratchService(Func<Component?> crossingFactory, List<Component> placed) =>
        new(crossingFactory)
        {
            ComponentAdded = placed.Add,
            ChainPassTimeBudget = TimeSpan.MaxValue,
            FallbackChainSearches = new[] { (40_000_000, 1.0), (8_000_000, 2.5) },
        };

    /// <summary>Replaces the group's frozen paths by the scratch connections and adds the placed crossings.</summary>
    internal static void WriteBack(ComponentGroup group, WaveguideConnectionManager manager, IEnumerable<Component> placed)
    {
        foreach (var path in group.InternalPaths.ToList())
            group.RemoveInternalPath(path);
        group.AddInternalPaths(manager.Connections.Select(ToFrozenPath).ToList());
        foreach (var crossing in placed)
            group.AddChild(crossing);
    }

    private static WaveguideConnection ToConnection(FrozenWaveguidePath path, WaveguideConnectionManager manager, WaveguideRouter router)
    {
        var connection = new WaveguideConnection { StartPin = path.StartPin!, EndPin = path.EndPin! };
        path.ApplySettingsTo(connection);
        connection.RestoreCachedPath(path.Path.DeepCopy());
        connection.IsRouteFrozen = true;
        if (connection.RoutedPath != null)
            router.PathfindingGrid!.AddWaveguideObstacle(connection.Id, connection.RoutedPath.Segments, manager.WaveguideWidthMicrometers);
        return connection;
    }

    private static FrozenWaveguidePath ToFrozenPath(WaveguideConnection connection)
    {
        var frozen = new FrozenWaveguidePath
        {
            Path = connection.RoutedPath!.DeepCopy(),
            StartPin = connection.StartPin,
            EndPin = connection.EndPin,
        };
        frozen.CaptureSettingsFrom(connection);
        return frozen;
    }
}
