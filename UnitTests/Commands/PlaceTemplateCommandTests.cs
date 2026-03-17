using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for PlaceTemplateCommand - instantiating ComponentGroup templates as ungrouped components.
/// </summary>
public class PlaceTemplateCommandTests
{
    /// <summary>
    /// Creates a simple template with two components and a connection.
    /// </summary>
    private ComponentGroup CreateSimpleTemplate()
    {
        var template = new ComponentGroup("Test_MZI")
        {
            Description = "Test Mach-Zehnder Interferometer",
            PhysicalX = 0,
            PhysicalY = 0,
            IsPrefab = true
        };

        // Create two simple components
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.Identifier = "splitter1";
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.Identifier = "splitter2";
        comp2.PhysicalX = 200;
        comp2.PhysicalY = 100;

        template.AddChild(comp1);
        template.AddChild(comp2);

        // Add a frozen path between them
        var startPin = comp1.PhysicalPins.First(p => p.Name == "o1");
        var endPin = comp2.PhysicalPins.First(p => p.Name == "o0");

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
    public void PlaceTemplate_CreatesUngroupedComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateSimpleTemplate();
        var placeX = 500.0;
        var placeY = 500.0;

        // Act
        var command = new PlaceTemplateCommand(canvas, template, placeX, placeY);
        command.Execute();

        // Assert
        canvas.Components.Count.ShouldBe(2, "Should place 2 ungrouped components");

        // Verify components are top-level (no parent group)
        foreach (var compVm in canvas.Components)
        {
            compVm.Component.ParentGroup.ShouldBeNull("Components should be top-level, not in a group");
        }

        // Verify components have SourceTemplate set
        canvas.Components[0].Component.SourceTemplate.ShouldBe("Test_MZI");
        canvas.Components[1].Component.SourceTemplate.ShouldBe("Test_MZI");
    }

    [Fact]
    public void PlaceTemplate_CreatesConnections()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateSimpleTemplate();

        // Act
        var command = new PlaceTemplateCommand(canvas, template, 500, 500);
        command.Execute();

        // Assert
        canvas.ConnectionManager.Connections.Count.ShouldBeGreaterThan(0, "Should create connections from frozen paths");
    }

    [Fact]
    public void PlaceTemplate_OffsetsComponentsToPlacementPosition()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateSimpleTemplate();
        var placeX = 1000.0;
        var placeY = 2000.0;

        // Original template origin
        var templateOriginX = template.PhysicalX;
        var templateOriginY = template.PhysicalY;

        // Expected offset
        var offsetX = placeX - templateOriginX;
        var offsetY = placeY - templateOriginY;

        // Act
        var command = new PlaceTemplateCommand(canvas, template, placeX, placeY);
        command.Execute();

        // Assert
        // First component should be at its original position + offset
        var firstComp = canvas.Components[0].Component;
        firstComp.PhysicalX.ShouldBe(100 + offsetX, 0.1);
        firstComp.PhysicalY.ShouldBe(100 + offsetY, 0.1);
    }

    [Fact]
    public void PlaceTemplate_SelectsPlacedComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateSimpleTemplate();

        // Act
        var command = new PlaceTemplateCommand(canvas, template, 500, 500);
        command.Execute();

        // Assert
        canvas.Selection.SelectedComponents.Count.ShouldBe(2, "Should select all placed components");
    }

    [Fact]
    public void PlaceTemplate_Undo_RemovesPlacedComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateSimpleTemplate();
        var command = new PlaceTemplateCommand(canvas, template, 500, 500);
        command.Execute();

        // Act
        command.Undo();

        // Assert
        canvas.Components.Count.ShouldBe(0, "Undo should remove all placed components");
        canvas.ConnectionManager.Connections.Count.ShouldBe(0, "Undo should remove all connections");
    }

    [Fact]
    public void PlaceTemplate_MultipleTimes_CreatesIndependentInstances()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateSimpleTemplate();

        // Act - Place twice
        var command1 = new PlaceTemplateCommand(canvas, template, 500, 500);
        command1.Execute();

        var command2 = new PlaceTemplateCommand(canvas, template, 1000, 1000);
        command2.Execute();

        // Assert
        canvas.Components.Count.ShouldBe(4, "Should have 4 components (2 from each template instance)");

        // Verify each component has a unique identifier
        var identifiers = canvas.Components.Select(c => c.Component.Identifier).ToList();
        identifiers.Distinct().Count().ShouldBe(4, "All components should have unique identifiers");
    }

    [Fact]
    public void PlaceTemplate_DoesNotModifyOriginalTemplate()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var template = CreateSimpleTemplate();
        var originalChildCount = template.ChildComponents.Count;
        var originalX = template.PhysicalX;

        // Act
        var command = new PlaceTemplateCommand(canvas, template, 1000, 2000);
        command.Execute();

        // Assert
        template.ChildComponents.Count.ShouldBe(originalChildCount, "Original template should be unchanged");
        template.PhysicalX.ShouldBe(originalX, "Original template position should be unchanged");
    }
}
