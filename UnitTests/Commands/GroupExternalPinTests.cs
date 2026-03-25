using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for ComponentGroup external pin creation logic.
/// Verifies that ALL unoccupied pins are exposed as external pins,
/// not just pins with existing external connections.
/// </summary>
public class GroupExternalPinTests
{
    [Fact]
    public void CreateGroup_WithUnoccupiedPins_ShouldExposeExternalPins()
    {
        // Arrange - Create 2 components with 2 pins each, connect 1 internal, 2 unoccupied
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100, pinCount: 2);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100, pinCount: 2);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Connect comp1.Pin1 to comp2.Pin1 (internal connection)
        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];
        canvas.ConnectPins(pin1, pin2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Should have 2 external pins (comp1.Pin2 and comp2.Pin2 are unoccupied)
        var group = (ComponentGroup)canvas.Components.First().Component;
        group.ExternalPins.Count.ShouldBe(2);

        // Verify the external pins are the correct unoccupied pins
        var externalPinNames = group.ExternalPins.Select(p => p.InternalPin.Name).ToList();
        externalPinNames.ShouldContain("Pin2");

        // Verify pins are visible in UI (AllPins collection)
        var groupVm = canvas.Components.First();
        var groupPinsInAllPins = canvas.AllPins.Where(p => p.ParentComponentViewModel == groupVm).ToList();
        groupPinsInAllPins.Count.ShouldBe(2);
    }

    [Fact]
    public void CreateGroup_WithInternalConnection_ShouldOnlyExposeUnoccupiedPins()
    {
        // Arrange - 2 components with 1 pin each, connected internally
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100, pinCount: 1);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100, pinCount: 1);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Connect them internally
        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];
        canvas.ConnectPins(pin1, pin2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Should have 0 external pins (both pins are occupied by internal connection)
        var group = (ComponentGroup)canvas.Components.First().Component;
        group.ExternalPins.Count.ShouldBe(0);

        // No pins should be visible in UI
        var groupVm = canvas.Components.First();
        var groupPinsInAllPins = canvas.AllPins.Where(p => p.ParentComponentViewModel == groupVm).ToList();
        groupPinsInAllPins.Count.ShouldBe(0);
    }

    [Fact]
    public void CreateGroup_WithNoConnections_AllPinsShouldBeExternal()
    {
        // Arrange - Create 2 components with 2 pins each (4 total pins), no connections
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100, pinCount: 2);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100, pinCount: 2);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group (no connections)
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Should have 4 external pins (all pins are unoccupied)
        var group = (ComponentGroup)canvas.Components.First().Component;
        group.ExternalPins.Count.ShouldBe(4);

        // Verify all 4 internal pins are represented
        var internalPins = new HashSet<PhysicalPin>();
        foreach (var groupPin in group.ExternalPins)
        {
            internalPins.Add(groupPin.InternalPin);
        }

        internalPins.Count.ShouldBe(4);
        internalPins.ShouldContain(comp1.PhysicalPins[0]);
        internalPins.ShouldContain(comp1.PhysicalPins[1]);
        internalPins.ShouldContain(comp2.PhysicalPins[0]);
        internalPins.ShouldContain(comp2.PhysicalPins[1]);

        // Verify all pins are visible in UI
        var groupVm = canvas.Components.First();
        var groupPinsInAllPins = canvas.AllPins.Where(p => p.ParentComponentViewModel == groupVm).ToList();
        groupPinsInAllPins.Count.ShouldBe(4);
    }

    /// <summary>
    /// Helper to create a component with specified number of physical pins.
    /// </summary>
    private Component CreateComponentWithPins(string identifier, double x, double y, int pinCount)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var pins = new List<PhysicalPin>();

        for (int i = 0; i < pinCount; i++)
        {
            pins.Add(new PhysicalPin
            {
                Name = $"Pin{i + 1}",
                OffsetXMicrometers = i * 25.0,
                OffsetYMicrometers = 15,
                AngleDegrees = 0
            });
        }

        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            pins
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };

        return component;
    }
}
