using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for template origin metadata tracking (Issue #218).
/// Tests that components remember which template they came from and share instance IDs.
/// </summary>
public class TemplateOriginMetadataTests
{
    /// <summary>
    /// Creates a simple template with two components and a connection for testing.
    /// </summary>
    private ComponentGroup CreateTestTemplate(string name = "TestTemplate")
    {
        var template = new ComponentGroup(name)
        {
            Description = "Test template",
            PhysicalX = 0,
            PhysicalY = 0,
            IsPrefab = true
        };

        // Create two components
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.Identifier = $"{name}_comp1";
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.Identifier = $"{name}_comp2";
        comp2.PhysicalX = 50;
        comp2.PhysicalY = 0;

        template.AddChild(comp1);
        template.AddChild(comp2);

        // Add a frozen path between them
        var startPin = comp1.PhysicalPins.First();
        var endPin = comp2.PhysicalPins.First();

        var (startPinX, startPinY) = startPin.GetAbsolutePosition();
        var (endPinX, endPinY) = endPin.GetAbsolutePosition();

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(
            startPinX,
            startPinY,
            endPinX,
            endPinY,
            0
        ));

        var frozenPath = new FrozenWaveguidePath
        {
            Path = path,
            StartPin = startPin,
            EndPin = endPin
        };

        template.AddInternalPath(frozenPath);
        template.UpdateGroupBounds();

        return template;
    }

    [Fact]
    public void PlaceTemplate_SetsSourceTemplateAndInstanceId()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateTestTemplate("MZI_v1");

        // Act: Place template
        var placeCmd = new PlaceTemplateCommand(canvas, template, 100, 100);
        placeCmd.Execute();

        // Assert: All components have source template set
        canvas.Components.Count.ShouldBe(2);
        foreach (var compVm in canvas.Components)
        {
            compVm.Component.SourceTemplate.ShouldNotBeNullOrEmpty("Should track source template");
            compVm.Component.SourceTemplate.ShouldBe("MZI_v1");
        }

        // Assert: All components share the same instance ID
        var instanceIds = canvas.Components.Select(c => c.Component.TemplateInstanceId).Distinct().ToList();
        instanceIds.Count.ShouldBe(1, "All components from same placement should share instance ID");
        instanceIds.First().ShouldNotBeNull("Instance ID should be set");
    }

    [Fact]
    public void PlaceTemplate_MultipleTimes_HasUniqueInstanceIds()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateTestTemplate("MZI_v2");

        // Act: Place template 3 times
        var place1 = new PlaceTemplateCommand(canvas, template, 100, 100);
        place1.Execute();

        var place2 = new PlaceTemplateCommand(canvas, template, 300, 100);
        place2.Execute();

        var place3 = new PlaceTemplateCommand(canvas, template, 500, 100);
        place3.Execute();

        // Assert: Should have 3 unique instance IDs (one per placement)
        var allInstanceIds = canvas.Components.Select(c => c.Component.TemplateInstanceId).ToList();
        allInstanceIds.Count.ShouldBe(6, "Should have 6 components total");

        var uniqueInstanceIds = allInstanceIds.Distinct().ToList();
        uniqueInstanceIds.Count.ShouldBe(3, "Should have 3 unique instance IDs (one per placement)");

        // Each instance should have exactly 2 components
        foreach (var instanceId in uniqueInstanceIds)
        {
            var count = allInstanceIds.Count(id => id == instanceId);
            count.ShouldBe(2, $"Instance {instanceId} should have 2 components");
        }
    }

    [Fact]
    public void ComponentViewModel_ExposesTemplateOriginProperties()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateTestTemplate("DirectionalCoupler");

        var placeCmd = new PlaceTemplateCommand(canvas, template, 100, 100);
        placeCmd.Execute();

        // Act & Assert
        var placedVm = canvas.Components.First();
        placedVm.IsFromTemplate.ShouldBeTrue("Should be from template");
        placedVm.SourceTemplateName.ShouldBe("DirectionalCoupler");
        placedVm.TemplateInstanceId.ShouldNotBeNull("Should have instance ID");
        placedVm.TemplateOriginTooltip.ShouldContain("From template:");
        placedVm.TemplateOriginTooltip.ShouldContain("DirectionalCoupler");
    }

    [Fact]
    public void ManuallyPlacedComponent_HasNoTemplateOrigin()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateSimpleTwoPortComponent();

        // Act: Place component manually (not from template)
        var compVm = canvas.AddComponent(comp);

        // Assert: Should not have template origin metadata
        compVm.Component.SourceTemplate.ShouldBeNull("Manually placed component should not have template");
        compVm.Component.TemplateInstanceId.ShouldBeNull("Manually placed component should not have instance ID");
        compVm.IsFromTemplate.ShouldBeFalse("Should not be from template");
        compVm.TemplateOriginTooltip.ShouldBeNull("Should not have tooltip");
    }

    [Fact]
    public void TemplateOriginViewModel_TracksTemplateInstances()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var templateOriginVm = new TemplateOriginViewModel(canvas);
        var template = CreateTestTemplate("PhaseShifter");

        // Place template twice
        var place1 = new PlaceTemplateCommand(canvas, template, 100, 100);
        place1.Execute();

        var place2 = new PlaceTemplateCommand(canvas, template, 300, 100);
        place2.Execute();

        // Act: Refresh template instances
        templateOriginVm.RefreshTemplateInstancesCommand.Execute(null);

        // Assert
        templateOriginVm.HasTemplateInstances.ShouldBeTrue("Should have template instances");
        templateOriginVm.TemplateInstances.Count.ShouldBe(2, "Should track 2 template instances");

        foreach (var instance in templateOriginVm.TemplateInstances)
        {
            instance.ComponentCount.ShouldBe(2, "Each instance should have 2 components");
            instance.TemplateName.ShouldBe("PhaseShifter");
            instance.InstanceId.ShouldNotBe(Guid.Empty, "Should have valid instance ID");
        }
    }

    [Fact]
    public void TemplateOriginViewModel_SelectTemplateInstance_SelectsAllComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var templateOriginVm = new TemplateOriginViewModel(canvas);
        var template = CreateTestTemplate("Filter");

        var placeCmd = new PlaceTemplateCommand(canvas, template, 100, 100);
        placeCmd.Execute();

        templateOriginVm.RefreshTemplateInstancesCommand.Execute(null);
        var instance = templateOriginVm.TemplateInstances.First();

        // Act: Select template instance
        templateOriginVm.SelectTemplateInstanceCommand.Execute(instance);

        // Assert: All components from instance should be selected
        canvas.Selection.SelectedComponents.Count.ShouldBe(2, "Should select all components from instance");

        foreach (var comp in canvas.Selection.SelectedComponents)
        {
            comp.TemplateInstanceId.ShouldBe(instance.InstanceId, "Selected component should be from instance");
        }
    }
}
