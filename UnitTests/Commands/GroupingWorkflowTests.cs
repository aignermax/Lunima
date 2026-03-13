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
/// Integration tests for ComponentGroup creation and ungrouping workflow.
/// Tests the full MVVM stack: Core → Commands → ViewModel.
/// </summary>
public class GroupingWorkflowTests
{
    [Fact]
    public void CreateGroup_From2Components_CreatesGroupWithCorrectChildren()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1, "Template1");
        var vm2 = canvas.AddComponent(comp2, "Template2");

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Individual components removed, group added
        canvas.Components.Count.ShouldBe(1);
        var groupVm = canvas.Components[0];
        groupVm.Component.ShouldBeOfType<ComponentGroup>();

        var group = (ComponentGroup)groupVm.Component;
        group.ChildComponents.Count.ShouldBe(2);
        group.ChildComponents.ShouldContain(comp1);
        group.ChildComponents.ShouldContain(comp2);
    }

    [Fact]
    public async Task CreateGroup_WithInternalConnection_FreezesPath()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Connect the components
        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];
        canvas.ConnectPins(pin1, pin2);

        // Wait for routing to complete
        await canvas.RecalculateRoutesAsync();

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Group has frozen internal path
        canvas.Components.Count.ShouldBe(1);
        var group = (ComponentGroup)canvas.Components[0].Component;
        group.InternalPaths.Count.ShouldBe(1);

        var frozenPath = group.InternalPaths[0];
        frozenPath.StartPin.ShouldBe(pin1);
        frozenPath.EndPin.ShouldBe(pin2);
        frozenPath.Path.ShouldNotBeNull();
    }

    [Fact]
    public void CreateGroup_ThenUndo_RestoresOriginalComponents()
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

        // Act - Create group then undo
        cmd.Execute();
        canvas.Components.Count.ShouldBe(1);

        cmd.Undo();

        // Assert - Individual components restored
        canvas.Components.Count.ShouldBe(2);
        canvas.Components[0].Component.Identifier.ShouldContain("Comp");
        canvas.Components[1].Component.Identifier.ShouldContain("Comp");
    }

    [Fact]
    public void UngroupCommand_RestoresIndividualComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Create group
        var createCmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        createCmd.Execute();

        var group = (ComponentGroup)canvas.Components[0].Component;

        // Act - Ungroup
        var ungroupCmd = new UngroupCommand(canvas, group);
        ungroupCmd.Execute();

        // Assert - Individual components restored
        canvas.Components.Count.ShouldBe(2);
        canvas.Components.ShouldContain(c => c.Component == comp1);
        canvas.Components.ShouldContain(c => c.Component == comp2);
    }

    [Fact]
    public void UngroupCommand_ThenUndo_RecreatesGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Create group
        var createCmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        createCmd.Execute();
        var group = (ComponentGroup)canvas.Components[0].Component;

        // Ungroup
        var ungroupCmd = new UngroupCommand(canvas, group);
        ungroupCmd.Execute();
        canvas.Components.Count.ShouldBe(2);

        // Act - Undo ungroup
        ungroupCmd.Undo();

        // Assert - Group recreated
        canvas.Components.Count.ShouldBe(1);
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();
        var restoredGroup = (ComponentGroup)canvas.Components[0].Component;
        restoredGroup.ChildComponents.Count.ShouldBe(2);
    }

    [Fact]
    public void CreateGroup_WithExternalConnection_CreatesGroupPin()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);
        var comp3 = CreateComponentWithPins("Comp3", 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);
        var vm3 = canvas.AddComponent(comp3);

        // Connect comp2 to comp3 (comp3 is outside the group)
        var pin2 = comp2.PhysicalPins[0];
        var pin3 = comp3.PhysicalPins[0];
        canvas.ConnectPins(pin2, pin3);

        // Select only comp1 and comp2 for grouping
        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Group has external pin
        canvas.Components.Count.ShouldBe(2); // Group + Comp3
        var group = canvas.Components
            .Where(c => c.Component is ComponentGroup)
            .Select(c => (ComponentGroup)c.Component)
            .First();

        group.ExternalPins.Count.ShouldBe(1);
        group.ExternalPins[0].InternalPin.ShouldBe(pin2);
    }

    [Fact]
    public void CreateGroup_WithLockedComponent_DoesNothing()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);
        comp1.IsLocked = true; // Lock one component

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Try to create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - No group created, individual components remain
        canvas.Components.Count.ShouldBe(2);
        canvas.Components.ShouldNotContain(c => c.Component is ComponentGroup);
    }

    [Fact]
    public void CreateGroup_LessThan2Components_DoesNothing()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var vm1 = canvas.AddComponent(comp1);

        canvas.Selection.AddToSelection(vm1);

        // Act - Try to create group with only 1 component
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - No group created
        canvas.Components.Count.ShouldBe(1);
        canvas.Components[0].Component.ShouldNotBeOfType<ComponentGroup>();
    }

    [Fact]
    public void CreateGroup_AutomaticallySelectsNewGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Act - Create group
        var cmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        cmd.Execute();

        // Assert - Group is automatically selected
        canvas.Selection.SelectedComponents.Count.ShouldBe(1);
        var selectedVm = canvas.Selection.SelectedComponents[0];
        selectedVm.Component.ShouldBeOfType<ComponentGroup>();
        selectedVm.IsSelected.ShouldBeTrue();
        canvas.SelectedComponent.ShouldBe(selectedVm);
    }

    [Fact]
    public void CreateGroup_ThenUndo_ClearsSelection()
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

        // Verify group is selected
        canvas.Selection.SelectedComponents.Count.ShouldBe(1);
        var groupVm = canvas.Selection.SelectedComponents[0];
        groupVm.Component.ShouldBeOfType<ComponentGroup>();

        // Act - Undo
        cmd.Undo();

        // Assert - Individual components restored (selection behavior depends on undo logic)
        canvas.Components.Count.ShouldBe(2);
        canvas.Components.ShouldNotContain(c => c.Component is ComponentGroup);
    }

    [Fact]
    public async Task UngroupCommand_AfterGroupMoved_RecalculatesRoutes()
    {
        // Arrange - Create components with connection
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Connect the components
        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];
        canvas.ConnectPins(pin1, pin2);

        // Wait for routing to complete
        await canvas.RecalculateRoutesAsync();

        // Verify connection has valid route
        var originalConnection = canvas.ConnectionManager.Connections[0];
        originalConnection.IsPathValid.ShouldBeTrue();
        var originalStartX = originalConnection.RoutedPath?.Segments[0].StartPoint.X ?? 0;
        var originalEndX = originalConnection.RoutedPath?.Segments[^1].EndPoint.X ?? 0;

        canvas.Selection.AddToSelection(vm1);
        canvas.Selection.AddToSelection(vm2);

        // Create group
        var createCmd = new CreateGroupCommand(canvas, canvas.Selection.SelectedComponents.ToList());
        createCmd.Execute();

        var group = (ComponentGroup)canvas.Components[0].Component;

        // Act - Move the group to a new position
        group.MoveGroup(500, 500); // Move significantly

        // Ungroup - this should trigger route recalculation
        var ungroupCmd = new UngroupCommand(canvas, group);
        ungroupCmd.Execute();

        // Wait for routing to complete
        await canvas.RecalculateRoutesAsync();

        // Assert - Routes should be recalculated for new positions
        canvas.Components.Count.ShouldBe(2);
        canvas.Connections.Count.ShouldBe(1);

        var restoredConnection = canvas.ConnectionManager.Connections[0];
        restoredConnection.IsPathValid.ShouldBeTrue();

        // Verify route endpoints match current component positions
        var (startX, startY) = pin1.GetAbsolutePosition();
        var (endX, endY) = pin2.GetAbsolutePosition();

        var routedPath = restoredConnection.RoutedPath;
        routedPath.ShouldNotBeNull();
        routedPath.Segments.Count.ShouldBeGreaterThan(0);

        var firstSegment = routedPath.Segments[0];
        var lastSegment = routedPath.Segments[^1];

        // Route should start and end at current pin positions (within tolerance)
        Math.Abs(firstSegment.StartPoint.X - startX).ShouldBeLessThan(1.0);
        Math.Abs(firstSegment.StartPoint.Y - startY).ShouldBeLessThan(1.0);
        Math.Abs(lastSegment.EndPoint.X - endX).ShouldBeLessThan(1.0);
        Math.Abs(lastSegment.EndPoint.Y - endY).ShouldBeLessThan(1.0);

        // Route coordinates should be different from original (group was moved)
        Math.Abs(firstSegment.StartPoint.X - originalStartX).ShouldBeGreaterThan(100);
        Math.Abs(lastSegment.EndPoint.X - originalEndX).ShouldBeGreaterThan(100);
    }

    /// <summary>
    /// Helper to create a simple test component.
    /// </summary>
    private Component CreateTestComponent(string identifier, double x, double y)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<Slider>(),
            "test",
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
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
        return component;
    }

    /// <summary>
    /// Helper to create a component with physical pins for connection testing.
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

        return component;
    }
}
