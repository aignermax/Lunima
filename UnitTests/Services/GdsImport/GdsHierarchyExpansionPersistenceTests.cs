using CAP_DataAccess.Persistence.NativeDesign;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Grid;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Tiles;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsHierarchyExpansionPersistenceTests
{
    private static byte[] Source() => GdsTestWriter.Create().StandardPrologue()
        .BeginCell("child").Boundary(20, 3, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
        .Text(1, 10, "o1", 0, 2000, 180).EndCell()
        .BeginCell("parent").SRef("child", 20000, 10000)
        .Boundary(30, 7, (0, 0), (10000, 0), (10000, 10000), (0, 10000), (0, 0))
        .EndCell().EndLibrary().ToArray();

    [Theory]
    [InlineData(0, false)]
    [InlineData(37, true)]
    public async Task ParentAliasesKindsAndOwnInterfaceSurviveScopeSaveReload(double angle, bool mirror)
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await GdsHierarchyExpansionTests.Setup(host, Source(), angle, mirror);
        var interfacePin = original.Component.PhysicalPins.Single();
        interfacePin.Name = "interface";
        interfacePin.LogicalPin!.Name = "interface";
        interfacePin.LogicalPin.MatterType = MatterType.Electricity;
        interfacePin.Layer = 72;
        var extra = new PhysicalPin { Name = "own-contact", ParentComponent = original.Component,
            OffsetXMicrometers = original.Component.WidthMicrometers / 2,
            OffsetYMicrometers = original.Component.HeightMicrometers / 2, AngleDegrees = 0,
            LogicalPin = new Pin("own-contact", 10, MatterType.Electricity, RectSide.Right) };
        original.Component.PhysicalPins.Add(extra);
        var expected = original.Component.PhysicalPins.Select(Snapshot).ToList();
        var command = await service.PrepareAsync(canvas, original);
        command.Execute();
        var expanded = canvas.Components.SelectMany(c => c.Component.PhysicalPins).Select(Snapshot).ToList();
        foreach (var pin in expected) expanded.ShouldContain(pin);
        var saved = host.Scope.CaptureForSave(canvas.Components.Select(c => c.TemplatePdkSource))!;
        var restoredData = JsonSerializer.Deserialize<List<ImportedGdsComponentSetData>>(JsonSerializer.Serialize(saved))!;
        using var restoredHost = new GdsDesignScopeTestHost();
        restoredHost.Scope.RestoreDesignScope(restoredData);
        var restoredCanvas = new DesignCanvasViewModel();
        foreach (var child in canvas.Components)
        {
            var template = restoredHost.Templates.Single(t => t.Name == child.TemplateName);
            var placement = PlaceComponentCommand.CreateExact(restoredCanvas, template, child.Component.PhysicalX,
                child.Component.PhysicalY, exactRotationDegrees: child.Component.RotationDegrees,
                mirrorPinsHorizontally: child.Component.IsMirroredHorizontally);
            placement.Execute();
        }
        var restoredPins = restoredCanvas.Components.SelectMany(c => c.Component.PhysicalPins).Select(Snapshot).ToList();
        foreach (var pin in expected) restoredPins.ShouldContain(pin);
        var own = restoredHost.Templates.Single(t => t.SourceDraft!.ImportedOwnGeometryOnly && t.PinDefinitions.Length > 0);
        own.RawCode!.ShouldContain("nd.Polygon");
        own.RawCode.ShouldNotContain("load_gds");
        own.SourceDraft!.ImportedSourceCellName.ShouldBe("parent");
        command.Undo();
        original.Component.PhysicalPins.Select(Snapshot).ShouldBe(expected);
    }

    [Fact]
    public async Task OwnPolygonsKeepPlacementAndRoutingOccupancy()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await GdsHierarchyExpansionTests.Setup(host, Source());
        var command = await service.PrepareAsync(canvas, original);
        command.Execute();
        var own = canvas.Components.Single(c => host.Templates.Single(t => t.Name == c.TemplateName).SourceDraft!.ImportedOwnGeometryOnly);
        own.Component.IsRoutingObstacle.ShouldBeTrue();
        new GroupCollisionDetector().CanPlaceFootprint(102, 207, 2, 2, canvas.Components.Select(c => c.Component)).ShouldBeFalse();
        var grid = new PathfindingGrid(0, 0, 300, 300);
        foreach (var child in canvas.Components) grid.AddComponentObstacle(child.Component);
        grid.IsBlocked(105, 209).ShouldBeTrue();
        command.Undo();
        original.Component.IsRoutingObstacle = false;
        var background = await service.PrepareAsync(canvas, original);
        background.Execute();
        canvas.Components.ShouldAllBe(c => !c.Component.IsRoutingObstacle);
    }

    private static string Snapshot(PhysicalPin pin)
    {
        var point = pin.GetAbsolutePosition();
        return FormattableString.Invariant($"{pin.Name}:{point.x:F6}:{point.y:F6}:{pin.GetAbsoluteAngle():F6}:{pin.MatterType}:{pin.Layer}");
    }
}
