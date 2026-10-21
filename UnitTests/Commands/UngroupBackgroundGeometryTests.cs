using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

public class UngroupBackgroundGeometryTests
{
    [Fact]
    public void BackgroundRemainsBehindReleasedRoutesAcrossRedo()
    {
        var canvas = new DesignCanvasViewModel();
        var a = canvas.AddComponent(TestComponentFactory.CreateBasicComponent());
        var b = canvas.AddComponent(TestComponentFactory.CreateBasicComponent());
        var grouping = new CreateGroupCommand(canvas, [a, b], "source");
        grouping.Execute();
        var group = grouping.CreatedGroup!;
        group.OutlinePolygons = [new OutlinePolygon { Layer = 77, Points = new[] {
            new OutlinePoint(0, 0), new OutlinePoint(10, 0), new OutlinePoint(10, 10) } }];
        group.AddInternalPath(CAP.Avalonia.Services.GdsImport.GdsFrozenRoutePathFactory.Create(
            new CAP_DataAccess.Import.Gds.GdsOutlinePolygon { Layer = 1, Points = new[] {
                new CAP_DataAccess.Import.Gds.GdsOutlinePoint(0, 0), new CAP_DataAccess.Import.Gds.GdsOutlinePoint(10, 0),
                new CAP_DataAccess.Import.Gds.GdsOutlinePoint(10, 1) } }));
        var command = new UngroupCommand(canvas, group);
        command.Execute();
        canvas.CanvasFrozenPaths.Select(p => p.Path.Layer).ShouldBe(new int?[] { 77, 1 });
        command.Undo();
        command.Execute();
        canvas.CanvasFrozenPaths.Select(p => p.Path.Layer).ShouldBe(new int?[] { 77, 1 });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(37, true)]
    public void UngroupPreservesBackgroundPoseLayersUndoAndPersistence(double angle, bool mirror)
    {
        var canvas = new DesignCanvasViewModel();
        var a = TestComponentFactory.CreateBasicComponent();
        var b = TestComponentFactory.CreateBasicComponent();
        a.PhysicalX = 100; a.PhysicalY = 200;
        b.PhysicalX = 200; b.PhysicalY = 250;
        var groupCommand = new CreateGroupCommand(canvas, [canvas.AddComponent(a), canvas.AddComponent(b)], "source");
        groupCommand.Execute();
        var group = groupCommand.CreatedGroup!;
        var width = group.WidthMicrometers;
        var height = group.HeightMicrometers;
        var original = new OutlinePolygon { Layer = 77, DataType = 9, Points = new[] {
            new OutlinePoint(2, 3), new OutlinePoint(22, 3), new OutlinePoint(22, 13), new OutlinePoint(2, 13) } };
        group.OutlinePolygons = [original];
        if (mirror) ComponentPoseTransform.MirrorPinsHorizontally(group);
        ComponentPoseTransform.ApplyExactRotation(group, angle);
        var radians = angle * Math.PI / 180;
        var expected = original.Points.Select(point =>
        {
            var x = point.X - width / 2;
            var y = (point.Y - height / 2) * (mirror ? -1 : 1);
            return new OutlinePoint(group.PhysicalX + group.WidthMicrometers / 2 + x * Math.Cos(radians) - y * Math.Sin(radians),
                group.PhysicalY + group.HeightMicrometers / 2 + x * Math.Sin(radians) + y * Math.Cos(radians));
        }).ToList();
        var commands = new CommandManager();
        commands.ExecuteCommand(new UngroupCommand(canvas, group));
        var released = canvas.CanvasFrozenPaths.ShouldHaveSingleItem();
        var mask = released.Path.AsDrawnGeometry!.Polygons.ShouldHaveSingleItem();
        mask.Layer.ShouldBe(77); mask.DataType.ShouldBe(9);
        for (var i = 0; i < expected.Count; i++)
        {
            mask.Points[i].X.ShouldBe(expected[i].X, 1e-9);
            mask.Points[i].Y.ShouldBe(expected[i].Y, 1e-9);
        }
        canvas.Components.Count.ShouldBe(2, "background stays render-only, without an obstacle component");
        released.Path.StartPin.ShouldBeNull(); released.Path.EndPin.ShouldBeNull();
        var dto = ComponentGroupSerializer.ToCanvasFrozenPathDto(released.Path);
        var restored = ComponentGroupSerializer.FromCanvasFrozenPathDto(
            JsonSerializer.Deserialize<FrozenPathDto>(JsonSerializer.Serialize(dto))!);
        var restoredPoints = restored.AsDrawnGeometry!.Polygons.Single().Points;
        for (var i = 0; i < mask.Points.Count; i++)
        {
            restoredPoints[i].X.ShouldBe(mask.Points[i].X, 0.0005, ".lun masks use nanometer precision");
            restoredPoints[i].Y.ShouldBe(mask.Points[i].Y, 0.0005, ".lun masks use nanometer precision");
        }
        restored.Layer.ShouldBe(77); restored.DataType.ShouldBe(9);
        commands.Undo();
        canvas.CanvasFrozenPaths.ShouldBeEmpty();
        group.OutlinePolygons.ShouldHaveSingleItem().ShouldBeSameAs(original);
        commands.Redo();
        canvas.CanvasFrozenPaths.ShouldHaveSingleItem().ShouldBeSameAs(released);
    }
}
