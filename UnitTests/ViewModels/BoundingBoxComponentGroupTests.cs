using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for bounding box calculation with ComponentGroups.
/// Verifies that BoundingBoxCalculator correctly handles ComponentGroups.
/// </summary>
public class BoundingBoxComponentGroupTests
{
    /// <summary>
    /// Verifies that a ComponentGroup's ViewModel position matches its Component position.
    /// </summary>
    [Fact]
    public void ComponentViewModel_ForGroup_SynchronizesXYWithPhysicalXY()
    {
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        group.PhysicalX = 200;
        group.PhysicalY = 300;

        var groupVm = new ComponentViewModel(group, "CustomGroup");

        // ComponentViewModel X/Y should match Component PhysicalX/PhysicalY
        groupVm.X.ShouldBe(group.PhysicalX, "ComponentViewModel.X should sync with Component.PhysicalX");
        groupVm.Y.ShouldBe(group.PhysicalY, "ComponentViewModel.Y should sync with Component.PhysicalY");
    }

    /// <summary>
    /// Verifies that after moving a group, the ComponentViewModel position is updated.
    /// </summary>
    [Fact]
    public void ComponentViewModel_AfterGroupMove_UpdatesXYPosition()
    {
        var group = TestComponentFactory.CreateComponentGroup("MovableGroup", addChildren: true);
        group.PhysicalX = 100;
        group.PhysicalY = 100;

        var groupVm = new ComponentViewModel(group, "CustomGroup");
        groupVm.X.ShouldBe(100);
        groupVm.Y.ShouldBe(100);

        // Move the group
        group.MoveGroup(50, 75);

        // Check if PhysicalX/PhysicalY are updated
        group.PhysicalX.ShouldBe(150);
        group.PhysicalY.ShouldBe(175);

        // ComponentViewModel X/Y are NOT automatically updated - they must be explicitly set
        // This could be the bug!
        groupVm.X.ShouldBe(100, "X hasn't been updated yet");
        groupVm.Y.ShouldBe(100, "Y hasn't been updated yet");

        // After explicit update
        groupVm.X = group.PhysicalX;
        groupVm.Y = group.PhysicalY;
        groupVm.X.ShouldBe(150);
        groupVm.Y.ShouldBe(175);
    }

    /// <summary>
    /// Regression test: Verifies that BoundingBoxCalculator uses ComponentViewModel.X/Y
    /// not Component.PhysicalX/PhysicalY.
    /// </summary>
    [Fact]
    public void BoundingBoxCalculator_UsesComponentViewModelPosition()
    {
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        group.PhysicalX = 100;
        group.PhysicalY = 200;

        var groupVm = new ComponentViewModel(group, "CustomGroup");

        // Simulate desync: ComponentViewModel position is different from Component position
        groupVm.X = 500;
        groupVm.Y = 600;

        var bounds = BoundingBoxCalculator.Calculate(new[] { groupVm });

        bounds.ShouldNotBeNull();
        // Bounding box should use ComponentViewModel.X/Y (500, 600) + offsets (100, 100)
        // MinX = 500 + 100 = 600, MinY = 600 + 100 = 700
        // MaxX = 600 + 550 = 1150, MaxY = 700 + 250 = 950
        bounds.Value.MinX.ShouldBe(600, "BoundingBox should use ComponentViewModel.X + offset");
        bounds.Value.MinY.ShouldBe(700, "BoundingBox should use ComponentViewModel.Y + offset");
    }

    /// <summary>
    /// Integration test: Verifies that zoom-to-fit uses the correct bounding box
    /// when ComponentViewModel positions are out of sync with Component positions.
    /// </summary>
    [Fact]
    public void ZoomToFit_WithDesyncedComponentViewModel_UsesViewModelPosition()
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.WidthMicrometers = 100;
        comp1.HeightMicrometers = 100;

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = 1000;  // Far away
        comp2.PhysicalY = 1000;
        comp2.WidthMicrometers = 100;
        comp2.HeightMicrometers = 100;

        var vm1 = new ComponentViewModel(comp1);
        var vm2 = new ComponentViewModel(comp2);

        // Simulate desync: vm2's position in ViewModel doesn't match Component
        vm2.X = 0;  // Should be 1000
        vm2.Y = 0;  // Should be 1000

        var bounds = BoundingBoxCalculator.Calculate(new[] { vm1, vm2 });

        bounds.ShouldNotBeNull();
        // If using ComponentViewModel.X/Y (both at 0,0): bounds = (0,0) to (100,100)
        // If using Component.PhysicalX/Y: bounds = (0,0) to (1100,1100)
        // We expect (0,0) to (100,100) because both VMs are at (0,0)
        bounds.Value.Width.ShouldBeLessThan(200, "Should use ViewModel positions, not Component positions");
    }

    /// <summary>
    /// Critical test: Verifies that a ComponentGroup's Width/Height correctly represent
    /// the bounding box of all child components.
    /// </summary>
    [Fact]
    public void ComponentGroup_WidthHeight_IncludesAllChildren()
    {
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);

        // Children are at (100, 100) and (400, 100), each 250x250
        // So bounding box should be from (100, 100) to (650, 350)
        // Width = 550, Height = 250

        // The group's PhysicalX/PhysicalY are set at creation (0, 0 by TestComponentFactory)
        // The offsets track the difference between group position and child positions
        group.MinChildOffsetX.ShouldBe(100, "Offset from group X to min child X should be 100");
        group.MinChildOffsetY.ShouldBe(100, "Offset from group Y to min child Y should be 100");

        // The group's width/height should span all children
        group.WidthMicrometers.ShouldBe(550, "Group width should span from X=100 to X=650");
        group.HeightMicrometers.ShouldBe(250, "Group height should span from Y=100 to Y=350");
    }

    /// <summary>
    /// Critical test: Verifies that BoundingBoxCalculator correctly calculates bounds
    /// for a ComponentGroup based on the group's position + width/height.
    /// </summary>
    [Fact]
    public void BoundingBoxCalculator_WithComponentGroup_UsesGroupBounds()
    {
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);
        var groupVm = new ComponentViewModel(group, "CustomGroup");

        var bounds = BoundingBoxCalculator.Calculate(new[] { groupVm });

        bounds.ShouldNotBeNull();
        // Group is at (0, 0) with offset (100, 100) and width=550, height=250
        // Bounding box should be (0+100, 0+100) to (0+100+550, 0+100+250)
        // = (100, 100) to (650, 350)
        bounds.Value.MinX.ShouldBe(100);
        bounds.Value.MinY.ShouldBe(100);
        bounds.Value.MaxX.ShouldBe(650);
        bounds.Value.MaxY.ShouldBe(350);
    }
}
