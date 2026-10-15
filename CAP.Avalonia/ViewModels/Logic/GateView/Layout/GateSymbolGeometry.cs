using Avalonia;
using CAP_Core.Analysis.LogicAnalysis.GateView;

namespace CAP.Avalonia.ViewModels.Logic.GateView.Layout;

/// <summary>
/// Where one gate symbol sits: its body rectangle and the free ends of its pin stubs
/// (where nets attach), all in world µm. Inputs enter on the left, outputs leave on the
/// right — the schematic convention, and the side the physical MMI pins sit on too.
/// </summary>
/// <param name="Node">The gate this symbol stands for.</param>
/// <param name="Body">The symbol body.</param>
/// <param name="InputPins">Free end of each input stub, in pin order.</param>
/// <param name="OutputPins">Free end of each output stub, in pin order.</param>
public sealed record GateSymbolPlacement(
    GateViewNode Node, Rect Body, IReadOnlyList<Point> InputPins, IReadOnlyList<Point> OutputPins)
{
    /// <summary>Length of the pin stubs left and right of the body.</summary>
    public double StubLength => GateSymbolGeometry.StubLengthFor(Body.Height);

    /// <summary>Body plus pin stubs — the area nets must not cross.</summary>
    public Rect Footprint => new(Body.X - StubLength, Body.Y, Body.Width + 2 * StubLength, Body.Height);
}

/// <summary>
/// Sizes and places gate symbols at the centre of their physical gate group, so the gate
/// view keeps the spatial map of the Layout workspace. The body fills most of the group's
/// height (within a readable range) at a per-symbol aspect ratio.
/// </summary>
public static class GateSymbolGeometry
{
    /// <summary>Smallest symbol body height in µm, so tiny copy cells stay legible.</summary>
    public const double MinBodyHeight = 90;

    /// <summary>Largest symbol body height in µm.</summary>
    public const double MaxBodyHeight = 170;

    /// <summary>Share of the group height the body fills.</summary>
    public const double HeightFill = 0.8;

    /// <summary>Pin stub length as a share of the body height.</summary>
    private const double StubShare = 0.22;

    /// <summary>Minimum pin pitch in µm before the body grows to fit more pins.</summary>
    private const double MinPinPitch = 22;

    /// <summary>Pin stub length for a body of the given height.</summary>
    public static double StubLengthFor(double bodyHeight) => bodyHeight * StubShare;

    /// <summary>Places the symbol of <paramref name="node"/> inside its group's bounds.</summary>
    /// <param name="node">The gate.</param>
    /// <param name="groupBounds">The world bounds of the gate's physical group.</param>
    public static GateSymbolPlacement Place(GateViewNode node, Rect groupBounds)
    {
        ArgumentNullException.ThrowIfNull(node);
        var pinCount = Math.Max(node.Inputs.Count, node.Outputs.Count);
        var height = Math.Clamp(groupBounds.Height * HeightFill, MinBodyHeight, MaxBodyHeight);
        height = Math.Max(height, (pinCount + 1) * MinPinPitch);
        var width = height * AspectOf(node.Kind);
        var center = groupBounds.Center;
        var body = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
        var stub = StubLengthFor(height);
        return new GateSymbolPlacement(
            node,
            body,
            PinColumn(body, node.Inputs.Count, body.Left - stub),
            PinColumn(body, node.Outputs.Count, body.Right + stub));
    }

    /// <summary>Width-to-height ratio of a symbol body.</summary>
    public static double AspectOf(GateSymbolKind kind) => kind switch
    {
        GateSymbolKind.Not or GateSymbolKind.Buffer => 0.95,
        GateSymbolKind.FanOut => 0.8,
        GateSymbolKind.Register => 1.0,
        GateSymbolKind.Unknown => 1.1,
        _ => 1.3,
    };

    /// <summary>Evenly spaced pin ends along one body side.</summary>
    private static List<Point> PinColumn(Rect body, int count, double x)
    {
        var pins = new List<Point>(count);
        for (var i = 0; i < count; i++)
            pins.Add(new Point(x, body.Top + body.Height * (i + 1) / (count + 1)));
        return pins;
    }
}
