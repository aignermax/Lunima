using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Rendering;

/// <summary>
/// Integration tests for ComponentGroup rendering workflow.
/// Tests the full stack: Core → ViewModel → Rendering.
/// </summary>
public class GroupRenderingIntegrationTests
{
    [Fact]
    public void CanvasViewModel_WithComponentGroup_IncludesGroupInComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("Test Group");
        var child = CreateTestComponent("Child", 100, 100);
        group.AddChild(child);

        // Act
        var groupVm = canvas.AddComponent(group);

        // Assert
        canvas.Components.Count.ShouldBe(1);
        canvas.Components[0].ShouldBe(groupVm);
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();
    }

    [Fact]
    public void CreateGroupCommand_CreatesRenderableGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Group is now the only component in canvas
        canvas.Components.Count.ShouldBe(1);
        var groupVm = canvas.Components[0];
        groupVm.Component.ShouldBeOfType<ComponentGroup>();

        var group = (ComponentGroup)groupVm.Component;
        group.ChildComponents.Count.ShouldBe(2);
        group.GroupName.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateGroup_WithConnection_HasFrozenPath()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Connect components
        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];
        canvas.ConnectPins(pin1, pin2);

        // Wait for routing
        await canvas.RecalculateRoutesAsync();

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Group has frozen internal path for rendering
        var group = (ComponentGroup)canvas.Components[0].Component;
        group.InternalPaths.Count.ShouldBe(1);
        group.InternalPaths[0].Path.ShouldNotBeNull();
        group.InternalPaths[0].Path.Segments.ShouldNotBeEmpty();
    }

    [Fact]
    public void GroupMove_UpdatesAllChildPositions()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);
        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);
        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        var group = (ComponentGroup)canvas.Components[0].Component;
        double originalChild1X = group.ChildComponents[0].PhysicalX;

        // Act - Move the group
        group.MoveGroup(50, 25);

        // Assert - Children moved with group
        group.ChildComponents[0].PhysicalX.ShouldBe(originalChild1X + 50);
    }

    /// <summary>
    /// Creates a test component with specified name and position.
    /// </summary>
    private Component CreateTestComponent(string name, double x, double y)
    {
        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_type",
            name,
            new Part[1, 1] { { new Part() } },
            -1,
            $"test_{Guid.NewGuid():N}",
            new DiscreteRotation(),
            new List<PhysicalPin>()
        );

        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 30;

        return component;
    }

    /// <summary>
    /// Creates a test component with physical pins for connection testing.
    /// Follows the same pattern as GroupingWorkflowTests.
    /// </summary>
    private Component CreateComponentWithPins(string identifier, double x, double y)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "Pin1",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 15,
                AngleDegrees = 0
            }
        };

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

        // Set parent component for pins
        foreach (var pin in pins)
        {
            pin.ParentComponent = component;
        }

        return component;
    }
}
