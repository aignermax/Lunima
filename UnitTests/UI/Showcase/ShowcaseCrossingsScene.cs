using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Tiles;

namespace UnitTests.UI.Showcase;

/// <summary>
/// The staged scene of the crossings motif, all from the bundled Demo PDK: a horizontal
/// wire (left grating coupler → right grating coupler) and a vertical wire (top → bottom)
/// crossing through a Demo waveguide crossing at <see cref="CrossingCenter"/>, plus a
/// second top/bottom coupler pair at <see cref="SecondWireX"/> waiting for a third wire.
/// Every coupler is placed so its waveguide pin sits exactly on its wire's axis.
/// </summary>
internal sealed class ShowcaseCrossingsScene
{
    public const string PdkName = "Demo PDK";
    public const double WireY = 330;
    public const double TopPinY = 200;
    public const double BottomPinY = 460;
    public const double LeftPinX = 100;
    public const double RightPinX = 480;
    public const double SecondWireX = 390;
    public static readonly (double X, double Y) CrossingCenter = (260, WireY);

    /// <summary>World-space (µm) extent of the scene, for view fitting and cropping.</summary>
    public static readonly (double X, double Y, double W, double H) Extent = (-5, 95, 595, 470);

    private ShowcaseCrossingsScene(Component crossing, Component secondTop, Component secondBottom)
    {
        Crossing = crossing;
        SecondTop = secondTop;
        SecondBottom = secondBottom;
    }

    /// <summary>The crossing both wires pass through.</summary>
    public Component Crossing { get; }

    /// <summary>The coupler above the east piece, for the third wire.</summary>
    public Component SecondTop { get; }

    /// <summary>The coupler below the east piece, for the third wire.</summary>
    public Component SecondBottom { get; }

    /// <summary>Places the parts and routes both wires through the crossing.</summary>
    public static async Task<ShowcaseCrossingsScene> BuildAsync(MainViewModel vm)
    {
        var left = PlacePinAt(vm, "Grating Coupler", LeftPinX, WireY, 0);
        var right = PlacePinAt(vm, "Grating Coupler", RightPinX, WireY, 180);
        var top = PlacePinAt(vm, "Grating Coupler", CrossingCenter.X, TopPinY, 90);
        var bottom = PlacePinAt(vm, "Grating Coupler", CrossingCenter.X, BottomPinY, 270);
        var secondTop = PlacePinAt(vm, "Grating Coupler", SecondWireX, TopPinY, 90);
        var secondBottom = PlacePinAt(vm, "Grating Coupler", SecondWireX, BottomPinY, 270);

        var template = CrossingComponentInstance.FindCrossingTemplate(vm.LeftPanel.AllTemplates, new[] { PdkName })
            ?? throw new InvalidOperationException("the Demo PDK crossing must be loaded");
        var crossing = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        crossing.PhysicalX = CrossingCenter.X - crossing.WidthMicrometers / 2;
        crossing.PhysicalY = CrossingCenter.Y - crossing.HeightMicrometers / 2;
        crossing.IsInsertedCrossing = true;
        vm.Canvas.AddComponent(crossing, template.Name, template.PdkSource);

        await Connect(vm, Waveguide(left), Port(crossing, "port 1"));
        await Connect(vm, Port(crossing, "port 2"), Waveguide(right));
        await Connect(vm, Waveguide(top), Port(crossing, "port 3"));
        await Connect(vm, Port(crossing, "port 4"), Waveguide(bottom));
        return new ShowcaseCrossingsScene(crossing, secondTop, secondBottom);
    }

    /// <summary>The coupler's waveguide pin.</summary>
    public static PhysicalPin Waveguide(Component coupler) => coupler.PhysicalPins.First(p => p.Name == "waveguide");

    /// <summary>A crossing port by name.</summary>
    public static PhysicalPin Port(Component crossing, string name) => crossing.PhysicalPins.First(p => p.Name == name);

    /// <summary>Routes a wire between two pins through the production pipeline.</summary>
    public static async Task<WaveguideConnectionViewModel> Connect(MainViewModel vm, PhysicalPin from, PhysicalPin to) =>
        await vm.Canvas.ConnectPinsAsync(from, to)
        ?? throw new InvalidOperationException($"routing failed for {from.Name} → {to.Name}");

    /// <summary>
    /// Places a coupler so that its waveguide pin lands on (x, y) pointing at
    /// <paramref name="pinAngle"/>°: the rotation is found on a scratch canvas first
    /// (rotation is translation invariant, so the pin offset carries over).
    /// </summary>
    private static Component PlacePinAt(MainViewModel vm, string name, double x, double y, double pinAngle)
    {
        var template = vm.LeftPanel.AllTemplates.First(t => t.PdkSource == PdkName && t.Name == name);
        foreach (var rotation in Enum.GetValues<DiscreteRotation>())
        {
            var scratch = new DesignCanvasViewModel();
            var probe = Place(scratch, template, 0, 0, rotation);
            var pin = Waveguide(probe);
            if (Math.Abs(NormalizeDegrees(pin.GetAbsoluteAngle() - pinAngle)) > 1e-6) continue;
            var (px, py) = pin.GetAbsolutePosition();
            return Place(vm.Canvas, template, x - px, y - py, rotation);
        }
        throw new InvalidOperationException($"no rotation of {name} points its pin at {pinAngle}°");
    }

    private static Component Place(
        DesignCanvasViewModel canvas, ComponentTemplate template, double x, double y, DiscreteRotation rotation)
    {
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        var componentVm = canvas.AddComponent(component, template.Name, template.PdkSource);
        for (int i = 0; i < (int)rotation; i++)
        {
            var rotate = new RotateComponentCommand(canvas, componentVm);
            rotate.Execute();
            if (!rotate.WasApplied)
                throw new InvalidOperationException($"rotation blocked for {template.Name} at ({x}, {y})");
        }
        return component;
    }

    private static double NormalizeDegrees(double degrees) => ((degrees % 360) + 540) % 360 - 180;
}
