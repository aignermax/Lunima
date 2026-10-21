using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Editing a lossless frozen import (<see cref="NazcaStyleChipFixture"/>): clicks pick the
/// waveguide over the background frame, moves and undo bring the drawn route back, foreign
/// components never silently unfreeze it, and the background frame never blocks placement.
/// </summary>
[Collection("LocalizationSingleton")]
public sealed class GdsLosslessFrozenEditingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lunima-lossless-edit-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<DesignCanvasViewModel> ImportAsync()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, 1000, 1000);
        var executor = new GdsPlacementExecutor(canvas, null, () => _host.Templates.ToList());
        var dialog = new GdsImportDialogViewModel(NazcaStyleChipFixture.WriteTo(_root), _host.CreateService(), executor);
        await dialog.StartAnalysisAsync();
        await dialog.ImportCommand.ExecuteAsync(null);
        dialog.HasError.ShouldBeFalse(dialog.ErrorText);
        return canvas;
    }

    private static (double X, double Y) MidOfFirstSegment(DesignCanvasViewModel canvas)
    {
        var first = canvas.Connections.Single().Connection.RoutedPath!.Segments[0];
        return ((first.StartPoint.X + first.EndPoint.X) / 2, (first.StartPoint.Y + first.EndPoint.Y) / 2);
    }

    private static ComponentViewModel Frame(DesignCanvasViewModel canvas) => canvas.Components.Single(c => c.Width > 300);

    [Fact]
    public async Task Click_OnTheWaveguideSelectsIt_OnEmptyFrameAreaSelectsTheFrame()
    {
        var canvas = await ImportAsync();
        var interaction = new CanvasInteractionViewModel(canvas, new CommandManager());

        var (x, y) = MidOfFirstSegment(canvas);
        interaction.CanvasClicked(x, y);
        interaction.SelectedWaveguideConnection.ShouldBe(canvas.Connections.Single());
        interaction.SelectedComponent.ShouldBeNull();

        var frame = Frame(canvas);
        interaction.CanvasClicked(frame.X + 5, frame.Y + 5);
        interaction.SelectedComponent.ShouldBe(frame, "background is still selectable where nothing else is");
        interaction.SelectedWaveguideConnection.ShouldBeNull();
    }

    [Fact]
    public async Task MoveDevice_ThenUndo_BringsTheDrawnRouteBack()
    {
        var canvas = await ImportAsync();
        var connection = canvas.Connections.Single().Connection;
        var source = connection.AsDrawnGeometry.ShouldNotBeNull();
        var device = canvas.Components.First(c => c.Width < 300 && !c.Component.IsMirroredHorizontally);
        var move = new MoveComponentCommand(canvas, device, device.X, device.Y, device.X, device.Y - 40);

        move.Execute();
        await canvas.RecalculateRoutesAsync();
        connection.AsDrawnGeometry.ShouldBeSameAs(source, "moving a device must not replace source geometry");
        connection.IsRouteFrozen.ShouldBeTrue();
        connection.HasImportedEndpointMismatch.ShouldBeTrue("the unchanged route no longer reaches the moved pin");
        connection.TransmissionCoefficient.ShouldBe(System.Numerics.Complex.Zero,
            "a disconnected imported route cannot carry simulated light");

        move.Undo();
        await canvas.RecalculateRoutesAsync();
        connection.IsRouteFrozen.ShouldBeTrue();
        connection.PathLengthMicrometers.ShouldBe(NazcaStyleChipFixture.RouteLengthUm, 0.01);
        connection.AsDrawnGeometry.ShouldNotBeNull("undo snaps the imported route and its polygons back");
        connection.HasImportedEndpointMismatch.ShouldBeFalse();
    }

    [Fact]
    public async Task JointDeviceMove_TranslatesEveryImportedLayer_WithoutRerouting()
    {
        var canvas = await ImportAsync();
        var connection = canvas.Connections.Single().Connection;
        var source = connection.AsDrawnGeometry.ShouldNotBeNull();
        var start = connection.StartPin.ParentComponent;
        var end = connection.EndPin.ParentComponent;
        start.PhysicalX += 20;
        start.PhysicalY += 30;
        end.PhysicalX += 20;
        end.PhysicalY += 30;

        await canvas.RecalculateRoutesAsync();

        connection.HasImportedEndpointMismatch.ShouldBeFalse();
        var moved = connection.AsDrawnGeometry.ShouldNotBeNull();
        moved.Polygons.Count.ShouldBe(source.Polygons.Count);
        for (int i = 0; i < source.Polygons.Count; i++)
        {
            moved.Polygons[i].Layer.ShouldBe(source.Polygons[i].Layer);
            moved.Polygons[i].DataType.ShouldBe(source.Polygons[i].DataType);
            for (int j = 0; j < source.Polygons[i].Points.Count; j++)
            {
                moved.Polygons[i].Points[j].X.ShouldBe(source.Polygons[i].Points[j].X + 20, 1e-8);
                moved.Polygons[i].Points[j].Y.ShouldBe(source.Polygons[i].Points[j].Y + 30, 1e-8);
            }
        }
    }

    [Fact]
    public async Task JointMoveSnapshotRestore_RestoresExactSourcePolygons()
    {
        var canvas = await ImportAsync();
        var connection = canvas.Connections.Single().Connection;
        var source = connection.AsDrawnGeometry.ShouldNotBeNull();
        var snapshot = CAP.Avalonia.ViewModels.Canvas.Services.RouteGeometrySnapshot.Capture(canvas);
        connection.ReplaceRoutedPath(connection.RoutedPath!.TranslatedCopy(20, 30));
        connection.AttachAsDrawnGeometry(source.Translated(20, 30));

        snapshot.Restore(canvas);

        connection.AsDrawnGeometry.ShouldBeSameAs(source);
    }

    [Fact]
    public async Task ForeignComponentOnTheDrawnRoute_DoesNotUnfreezeIt()
    {
        var canvas = await ImportAsync();
        var connection = canvas.Connections.Single().Connection;
        var (x, y) = MidOfFirstSegment(canvas);
        var blocker = TestComponentFactory.CreateStraightWaveGuide();
        blocker.PhysicalX = x - 5;
        blocker.PhysicalY = y - 5;
        canvas.AddComponent(blocker, "blocker", "test");

        await canvas.RecalculateRoutesAsync();

        connection.IsRouteFrozen.ShouldBeTrue("only the user unfreezes a drawn route");
        connection.AsDrawnGeometry.ShouldNotBeNull();
    }

    [Fact]
    public async Task BackgroundFrame_NeverBlocksPlacement_AndMovesFreely()
    {
        var canvas = await ImportAsync();
        var frame = Frame(canvas);
        var device = canvas.Components.First(c => c.Width < 300);

        canvas.CanPlaceComponent(device.X, device.Y + 10, device.Width, device.Height, device)
            .ShouldBeTrue("overlapping the frame is not a collision");
        canvas.CanPlaceComponent(frame.X + 1, frame.Y + 1, frame.Width, frame.Height, frame)
            .ShouldBeTrue("the frame moves over the devices it encloses");
    }

    [Fact]
    public async Task RerouteAll_UndoRedo_TogglesBetweenDrawnAndRouted()
    {
        var canvas = await ImportAsync();
        var target = canvas.Connections.Single();
        var reroute = new RerouteImportedRoutesCommand(canvas, new[] { target });

        reroute.Execute();
        await canvas.RecalculateRoutesAsync();
        reroute.Undo();
        target.Connection.AsDrawnGeometry.ShouldNotBeNull();

        reroute.Execute();
        await canvas.RecalculateRoutesAsync();
        target.Connection.IsRouteFrozen.ShouldBeFalse("redo re-routes again");
        target.Connection.AsDrawnGeometry.ShouldBeNull();
    }
}
