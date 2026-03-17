using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for issue #215: Bug - Phantom Connections When Deleting Components.
/// Verifies that connections are properly cleaned up when components are deleted.
/// </summary>
public class DeleteComponentConnectionCleanupTests
{
    [Fact]
    public void DeleteComponent_WithConnection_RemovesConnectionViewModel()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = CreateTestComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateTestComponentWithPins("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1, "Template1");
        var vm2 = canvas.AddComponent(comp2, "Template2");

        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];

        // Create connection between components
        var connection = canvas.ConnectPins(pin1, pin2);
        connection.ShouldNotBeNull();
        canvas.Connections.Count.ShouldBe(1);

        // Act - Delete component 1
        var deleteCmd = new DeleteComponentCommand(canvas, vm1);
        deleteCmd.Execute();

        // Assert - Connection should be removed
        canvas.Components.Count.ShouldBe(1);
        canvas.Connections.Count.ShouldBe(0, "Connection ViewModel should be removed when component is deleted");
        canvas.ConnectionManager.Connections.Count.ShouldBe(0, "Core connection should be removed");
    }

    [Fact]
    public void DeleteComponent_WithConnection_UndoRestoresConnection()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = CreateTestComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateTestComponentWithPins("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1, "Template1");
        var vm2 = canvas.AddComponent(comp2, "Template2");

        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];

        var connection = canvas.ConnectPins(pin1, pin2);
        canvas.Connections.Count.ShouldBe(1);

        // Act - Delete component, then undo
        var deleteCmd = new DeleteComponentCommand(canvas, vm1);
        deleteCmd.Execute();
        canvas.Connections.Count.ShouldBe(0);

        deleteCmd.Undo();

        // Assert - Connection should be restored
        canvas.Components.Count.ShouldBe(2);
        canvas.Connections.Count.ShouldBe(1, "Connection should be restored on undo");
        canvas.ConnectionManager.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public void DeleteComponent_WithMultipleConnections_RemovesAllConnectionViewModels()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = CreateTestComponentWithMultiplePins("Comp1", 100, 100);
        var comp2 = CreateTestComponentWithPins("Comp2", 200, 100);
        var comp3 = CreateTestComponentWithPins("Comp3", 300, 100);

        var vm1 = canvas.AddComponent(comp1, "Template1");
        var vm2 = canvas.AddComponent(comp2, "Template2");
        var vm3 = canvas.AddComponent(comp3, "Template3");

        // Create two connections from comp1
        var conn1 = canvas.ConnectPins(comp1.PhysicalPins[0], comp2.PhysicalPins[0]);
        var conn2 = canvas.ConnectPins(comp1.PhysicalPins[1], comp3.PhysicalPins[0]);

        canvas.Connections.Count.ShouldBe(2);

        // Act - Delete comp1 which has both connections
        var deleteCmd = new DeleteComponentCommand(canvas, vm1);
        deleteCmd.Execute();

        // Assert - Both connection ViewModels should be removed
        canvas.Components.Count.ShouldBe(2);
        canvas.Connections.Count.ShouldBe(0, "All connection ViewModels should be removed");
        canvas.ConnectionManager.Connections.Count.ShouldBe(0, "All core connections should be removed");
    }

    [Fact]
    public void DeleteMultipleComponents_RemovesAllRelatedConnections()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = CreateTestComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateTestComponentWithPins("Comp2", 200, 100);
        var comp3 = CreateTestComponentWithPins("Comp3", 300, 100);

        var vm1 = canvas.AddComponent(comp1, "Template1");
        var vm2 = canvas.AddComponent(comp2, "Template2");
        var vm3 = canvas.AddComponent(comp3, "Template3");

        // Create chain: comp1 -> comp2 -> comp3
        canvas.ConnectPins(comp1.PhysicalPins[0], comp2.PhysicalPins[0]);
        canvas.ConnectPins(comp2.PhysicalPins[1], comp3.PhysicalPins[0]);

        canvas.Connections.Count.ShouldBe(2);

        // Act - Delete comp1 and comp3 (leaving comp2 with no connections)
        var deleteCmd = new GroupDeleteCommand(canvas, new[] { vm1, vm3 });
        deleteCmd.Execute();

        // Assert - All connections should be removed
        canvas.Components.Count.ShouldBe(1);
        canvas.Connections.Count.ShouldBe(0, "All connection ViewModels should be removed");
        canvas.ConnectionManager.Connections.Count.ShouldBe(0);
    }

    [Fact]
    public void DeleteComponent_FromInstantiatedTemplate_RemovesConnections()
    {
        // This test verifies that after #214 (template-only architecture),
        // components instantiated from a template can be deleted without phantom connections.

        // Arrange
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        // Create regular components and connect them (simulating template instantiation)
        var comp1 = CreateTestComponentWithPins("Comp1", 500, 500);
        var comp2 = CreateTestComponentWithPins("Comp2", 600, 500);

        var vm1 = canvas.AddComponent(comp1, "Template1");
        var vm2 = canvas.AddComponent(comp2, "Template2");

        // Create connection (simulating template internal path)
        var connection = canvas.ConnectPins(comp1.PhysicalPins[1], comp2.PhysicalPins[0]);
        canvas.Connections.Count.ShouldBe(1);

        // Act - Delete one of the components
        var deleteCmd = new DeleteComponentCommand(canvas, vm1);
        deleteCmd.Execute();

        // Assert - Connection should be removed (no phantom connection)
        canvas.Components.Count.ShouldBe(1);
        canvas.Connections.Count.ShouldBe(0, "Connection should be removed when component from template is deleted");
        canvas.ConnectionManager.Connections.Count.ShouldBe(0);
    }

    private static Component CreateTestComponentWithPins(string identifier, double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var pins = new List<PhysicalPin>
        {
            new PhysicalPin { Name = "in", OffsetXMicrometers = 0, OffsetYMicrometers = 25, AngleDegrees = 180 },
            new PhysicalPin { Name = "out", OffsetXMicrometers = 50, OffsetYMicrometers = 25, AngleDegrees = 0 }
        };

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test_function",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: identifier,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: pins
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private static Component CreateTestComponentWithMultiplePins(string identifier, double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var pins = new List<PhysicalPin>
        {
            new PhysicalPin { Name = "out1", OffsetXMicrometers = 50, OffsetYMicrometers = 15, AngleDegrees = 0 },
            new PhysicalPin { Name = "out2", OffsetXMicrometers = 50, OffsetYMicrometers = 35, AngleDegrees = 0 }
        };

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test_function",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: identifier,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: pins
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private static ComponentGroup CreateTestTemplate(string templateName)
    {
        var group = new ComponentGroup(templateName)
        {
            PhysicalX = 0,
            PhysicalY = 0,
            IsPrefab = true
        };

        // Create two child components
        var comp1 = CreateTestComponentWithPins("Child1", 0, 0);
        var comp2 = CreateTestComponentWithPins("Child2", 100, 0);

        group.AddChild(comp1);
        group.AddChild(comp2);

        // Add internal path (frozen connection)
        var frozenPath = new FrozenWaveguidePath
        {
            StartPin = comp1.PhysicalPins[1],
            EndPin = comp2.PhysicalPins[0],
            Path = new CAP_Core.Routing.RoutedPath()
        };

        group.AddInternalPath(frozenPath);

        return group;
    }
}
