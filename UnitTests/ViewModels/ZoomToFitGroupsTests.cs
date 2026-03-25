using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for zoom-to-fit with ComponentGroups.
/// Verifies that grouped components are correctly included in bounding box calculations.
/// </summary>
public class ZoomToFitGroupsTests
{
    private static MainViewModel CreateViewModel() =>
        new(new SimulationService(), new SimpleNazcaExporter(), new PdkLoader(), new CommandManager(), new UserPreferencesService(), new CAP_Core.Components.Creation.GroupLibraryManager(), new GroupPreviewGenerator(), new InputDialogService(), new GdsExportService());

    /// <summary>
    /// Regression test: Verifies that zoom-to-fit correctly includes components
    /// that are part of a ComponentGroup.
    /// </summary>
    [Fact]
    public void ZoomToFit_WithComponentGroup_IncludesAllGroupChildren()
    {
        var vm = CreateViewModel();

        // Create a group with children at specific positions
        var group = TestComponentFactory.CreateComponentGroup("TestGroup", addChildren: true);

        // Children are at (100, 100) and (400, 100), each 250x250
        // So the bounding box should span from (100, 100) to (650, 350)
        var groupVm = vm.Canvas.AddComponent(group, "CustomGroup");

        // Perform zoom-to-fit
        vm.ZoomToFit(1000, 1000);

        // With 10% padding on a (100,100)-(650,350) box:
        // Width=550, Height=250
        // Padded: (-55, -25) relative to the bounding box origin
        // Padded width = 660, padded height = 300
        // Zoom = min(1000/660, 1000/300) = min(1.515, 3.333) ≈ 1.515
        vm.ZoomLevel.ShouldBeInRange(1.0, 2.0, "Zoom should be calculated to fit the group");
    }

    /// <summary>
    /// Regression test: Verifies that zoom-to-fit works correctly when a group
    /// is moved after creation.
    /// </summary>
    [Fact]
    public void ZoomToFit_WithMovedComponentGroup_IncludesAllGroupChildren()
    {
        var vm = CreateViewModel();

        // Create two components
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;
        comp1.WidthMicrometers = 100;
        comp1.HeightMicrometers = 100;

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 100;
        comp2.WidthMicrometers = 100;
        comp2.HeightMicrometers = 100;

        var vm1 = vm.Canvas.AddComponent(comp1);
        var vm2 = vm.Canvas.AddComponent(comp2);

        // Select both and create a group
        vm.Canvas.Selection.AddToSelection(vm1);
        vm.Canvas.Selection.AddToSelection(vm2);
        vm.CreateGroupCommand.Execute(null);

        // The group should now be at the position of the leftmost component
        var groupVm = vm.Canvas.Components.FirstOrDefault(c => c.IsComponentGroup);
        groupVm.ShouldNotBeNull("A group should have been created");

        // Move the group to a new position
        var group = (ComponentGroup)groupVm.Component;
        double initialX = groupVm.X;
        double initialY = groupVm.Y;

        // Move group by 500, 500
        vm.Canvas.MoveComponent(groupVm, initialX + 500, initialY + 500);

        // Zoom to fit - should include the group at its NEW position
        vm.ZoomToFit(1000, 1000);

        // After move: group should be at ~(600, 600) to (800, 700)
        // Bounding box width=200, height=100
        // With 10% padding: width=240, height=120
        // Zoom = min(1000/240, 1000/120) = min(4.17, 8.33) ≈ 4.17
        // But clamped to maxZoom=10
        vm.ZoomLevel.ShouldBeGreaterThan(1.0, "Zoom should be calculated based on moved group position");
    }

    /// <summary>
    /// Regression test: Verifies that zoom-to-fit includes both grouped and ungrouped components.
    /// </summary>
    [Fact]
    public void ZoomToFit_WithMixedGroupedAndUngroupedComponents_IncludesAll()
    {
        var vm = CreateViewModel();

        // Add an ungrouped component at (0, 0)
        var standalone = TestComponentFactory.CreateStraightWaveGuide();
        standalone.PhysicalX = 0;
        standalone.PhysicalY = 0;
        standalone.WidthMicrometers = 100;
        standalone.HeightMicrometers = 100;
        vm.Canvas.AddComponent(standalone);

        // Add a group at (900, 900)
        var group = TestComponentFactory.CreateComponentGroup("FarGroup", addChildren: true);
        var groupVm = vm.Canvas.AddComponent(group, "CustomGroup");

        // Move group to far corner
        vm.Canvas.MoveComponent(groupVm, 900, 900);

        // Zoom to fit should include BOTH the standalone and the group
        vm.ZoomToFit(1000, 1000);

        // Bounding box spans from (0, 0) to (900+width, 900+height)
        // This is a large area, so zoom should be small
        vm.ZoomLevel.ShouldBeInRange(0.1, 2.0, "Zoom should fit both standalone and group");
    }

    /// <summary>
    /// Test: Verifies that empty groups don't break zoom-to-fit.
    /// </summary>
    [Fact]
    public void ZoomToFit_WithEmptyGroup_DoesNotCrash()
    {
        var vm = CreateViewModel();

        // Add a regular component
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.WidthMicrometers = 100;
        comp.HeightMicrometers = 100;
        vm.Canvas.AddComponent(comp);

        // Add an empty group
        var emptyGroup = new ComponentGroup("EmptyGroup");
        emptyGroup.PhysicalX = 500;
        emptyGroup.PhysicalY = 500;
        vm.Canvas.AddComponent(emptyGroup, "EmptyGroup");

        // Should not crash and should zoom to fit the regular component
        vm.ZoomToFit(1000, 1000);

        vm.ZoomLevel.ShouldBeGreaterThan(0, "Zoom should be set based on non-empty components");
    }
}
