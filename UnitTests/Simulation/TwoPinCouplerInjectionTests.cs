using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// A coupler with more than one optical pin (the two-pin SiEPIC grating coupler
/// with a fiber-side and a waveguide-side pin) must inject only on the
/// off-chip / fiber-side pin. Injecting on the connected waveguide-side pin as
/// well feeds power through that pin's reflection matrix entry and
/// double-counts the source.
/// </summary>
public class TwoPinCouplerInjectionTests
{
    [Fact]
    public void TwoPinCoupler_WaveguidePinConnected_InjectsOnlyOnFiberPin()
    {
        var canvas = new DesignCanvasViewModel();
        var coupler = CreateGratingCoupler("Grating Coupler In", 0, 0, pinCount: 2);
        var mmi = CreateMmi("MMI1", 100, 0);
        canvas.AddComponent(coupler);
        canvas.AddComponent(mmi);

        // port 2 (index 0, waveguide side) is wired into the circuit;
        // port 1 (index 1, fiber side) stays off-chip.
        canvas.ConnectPins(coupler.PhysicalPins[0], mmi.PhysicalPins[0]);

        var portManager = new PhysicalExternalPortManager();
        var sources = new SimulationService().ConfigureLightSources(canvas, portManager);

        sources.Count.ShouldBe(1, "a two-pin coupler must inject exactly once");
        var usedInputs = portManager.GetUsedExternalInputs().ToList();
        usedInputs.Count.ShouldBe(1);
        usedInputs[0].AttachedComponentPinId.ShouldBe(
            coupler.PhysicalPins[1].LogicalPin!.IDInFlow,
            "injection must happen on the unconnected fiber-side pin");
    }

    [Fact]
    public void TwoPinCoupler_InGroup_WaveguidePinConnected_InjectsOnlyOnFiberPin()
    {
        var canvas = new DesignCanvasViewModel();
        var coupler = CreateGratingCoupler("Grating Coupler In", 0, 0, pinCount: 2);
        var mmi = CreateMmi("MMI1", 100, 0);

        var group = new ComponentGroup("TestGroup") { PhysicalX = 0, PhysicalY = 0 };
        group.AddChild(coupler);
        group.AddChild(mmi);
        group.AddInternalPath(new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = CreateStraightPath(40, 5, 100, 5),
            StartPin = coupler.PhysicalPins[0],
            EndPin = mmi.PhysicalPins[0]
        });
        group.UpdateGroupBounds();
        canvas.Components.Add(new ComponentViewModel(group));

        var portManager = new PhysicalExternalPortManager();
        var sources = new SimulationService().ConfigureLightSources(canvas, portManager);

        sources.Count.ShouldBe(1, "a grouped two-pin coupler must inject exactly once");
        var usedInputs = portManager.GetUsedExternalInputs().ToList();
        usedInputs.Count.ShouldBe(1);
        usedInputs[0].AttachedComponentPinId.ShouldBe(coupler.PhysicalPins[1].LogicalPin!.IDInFlow);
    }

    [Fact]
    public void SinglePinCoupler_PinConnected_InjectionUnchanged()
    {
        var canvas = new DesignCanvasViewModel();
        var coupler = CreateGratingCoupler("Grating Coupler In", 0, 0, pinCount: 1);
        var mmi = CreateMmi("MMI1", 100, 0);
        canvas.AddComponent(coupler);
        canvas.AddComponent(mmi);
        canvas.ConnectPins(coupler.PhysicalPins[0], mmi.PhysicalPins[0]);

        var portManager = new PhysicalExternalPortManager();
        var sources = new SimulationService().ConfigureLightSources(canvas, portManager);

        sources.Count.ShouldBe(1);
        var usedInputs = portManager.GetUsedExternalInputs().ToList();
        usedInputs.Count.ShouldBe(1);
        usedInputs[0].AttachedComponentPinId.ShouldBe(
            coupler.PhysicalPins[0].LogicalPin!.IDInFlow,
            "a single-pin coupler keeps injecting on its only pin, connected or not");
    }

    [Fact]
    public void SelectInjectionPins_AllPinsWired_FallsBackToAllPins()
    {
        var coupler = CreateGratingCoupler("Grating Coupler In", 0, 0, pinCount: 2);
        var wired = new HashSet<PhysicalPin>(coupler.PhysicalPins);

        var selected = SimulationService.SelectInjectionPins(coupler.PhysicalPins, wired);

        selected.Count.ShouldBe(2, "a fully wired multi-pin coupler must not lose its source");
    }

    /// <summary>
    /// Creates a Grating Coupler test component with <paramref name="pinCount"/>
    /// optical pins. With two pins, index 0 is the waveguide side and index 1
    /// the fiber side.
    /// </summary>
    private static Component CreateGratingCoupler(string identifier, double x, double y, int pinCount)
    {
        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin>())
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 40,
            HeightMicrometers = 10
        };

        for (int i = 0; i < pinCount; i++)
        {
            string name = i == 0 ? "port 2" : "port 1";
            component.PhysicalPins.Add(new PhysicalPin
            {
                Name = name,
                ParentComponent = component,
                OffsetXMicrometers = i == 0 ? 40 : 0,
                OffsetYMicrometers = 5,
                AngleDegrees = i == 0 ? 0 : 180,
                LogicalPin = new Pin(name, i, MatterType.Light,
                    i == 0 ? RectSide.Right : RectSide.Left)
            });
        }

        return component;
    }

    private static Component CreateMmi(string identifier, double x, double y)
    {
        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin>())
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 10,
            HeightMicrometers = 10
        };
        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in",
            ParentComponent = component,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 5,
            AngleDegrees = 180,
            LogicalPin = new Pin("in", 0, MatterType.Light, RectSide.Left)
        });
        return component;
    }

    private static RoutedPath CreateStraightPath(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        return path;
    }
}
