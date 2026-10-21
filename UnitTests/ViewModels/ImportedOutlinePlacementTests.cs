using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels;

public class ImportedOutlinePlacementTests
{
    [Fact]
    public void CanvasPlacement_AllowsVoidInsideImportedCellButRejectsItsActualWall()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.Components.Add(Part(100, 100, true));
        canvas.CanPlaceComponent(150, 150, 10, 10).ShouldBeTrue();
        canvas.CanPlaceComponent(102, 150, 10, 10).ShouldBeFalse();
    }

    [Fact]
    public void MovingOutlinedCell_DoesNotBlockWithItsEmptyBoundingBox()
    {
        var canvas = new DesignCanvasViewModel();
        var imported = Part(300, 300, true);
        canvas.Components.Add(imported);
        canvas.Components.Add(Part(150, 150, false));
        canvas.CanMoveComponentTo(imported, 100, 100).ShouldBeTrue();
        canvas.CanMoveComponentTo(imported, 145, 140).ShouldBeFalse();
    }

    [Fact]
    public void NewOutlinedTemplate_StaysAtRequestedPositionWhenOnlyBoundsOverlap()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.Components.Add(Part(150, 150, false));
        var template = new CAP.Avalonia.ViewModels.Library.ComponentTemplate
        {
            Name = "concave", WidthMicrometers = 100, HeightMicrometers = 100,
            CreateWavelengthSMatrixMap = _ => new(),
            OutlinePolygons = Part(0, 0, true).Component.OutlinePolygons
        };
        var command = CAP.Avalonia.Commands.PlaceComponentCommand.TryCreate(canvas, template, 100, 100);
        command.ShouldNotBeNull();
        command.PlacedComponent!.PhysicalX.ShouldBe(100);
        command.PlacedComponent.PhysicalY.ShouldBe(100);
    }

    [Fact]
    public void MultiSelection_MayMoveIntoFreeSpaceOfImportedCell()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.Components.Add(Part(100, 100, true));
        var moved = Part(350, 350, false);
        canvas.Components.Add(moved);
        canvas.Selection.SelectedComponents.Add(moved);
        canvas.Selection.CanMoveGroup(canvas, -200, -200).ShouldBeTrue();
        canvas.Selection.CanMoveGroup(canvas, -248, -200).ShouldBeFalse();
    }

    private static ComponentViewModel Part(double x, double y, bool outline)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = outline ? 100 : 10;
        component.HeightMicrometers = outline ? 100 : 10;
        if (outline)
            component.OutlinePolygons = [new OutlinePolygon { Points = [
                new(0, 0), new(100, 0), new(100, 10),
                new(10, 10), new(10, 100), new(0, 100)] }];
        return new ComponentViewModel(component);
    }
}
