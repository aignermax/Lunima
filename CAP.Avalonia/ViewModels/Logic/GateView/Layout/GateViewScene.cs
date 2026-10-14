using Avalonia;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Logic.GateView.Layout;

/// <summary>A net end that leaves the gate view: a network input flag or an unconnected output.</summary>
/// <param name="Pin">The pin end the flag attaches to.</param>
/// <param name="IsOutput">True for an output flag (label right of the pin), false for an input flag.</param>
public readonly record struct GatePortFlag(Point Pin, bool IsOutput);

/// <summary>The drawable form of one net: wires, fan-out junction dots and port flags.</summary>
/// <param name="Net">The net.</param>
/// <param name="Wires">One orthogonal polyline per driver→load wire.</param>
/// <param name="Junctions">Fan-out points where branches split.</param>
/// <param name="Flags">Input flags (undriven net) or the output flag (net without loads).</param>
public sealed record GateNetRoute(
    GateViewNet Net, IReadOnlyList<Point[]> Wires, IReadOnlyList<Point> Junctions, IReadOnlyList<GatePortFlag> Flags)
{
    /// <summary>Where the net's label goes: just right of the driver, or the first flag.</summary>
    public Point? LabelAnchor => Wires.Count > 0 ? Wires[0][0] : Flags.Count > 0 ? Flags[0].Pin : null;
}

/// <summary>A design element that is not a gate, drawn as a plain labelled box (a cell wrapper, a laser…).</summary>
/// <param name="Name">The element's display name.</param>
/// <param name="Bounds">World bounds.</param>
/// <param name="Component">The top-level component the box stands for (selection highlight).</param>
public sealed record GateViewBlock(string Name, Rect Bounds, Component Component);

/// <summary>Everything the gate view draws in one frame, in world µm.</summary>
public sealed class GateViewScene
{
    /// <summary>Creates a scene.</summary>
    public GateViewScene(
        IReadOnlyList<GateSymbolPlacement> symbols,
        IReadOnlyList<GateNetRoute> routes,
        IReadOnlyList<GateViewBlock> blocks)
    {
        Symbols = symbols;
        Routes = routes;
        Blocks = blocks;
    }

    /// <summary>An empty scene.</summary>
    public static GateViewScene Empty { get; } = new([], [], []);

    /// <summary>Gate symbols.</summary>
    public IReadOnlyList<GateSymbolPlacement> Symbols { get; }

    /// <summary>Nets.</summary>
    public IReadOnlyList<GateNetRoute> Routes { get; }

    /// <summary>Non-gate elements.</summary>
    public IReadOnlyList<GateViewBlock> Blocks { get; }
}
