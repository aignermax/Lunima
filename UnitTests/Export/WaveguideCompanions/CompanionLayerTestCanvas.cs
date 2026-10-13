using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_Core.Tiles;

namespace UnitTests.Export.WaveguideCompanions;

/// <summary>
/// Synthetic canvases for the waveguide companion-layer tests: two components joined by a
/// cached route of straight → 90° arc → straight, and a process spec with a trench band.
/// </summary>
internal static class CompanionLayerTestCanvas
{
    /// <summary>GDS layer of the synthetic trench band.</summary>
    public const int TrenchLayer = 2;

    /// <summary>Growth of the synthetic trench band beyond each core edge, in µm.</summary>
    public const double TrenchOffsetMicrometers = 8.0;

    /// <summary>A trench band of <see cref="TrenchOffsetMicrometers"/> on (2, 0).</summary>
    public static WaveguideCompanionSpec TrenchSpec(WaveguideCompanionRole role = WaveguideCompanionRole.Trench) =>
        new(new[] { new WaveguideCompanionLayer("TRENCH", TrenchLayer, 0, role, TrenchOffsetMicrometers, null) });

    /// <summary>
    /// A canvas with A at (0, 0) and B at (120, 120), connected by straight (0,0)→(100,0),
    /// 90° arc of radius 20 to (120, 20), straight to (120, 120). Every waveguide resolves
    /// to <paramref name="spec"/> (null: no provider at all).
    /// </summary>
    public static (DesignCanvasViewModel Canvas, WaveguideConnection Connection) Create(
        WaveguideCompanionSpec? spec, bool electrical = false)
    {
        var canvas = new DesignCanvasViewModel();
        if (spec != null)
            canvas.WaveguideCompanionSpecProvider = (_, _) => spec;

        var a = ComponentAt("A", 0, 0, electrical);
        var b = ComponentAt("B", 120, 120, electrical);
        canvas.Components.Add(new ComponentViewModel(a));
        canvas.Components.Add(new ComponentViewModel(b));

        var connection = new WaveguideConnection
        {
            StartPin = a.PhysicalPins.Single(),
            EndPin = b.PhysicalPins.Single(),
        };
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        path.Segments.Add(new BendSegment(100, 20, 20, 0, 90));
        path.Segments.Add(new StraightSegment(120, 20, 120, 120, 90));
        connection.RestoreCachedPath(path);
        canvas.Connections.Add(new WaveguideConnectionViewModel(connection));
        return (canvas, connection);
    }

    private static Component ComponentAt(string identifier, double x, double y, bool electrical)
    {
        var comp = TestComponentFactory.CreateBasicComponent();
        comp.Identifier = identifier;
        comp.PhysicalX = x;
        comp.PhysicalY = y;
        comp.PhysicalPins.Add(new PhysicalPin
        {
            Name = "p0",
            ParentComponent = comp,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0,
            LogicalPin = electrical ? new Pin("p0", 0, MatterType.Electricity, RectSide.Up) : null,
        });
        return comp;
    }
}
