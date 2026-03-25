using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for ComponentViewModel bounds synchronization after group edit mode.
/// Verifies that Width/Height properties update when UpdateGroupBounds() is called.
/// </summary>
public class GroupBoundsUpdateTests
{
    [Fact]
    public void ExitGroupEditMode_UpdatesComponentViewModelBounds()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        // Create two components
        var comp1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;
        comp1.WidthMicrometers = 50;
        comp1.HeightMicrometers = 10;

        var comp2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp2.PhysicalX = 200;
        comp2.PhysicalY = 200;
        comp2.WidthMicrometers = 50;
        comp2.HeightMicrometers = 10;

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Create a group from these components
        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.SelectedComponents.Add(vm2);

        var createGroupCmd = new CAP.Avalonia.Commands.CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 });
        createGroupCmd.Execute();

        var groupVm = canvas.Components.FirstOrDefault(c => c.IsComponentGroup);
        groupVm.ShouldNotBeNull();

        var group = (ComponentGroup)groupVm.Component;
        double originalWidth = groupVm.Width;
        double originalHeight = groupVm.Height;

        // Act: Enter group edit mode, move child components, then exit
        canvas.EnterGroupEditMode(group);

        // Move child components to expand the group bounds
        comp1.PhysicalX = 50;  // Move left
        comp1.PhysicalY = 50;  // Move up
        comp2.PhysicalX = 300; // Move right
        comp2.PhysicalY = 300; // Move down

        canvas.ExitGroupEditMode();

        // Assert: ComponentViewModel should reflect updated bounds
        groupVm.Width.ShouldBeGreaterThan(originalWidth,
            "Width should increase after moving children to expand bounds");
        groupVm.Height.ShouldBeGreaterThan(originalHeight,
            "Height should increase after moving children to expand bounds");

        // Verify the ViewModel bounds match the Core model bounds
        groupVm.Width.ShouldBe(group.WidthMicrometers);
        groupVm.Height.ShouldBe(group.HeightMicrometers);
    }

    [Fact]
    public void ExitToRoot_UpdatesComponentViewModelBounds()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        // Create two components
        var comp1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;
        comp1.WidthMicrometers = 50;
        comp1.HeightMicrometers = 10;

        var comp2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp2.PhysicalX = 200;
        comp2.PhysicalY = 200;
        comp2.WidthMicrometers = 50;
        comp2.HeightMicrometers = 10;

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Create a group from these components
        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.SelectedComponents.Add(vm2);

        var createGroupCmd = new CAP.Avalonia.Commands.CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 });
        createGroupCmd.Execute();

        var groupVm = canvas.Components.FirstOrDefault(c => c.IsComponentGroup);
        groupVm.ShouldNotBeNull();

        var group = (ComponentGroup)groupVm.Component;
        double originalWidth = groupVm.Width;
        double originalHeight = groupVm.Height;

        // Act: Enter group edit mode, move child components, then exit to root
        canvas.EnterGroupEditMode(group);

        // Move child components to expand the group bounds
        comp1.PhysicalX = 50;  // Move left
        comp1.PhysicalY = 50;  // Move up
        comp2.PhysicalX = 300; // Move right
        comp2.PhysicalY = 300; // Move down

        canvas.ExitToRootCommand.Execute(null);

        // Assert: ComponentViewModel should reflect updated bounds
        groupVm.Width.ShouldBeGreaterThan(originalWidth,
            "Width should increase after moving children to expand bounds");
        groupVm.Height.ShouldBeGreaterThan(originalHeight,
            "Height should increase after moving children to expand bounds");

        // Verify the ViewModel bounds match the Core model bounds
        groupVm.Width.ShouldBe(group.WidthMicrometers);
        groupVm.Height.ShouldBe(group.HeightMicrometers);
    }

    [Fact]
    public void NotifyDimensionsChanged_UpdatesWidthAndHeight()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 0,
            PhysicalY = 0
        };

        // Add a child component
        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        child.PhysicalX = 10;
        child.PhysicalY = 10;
        child.WidthMicrometers = 100;
        child.HeightMicrometers = 20;
        group.AddChild(child);
        group.UpdateGroupBounds();

        var groupVm = new ComponentViewModel(group);

        // Verify initial bounds
        double initialWidth = groupVm.Width;
        double initialHeight = groupVm.Height;
        initialWidth.ShouldBe(100); // child width=100
        initialHeight.ShouldBe(20);  // child height=20

        // Act: Manually change the core model dimensions
        group.WidthMicrometers = 200;
        group.HeightMicrometers = 50;

        // At this point, ComponentViewModel still returns old values
        // because it caches the values from the Component

        // Call NotifyDimensionsChanged to force property change notification
        groupVm.NotifyDimensionsChanged();

        // Assert: ComponentViewModel should now reflect new dimensions
        groupVm.Width.ShouldBe(200, "Width should update after NotifyDimensionsChanged");
        groupVm.Height.ShouldBe(50, "Height should update after NotifyDimensionsChanged");
    }
}
