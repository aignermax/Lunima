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
/// Tests for light source detection when Grating Couplers or Edge Couplers are inside ComponentGroups.
/// Bug: Simulation doesn't detect light sources if they are grouped.
/// </summary>
public class LightSourceInGroupTests
{
    /// <summary>
    /// Reproduces bug: GratingCoupler inside a group is not recognized as light source.
    ///
    /// Scenario:
    /// 1. Create Grating Coupler with 1550nm wavelength
    /// 2. Connect it to another component
    /// 3. Group them together
    /// 4. Start simulation → Should detect light source, but currently doesn't
    /// </summary>
    [Fact]
    public void Simulation_GratingCouplerInGroup_ShouldBeRecognizedAsLightSource()
    {
        // Arrange: Create a canvas
        var canvas = new DesignCanvasViewModel();

        // Create a Grating Coupler (light source)
        var gratingCoupler = CreateGratingCoupler("Grating Coupler", 0, 0);

        // Create a connected component (e.g., MMI)
        var mmi = CreateMMI("MMI1", 50, 0);

        // Create a group containing both components
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 0,
            PhysicalY = 0
        };
        group.AddChild(gratingCoupler);
        group.AddChild(mmi);

        // Add frozen path to the group (simulates connection)
        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = CreateSimplePath(10, 0, 50, 0),
            StartPin = gratingCoupler.PhysicalPins[0],
            EndPin = mmi.PhysicalPins[0]
        };
        group.AddInternalPath(frozenPath);
        group.UpdateGroupBounds();

        // Add the group to canvas (this is the key: GC is inside group, not directly on canvas)
        var groupVm = new ComponentViewModel(group);
        canvas.Components.Add(groupVm);

        // Act: Configure light sources (this is what SimulationService does)
        var simulationService = new SimulationService();
        var portManager = new PhysicalExternalPortManager();
        var lightSources = simulationService.ConfigureLightSources(canvas, portManager);

        // Assert: Should detect the Grating Coupler as a light source even though it's in a group
        lightSources.Count.ShouldBeGreaterThan(0, "Should detect at least one light source");
        lightSources.ShouldContain(src => src.ComponentId.Contains("Grating"),
            "Should detect GratingCoupler as light source even when it's inside a group");
    }

    /// <summary>
    /// Creates a Grating Coupler test component.
    /// </summary>
    private Component CreateGratingCoupler(string identifier, double x, double y)
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
            new List<PhysicalPin>()
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 10,
            HeightMicrometers = 10
        };

        // Create a Pin (LogicalPin) for light output
        var logicalPin = new Pin("out", 1, MatterType.Light, RectSide.Right);

        // Add a physical pin connected to the logical pin
        var outputPin = new PhysicalPin
        {
            Name = "out",
            ParentComponent = component,
            OffsetXMicrometers = 10,
            OffsetYMicrometers = 5,
            AngleDegrees = 0,
            LogicalPin = logicalPin
        };
        component.PhysicalPins.Add(outputPin);

        return component;
    }

    /// <summary>
    /// Creates a generic MMI component for testing.
    /// </summary>
    private Component CreateMMI(string identifier, double x, double y)
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
            new List<PhysicalPin>()
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 10,
            HeightMicrometers = 10
        };

        var inputPin = new PhysicalPin
        {
            Name = "in",
            ParentComponent = component,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 5,
            AngleDegrees = 180
        };
        component.PhysicalPins.Add(inputPin);

        return component;
    }

    /// <summary>
    /// Creates a simple straight path.
    /// </summary>
    private RoutedPath CreateSimplePath(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        return path;
    }
}
