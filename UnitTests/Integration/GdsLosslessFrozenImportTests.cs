using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// End to end on a Nazca-style chip (<see cref="NazcaStyleChipFixture"/>): the import
/// dialog's defaults keep the finished layout exactly as drawn — a frozen, flat import
/// whose route carries a real centerline (for simulation and A*) plus the original
/// polygons (for display and export) — and a single waveguide can be unfrozen and
/// re-routed without touching anything else.
/// </summary>
[Collection("LocalizationSingleton")]
public sealed class GdsLosslessFrozenImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lunima-lossless-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>A canvas point on the route's first straight, halfway along it.</summary>
    private static global::Avalonia.Point CanvasPointOnFirstStraight(DesignCanvasViewModel canvas)
    {
        var first = canvas.Connections.Single().Connection.RoutedPath!.Segments[0];
        return new global::Avalonia.Point(
            (first.StartPoint.X + first.EndPoint.X) / 2, (first.StartPoint.Y + first.EndPoint.Y) / 2);
    }

    private async Task<DesignCanvasViewModel> ImportWithDialogDefaultsAsync()
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

    [Fact]
    public async Task Import_KeepsTheRouteFrozenWithAnExactCenterline()
    {
        var canvas = await ImportWithDialogDefaultsAsync();

        canvas.Components.Count.ShouldBe(3, "flat import: frame and two devices, no group");
        var connection = canvas.Connections.ShouldHaveSingleItem().Connection;
        connection.IsRouteFrozen.ShouldBeTrue();
        var segments = connection.RoutedPath!.Segments;
        segments.OfType<BendSegment>().Count().ShouldBe(2, "both drawn arcs are recognised as bends");
        segments.OfType<BendSegment>().ShouldAllBe(b => Math.Abs(b.RadiusMicrometers - NazcaStyleChipFixture.BendRadiusUm) < 1e-3);
        connection.PathLengthMicrometers.ShouldBe(NazcaStyleChipFixture.RouteLengthUm, 0.01);
        connection.WidthMicrometers.ShouldBe(NazcaStyleChipFixture.CoreWidthUm, 1e-6);
    }

    [Fact]
    public async Task Import_KeepsEveryDrawnPolygonOnTheRoute_AndMirrorsTheReflectedDevice()
    {
        var canvas = await ImportWithDialogDefaultsAsync();

        var drawn = canvas.Connections.Single().Connection.AsDrawnGeometry.ShouldNotBeNull();
        drawn.Polygons.Count.ShouldBe(10, "5 core ribbons plus their 5 cladding ribbons");
        drawn.Polygons.Select(p => p.Layer).Distinct().OrderBy(l => l).ShouldBe(new[] { 1, 2 });
        canvas.CanvasFrozenPaths.ShouldBeEmpty("no polygon is left over or dropped");
        canvas.Components.Count(c => c.Component.IsMirroredHorizontally).ShouldBe(1);
    }

    [Fact]
    public async Task DieFrame_ImportsAsBackground_ThatNeitherBlocksRoutingNorCatchesClicks()
    {
        var canvas = await ImportWithDialogDefaultsAsync();

        var frame = canvas.Components.Single(c => c.Width > 300).Component;
        frame.PhysicalPins.ShouldNotBeEmpty("the waveguide stub at its edge gives the frame guessed pins");
        frame.IsRoutingObstacle.ShouldBeFalse("a die frame enclosing the devices is background, not a device");

        // A press on the route (inside the frame's box) must not pick up the frame — the
        // waveguide under the cursor gets the click.
        CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestComponent(CanvasPointOnFirstStraight(canvas), canvas)
            .ShouldBeNull("background geometry is never picked up by the component hit test");
    }

    [Fact]
    public async Task ZoomedOut_AWaveguideIsClickableWithinAFewScreenPixels()
    {
        var canvas = await ImportWithDialogDefaultsAsync();
        var onRoute = CanvasPointOnFirstStraight(canvas);
        var nearby = new global::Avalonia.Point(onRoute.X, onRoute.Y + 30); // 30 µm off the waveguide

        canvas.ViewZoom = 1.0;
        CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestConnection(nearby, canvas)
            .ShouldBeNull("at 100 % zoom 30 µm are 30 px — clearly beside the waveguide");

        canvas.ViewZoom = 0.1;
        CAP.Avalonia.Controls.DesignCanvasHitTesting.HitTestConnection(nearby, canvas)
            .ShouldNotBeNull("at 10 % zoom 30 µm are 3 px — a click there means the waveguide");
    }

    [Fact]
    public async Task SaveAndReload_KeepsTheFrozenRouteAndItsPolygons()
    {
        var canvas = await ImportWithDialogDefaultsAsync();
        var lun = Path.Combine(_root, "chip.lun");
        await GdsImportJourneyFixture.SaveToFile(GdsImportJourneyFixture.CreateFileOperations(canvas, _host), lun);

        using var loadHost = new GdsDesignScopeTestHost();
        var reloaded = new DesignCanvasViewModel();
        reloaded.InitializeAStarRouting(0, 0, 1000, 1000);
        await GdsImportJourneyFixture.LoadFromFile(GdsImportJourneyFixture.CreateFileOperations(reloaded, loadHost), lun);

        var connection = reloaded.Connections.ShouldHaveSingleItem().Connection;
        connection.IsRouteFrozen.ShouldBeTrue();
        connection.AsDrawnGeometry.ShouldNotBeNull().Polygons.Count.ShouldBe(10);
        reloaded.Components.Count(c => c.Component.IsMirroredHorizontally).ShouldBe(1);
        reloaded.Components.Single(c => c.Width > 300).Component.IsRoutingObstacle
            .ShouldBeFalse("the background flag is persisted in the .lun");
    }

    [Fact]
    public async Task RerouteOneWaveguide_ReplacesItsGeometry_AndUndoBringsTheDrawingBack()
    {
        var canvas = await ImportWithDialogDefaultsAsync();
        var target = canvas.Connections.Single();
        var reroute = new RerouteImportedRoutesCommand(canvas, new[] { target });

        reroute.Execute();
        await canvas.RecalculateRoutesAsync();

        target.Connection.IsRouteFrozen.ShouldBeFalse();
        target.Connection.RoutedPath.ShouldNotBeNull();
        target.Connection.IsBlockedFallback.ShouldBeFalse("the router finds its own path between the devices");
        target.Connection.AsDrawnGeometry.ShouldBeNull("a re-routed waveguide no longer shows the drawing");

        reroute.Undo();

        target.Connection.IsRouteFrozen.ShouldBeTrue();
        target.Connection.PathLengthMicrometers.ShouldBe(NazcaStyleChipFixture.RouteLengthUm, 0.01);
        target.Connection.AsDrawnGeometry.ShouldNotBeNull("undo brings the drawn polygons back with the route");
    }

    [Fact]
    public async Task RerouteSelected_WithNoFreePath_KeepsTheDrawnRoute()
    {
        var canvas = await ImportWithDialogDefaultsAsync();
        var target = canvas.Connections.Single();

        // Wall the end device's input in: a component body right in front of the pin
        // leaves the router nothing but a blocked fallback.
        var (endX, endY) = target.Connection.EndPin.GetAbsolutePosition();
        var wall = UnitTests.TestComponentFactory.CreateStraightWaveGuide();
        wall.PhysicalX = endX - 120;
        wall.PhysicalY = endY - 60;
        wall.WidthMicrometers = 110;
        wall.HeightMicrometers = 120;
        canvas.AddComponent(wall, "wall", "test");

        var reroute = new CAP.Avalonia.ViewModels.Canvas.RerouteImported.RerouteImportedRoutesViewModel(canvas, new CommandManager())
        {
            SelectedConnection = target,
        };
        await reroute.RerouteSelectedCommand.ExecuteAsync(null);

        target.Connection.IsBlockedFallback.ShouldBeFalse("no dashed placeholder replaces the working drawn route");
        target.Connection.IsRouteFrozen.ShouldBeTrue();
        target.Connection.PathLengthMicrometers.ShouldBe(NazcaStyleChipFixture.RouteLengthUm, 0.01);
        target.Connection.AsDrawnGeometry.ShouldNotBeNull();
        reroute.ResultText.ShouldContain("1");
    }

    [Fact]
    public async Task Export_WritesTheDrawnPolygonsVerbatim_AndFlipsTheMirroredDevice()
    {
        var canvas = await ImportWithDialogDefaultsAsync();

        var script = new SimpleNazcaExporter().Export(canvas, library: _host.Templates.ToList());

        script.ShouldContain("flip=True");
        var polygonLines = script.Split('\n').Count(l => l.Contains("nd.Polygon(") && (l.Contains("layer=(1, 0)") || l.Contains("layer=(2, 0)")));
        polygonLines.ShouldBe(10, "the route exports as its 10 drawn polygons, not as re-derived waveguides");
    }
}
