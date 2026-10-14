using Avalonia;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.ViewModels.Logic.GateView.Layout;

/// <summary>
/// Lays a <see cref="GateViewGraph"/> out over the live physical positions: each symbol at
/// its gate group, each net routed between symbol pins, every non-gate element that
/// matters (cell wrappers, sources) as a box. Pure and cheap — the canvas rebuilds it
/// whenever a gate moves, so dragging a gate drags its symbol and nets along.
/// </summary>
public static class GateViewSceneBuilder
{
    private const int TwoPinPassThrough = 2;

    /// <summary>Builds the scene.</summary>
    /// <param name="graph">The gate graph.</param>
    /// <param name="topLevel">The design's top-level components (for non-gate boxes).</param>
    /// <param name="groupBounds">World bounds of a group (the canvas's group-frame rule).</param>
    public static GateViewScene Build(
        GateViewGraph graph, IEnumerable<Component> topLevel, Func<ComponentGroup, Rect> groupBounds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var symbols = graph.Nodes.Select(node => GateSymbolGeometry.Place(node, groupBounds(node.Group))).ToList();
        var byId = symbols.ToDictionary(symbol => symbol.Node.GateId);
        var tracks = new GateTrackRegistry();
        var obstacles = symbols.Select(symbol => symbol.Footprint).ToList();
        var routes = graph.Nets.Select(net => RouteNet(net, byId, tracks, obstacles)).ToList();
        return new GateViewScene(symbols, routes, Blocks(graph, topLevel, groupBounds));
    }

    private static GateNetRoute RouteNet(
        GateViewNet net,
        IReadOnlyDictionary<string, GateSymbolPlacement> byId,
        GateTrackRegistry tracks,
        IReadOnlyList<Rect> obstacles)
    {
        var loads = net.Loads
            .Select(load => (Load: load, Symbol: byId.GetValueOrDefault(load.GateId)))
            .Where(pair => pair.Symbol != null)
            .Select(pair => (pair.Load, Symbol: pair.Symbol!, Pin: InputPin(pair.Symbol!, pair.Load)))
            .Where(pair => pair.Pin != null)
            .ToList();
        if (net.Driver == null || !byId.TryGetValue(net.Driver.GateId, out var source))
            return new GateNetRoute(net, [], [], loads.Select(l => new GatePortFlag(l.Pin!.Value, IsOutput: false)).ToList());

        var start = OutputPin(source, net.Driver);
        if (start == null)
            return new GateNetRoute(net, [], [], []);
        if (loads.Count == 0)
            return new GateNetRoute(net, [], [], [new GatePortFlag(start.Value, IsOutput: true)]);

        var trunk = ClaimTrunk(start.Value, source.StubLength, loads.Select(l => l.Symbol.Footprint), tracks);
        var wires = loads.Select(load => GateNetRouter.Route(
            new GateNetRouter.Request(start.Value, load.Pin!.Value, load.Symbol.Footprint, source.StubLength, trunk),
            obstacles, tracks)).ToList();
        var junctions = wires.Count > 1 ? new List<Point> { wires[0][1] } : new List<Point>();
        return new GateNetRoute(net, wires, junctions, []);
    }

    /// <summary>
    /// Claims the net's vertical trunk right of the driver, spanning every load row (and
    /// the channels next to them), so all branches of a fan-out share one trunk.
    /// </summary>
    private static double ClaimTrunk(Point start, double gap, IEnumerable<Rect> loadFootprints, GateTrackRegistry tracks)
    {
        var top = start.Y;
        var bottom = start.Y;
        foreach (var footprint in loadFootprints)
        {
            top = Math.Min(top, footprint.Top - gap);
            bottom = Math.Max(bottom, footprint.Bottom + gap);
        }
        tracks.AddHorizontal(start.Y, start.X, GateNetRouter.PreferredTrunkX(start, gap));
        return tracks.ClaimVertical(
            GateNetRouter.PreferredTrunkX(start, gap), top, bottom, GateNetRouter.LaneStep(gap), direction: 1);
    }

    private static Point? InputPin(GateSymbolPlacement symbol, LogicPinRef pin)
    {
        var index = IndexOf(symbol.Node.Inputs, pin.PinName);
        return index < 0 ? null : symbol.InputPins[index];
    }

    private static Point? OutputPin(GateSymbolPlacement symbol, LogicPinRef pin)
    {
        var index = IndexOf(symbol.Node.Outputs, pin.PinName);
        return index < 0 ? null : symbol.OutputPins[index];
    }

    private static int IndexOf(IReadOnlyList<GateViewPin> pins, string name)
    {
        for (var i = 0; i < pins.Count; i++)
        {
            if (pins[i].Name == name)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Boxes for top-level elements that are not gates themselves: wrapper cells (their
    /// nested gates draw inside) and active elements such as sources. Crossings and other
    /// two-port pass-through optics vanish — at gate level they are just wire.
    /// </summary>
    private static List<GateViewBlock> Blocks(
        GateViewGraph graph, IEnumerable<Component> topLevel, Func<ComponentGroup, Rect> groupBounds)
    {
        var gateGroups = graph.Nodes.Select(node => node.Group).ToHashSet();
        var blocks = new List<GateViewBlock>();
        foreach (var component in topLevel)
        {
            if (component is ComponentGroup group)
            {
                if (!gateGroups.Contains(group))
                    blocks.Add(new GateViewBlock(group.GroupName, groupBounds(group), group));
                continue;
            }
            if (CrossingComponentCatalog.IsCrossing(component) || component.PhysicalPins.Count == TwoPinPassThrough)
                continue;
            blocks.Add(new GateViewBlock(
                component.HumanReadableName ?? component.Identifier,
                new Rect(component.PhysicalX, component.PhysicalY, component.WidthMicrometers, component.HeightMicrometers),
                component));
        }
        return blocks;
    }
}
