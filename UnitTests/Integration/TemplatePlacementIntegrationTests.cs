using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for the template-only ComponentGroup architecture (Unity Prefab pattern).
/// Tests the full workflow: Create Template → Save to Library → Place as Ungrouped Components.
/// </summary>
public class TemplatePlacementIntegrationTests
{
    [Fact]
    public void CreateTemplate_SaveToLibrary_PlaceOnCanvas_WorksEndToEnd()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var libraryManager = new GroupLibraryManager();
        var libraryViewModel = new ComponentLibraryViewModel(libraryManager);

        // 1. Add components to canvas
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.PhysicalX = 200;
        comp2.PhysicalY = 100;

        var compVm1 = canvas.AddComponent(comp1);
        var compVm2 = canvas.AddComponent(comp2);

        // 2. Connect components
        var startPin = comp1.PhysicalPins.First();
        var endPin = comp2.PhysicalPins.First();
        canvas.ConnectionManager.AddConnection(startPin, endPin);

        // 3. Create template from selection
        var createTemplateCmd = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { compVm1, compVm2 },
            libraryViewModel
        );
        createTemplateCmd.Execute();

        // Assert: Template saved to library
        libraryViewModel.UserGroups.Count.ShouldBe(1, "Should have 1 template in library");
        var template = libraryViewModel.UserGroups.First();
        template.TemplateGroup.ShouldNotBeNull();
        template.TemplateGroup.ChildComponents.Count.ShouldBe(2);
        template.TemplateGroup.InternalPaths.Count.ShouldBe(1);

        // 4. Clear canvas to simulate fresh start
        canvas.Components.Clear();
        canvas.Connections.Clear();
        canvas.ConnectionManager.Clear();

        // 5. Place template on canvas
        var placeTemplateCmd = new PlaceTemplateCommand(
            canvas,
            template.TemplateGroup!,
            500,
            500
        );
        placeTemplateCmd.Execute();

        // Assert: Components placed as ungrouped
        canvas.Components.Count.ShouldBe(2, "Should place 2 ungrouped components");

        // Verify components are NOT in a ComponentGroup
        foreach (var compVm in canvas.Components)
        {
            compVm.Component.ParentGroup.ShouldBeNull("Components should be top-level");
            compVm.Component.ShouldNotBeOfType<ComponentGroup>("Canvas should not contain live groups");
        }

        // Verify connections created
        canvas.ConnectionManager.Connections.Count.ShouldBeGreaterThan(0,
            "Should create connections from template's frozen paths");
    }

    [Fact]
    public void PlaceTemplate_MultipleTimes_CreatesIndependentInstances()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var libraryManager = new GroupLibraryManager();
        var libraryViewModel = new ComponentLibraryViewModel(libraryManager);

        // Create template with 2 components
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.PhysicalX = 50;
        comp2.PhysicalY = 0;

        var compVm1 = canvas.AddComponent(comp1);
        var compVm2 = canvas.AddComponent(comp2);

        var createTemplateCmd = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { compVm1, compVm2 },
            libraryViewModel
        );
        createTemplateCmd.Execute();

        var template = libraryViewModel.UserGroups.First().TemplateGroup!;

        // Clear canvas
        canvas.Components.Clear();
        canvas.ConnectionManager.Clear();

        // Act: Place template 3 times
        var place1 = new PlaceTemplateCommand(canvas, template, 100, 100);
        place1.Execute();

        var place2 = new PlaceTemplateCommand(canvas, template, 300, 100);
        place2.Execute();

        var place3 = new PlaceTemplateCommand(canvas, template, 500, 100);
        place3.Execute();

        // Assert
        canvas.Components.Count.ShouldBe(6, "Should have 6 components (2 per template instance)");

        // Verify all components have unique identifiers
        var identifiers = canvas.Components.Select(c => c.Component.Identifier).ToList();
        identifiers.Distinct().Count().ShouldBe(6, "All components should have unique identifiers");

        // Verify all components track their source template
        foreach (var compVm in canvas.Components)
        {
            compVm.Component.SourceTemplate.ShouldNotBeNullOrEmpty("Should track source template");
        }
    }

    [Fact]
    public void CanvasWithoutGroups_NeverContainsLiveComponentGroups()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        // Add regular components
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        canvas.AddComponent(comp2);

        // Act & Assert: Canvas should never contain ComponentGroup instances
        foreach (var compVm in canvas.Components)
        {
            compVm.Component.ShouldNotBeOfType<ComponentGroup>(
                "Canvas should never contain live ComponentGroup instances. " +
                "Groups exist only as library templates.");
        }

        // Verify canvas is completely flat
        foreach (var compVm in canvas.Components)
        {
            compVm.Component.ParentGroup.ShouldBeNull("Canvas should be completely flat");
        }
    }
}
