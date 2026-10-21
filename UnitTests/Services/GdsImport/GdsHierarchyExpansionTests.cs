using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsHierarchyExpansionTests
{
    private static byte[] Source(bool children = true, double magnification = 1) => GdsTestWriter.Create()
        .StandardPrologue().BeginCell("child")
        .Boundary(20, 3, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0)).EndCell()
        .BeginCell("parent")
        .Boundary(30, 7, (0, 0), (1000, 0), (1000, 1000), (0, 1000), (0, 0))
        .SRef(children ? "child" : "empty", 20000, 10000, angleDegrees: 30,
            reflected: true, magnification: magnification)
        .EndCell().BeginCell("empty").EndCell().EndLibrary().ToArray();

    internal static async Task<(DesignCanvasViewModel Canvas, ComponentViewModel Original,
        GdsHierarchyExpansionService Service)> Setup(GdsDesignScopeTestHost host, byte[] bytes,
        double angle = 0, bool mirror = false, string cell = "parent")
    {
        using var stream = new MemoryStream(bytes);
        var library = await new GdsReader().ReadAsync(stream);
        var import = await GdsHierarchyImporter.ImportAsync(library, cell,
            new GdsHierarchyImportOptions { Mode = GdsHierarchyImportMode.BlackBox });
        var draft = GdsCellDraftMapper.Map(import.ImportedCellDrafts.Single(), GdsHierarchyImporter.GdsFileNameToken);
        host.Scope.AddAndRegister(new DesignScopedGdsSet
        {
            PdkName = "GDS Import - source", GdsFileName = "source.gds", GdsBytes = bytes, Drafts = [draft]
        });
        var canvas = new DesignCanvasViewModel();
        var placement = PlaceComponentCommand.CreateExact(canvas, host.Templates.Single(), 100, 200,
            mirrorPinsHorizontally: mirror, exactRotationDegrees: angle);
        placement.Execute();
        return (canvas, placement.CreatedViewModel!, new GdsHierarchyExpansionService(host.Scope));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(37, true)]
    public async Task ExpansionPreservesEveryPolygonAndSourcePdkAcrossUndoRedo(double angle, bool mirror)
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source(), angle, mirror);
        var expected = WorldPolygons(original.Component).ToList();
        var command = await service.PrepareAsync(canvas, original);
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
        host.Templates.Count.ShouldBe(1);
        var commands = new CommandManager();
        commands.ExecuteCommand(command);
        canvas.Components.Count.ShouldBe(2);
        canvas.Components.ShouldAllBe(c => c.TemplatePdkSource == original.TemplatePdkSource);
        host.Templates.Count.ShouldBe(3);
        var actual = canvas.Components.SelectMany(c => WorldPolygons(c.Component))
            .Concat(canvas.CanvasFrozenPaths.SelectMany(p => p.Path.AsDrawnGeometry!.Polygons)
                .Select(p => (p.Layer, p.DataType, Points: p.Points.Select(x => (x.X, x.Y)).ToList()))).ToList();
        actual.Count.ShouldBe(expected.Count);
        foreach (var polygon in expected)
        {
            var counterpart = actual.Single(p => p.Layer == polygon.Layer && p.DataType == polygon.DataType);
            counterpart.Points.Count.ShouldBe(polygon.Points.Count);
            foreach (var point in polygon.Points)
                counterpart.Points.ShouldContain(p => Math.Abs(p.Item1 - point.Item1) < 1e-6 && Math.Abs(p.Item2 - point.Item2) < 1e-6);
        }
        commands.Undo();
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
        canvas.CanvasFrozenPaths.ShouldBeEmpty();
        commands.Redo();
        canvas.Components.ShouldBe(command.Children);
        host.Scope.CaptureForSave([original.TemplatePdkSource])!.Single().Components.Count.ShouldBe(3);
    }

    [Fact]
    public async Task FlatCellRefusesWithoutMutation()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source(), cell: "child");
        var error = await Should.ThrowAsync<InvalidOperationException>(() => service.PrepareAsync(canvas, original));
        error.Message.ShouldContain("no child cell references");
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
        host.Templates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task MagnifiedReferenceRefusesWithoutMutation()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source(magnification: 2));
        (await Should.ThrowAsync<InvalidOperationException>(() => service.PrepareAsync(canvas, original)))
            .Message.ShouldContain("magnified");
        host.Templates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StalePoseAndClosedScopeRefuseBeforeRegistration()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source());
        var command = await service.PrepareAsync(canvas, original);
        original.Component.PhysicalX++;
        Should.Throw<InvalidOperationException>(() => command.Execute()).Message.ShouldContain("moved");
        original.Component.PhysicalX--;
        host.Scope.ClearDesignScope();
        Should.Throw<InvalidOperationException>(() => command.Execute()).Message.ShouldContain("closed");
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
    }

    [Fact]
    public async Task UnmappedPortRefusesWithoutDroppingIt()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source(), cell: "child");
        original.Component.PhysicalPins.Add(new PhysicalPin { Name = "user-port", ParentComponent = original.Component });
        (await Should.ThrowAsync<InvalidOperationException>(() => service.PrepareAsync(canvas, original)))
            .Message.ShouldContain("no child cell references");
        original.Component.PhysicalPins.ShouldHaveSingleItem();
        host.Templates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConnectionAddedAfterPreparationBlocksExecutionWithoutDeletingWire()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source());
        var command = await service.PrepareAsync(canvas, original);
        var a = new PhysicalPin { Name = "a", ParentComponent = original.Component };
        var b = new PhysicalPin { Name = "b", ParentComponent = original.Component };
        var wire = new WaveguideConnection { StartPin = a, EndPin = b };
        canvas.ConnectionManager.AddExistingConnection(wire);
        Should.Throw<InvalidOperationException>(() => command.Execute()).Message.ShouldContain("external connections");
        canvas.ConnectionManager.Connections.ShouldContain(wire);
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
        host.Templates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GroupThenUngroupRecoversUniqueSourceSetAndExposesChildren()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source());
        var sibling = PlaceComponentCommand.CreateExact(canvas, host.Templates.Single(), 400, 200);
        sibling.Execute();
        var group = new CreateGroupCommand(canvas, [original, sibling.CreatedViewModel!], "import");
        group.Execute();
        new UngroupCommand(canvas, group.CreatedGroup!).Execute();
        var restored = canvas.Components.Single(c => ReferenceEquals(c.Component, original.Component));
        restored.TemplatePdkSource.ShouldBeNull();
        service.IsImportedComponent(restored).ShouldBeTrue();
        var command = await service.PrepareAsync(canvas, restored);
        command.Execute();
        command.Children.ShouldAllBe(c => c.TemplatePdkSource == host.Scope.Sets.Single().PdkName);
        command.Undo();
        canvas.Components.ShouldContain(restored);
    }

    [Fact]
    public async Task MissingPdkMetadataNeverGuessesBetweenRepeatedImports()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original, service) = await Setup(host, Source());
        host.Scope.AddAndRegister(host.Scope.Sets.Single() with { PdkName = "other imported set" });
        original.TemplatePdkSource = null;
        original.TemplateName = null;
        service.IsImportedComponent(original).ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => service.PrepareAsync(canvas, original));
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
    }

    private static IEnumerable<(int Layer, int DataType, List<(double, double)> Points)> WorldPolygons(Component component)
    {
        var width = component.UnrotatedWidthMicrometers > 0 ? component.UnrotatedWidthMicrometers : component.WidthMicrometers;
        var height = component.UnrotatedHeightMicrometers > 0 ? component.UnrotatedHeightMicrometers : component.HeightMicrometers;
        var radians = component.RotationDegrees * Math.PI / 180;
        return component.OutlinePolygons!.Select(p => (p.Layer, p.DataType, p.Points.Select(point =>
        {
            var x = point.X - width / 2;
            var y = (point.Y - height / 2) * (component.IsMirroredHorizontally ? -1 : 1);
            return (component.PhysicalX + component.WidthMicrometers / 2 + x * Math.Cos(radians) - y * Math.Sin(radians),
                component.PhysicalY + component.HeightMicrometers / 2 + x * Math.Sin(radians) + y * Math.Cos(radians));
        }).ToList()));
    }
}
