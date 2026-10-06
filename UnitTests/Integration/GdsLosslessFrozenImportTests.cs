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

        canvas.Components.Count.ShouldBe(2, "flat import: no group around the two devices");
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
    public async Task Export_WritesTheDrawnPolygonsVerbatim_AndFlipsTheMirroredDevice()
    {
        var canvas = await ImportWithDialogDefaultsAsync();

        var script = new SimpleNazcaExporter().Export(canvas, library: _host.Templates.ToList());

        script.ShouldContain("flip=True");
        var polygonLines = script.Split('\n').Count(l => l.Contains("nd.Polygon(") && (l.Contains("layer=(1, 0)") || l.Contains("layer=(2, 0)")));
        polygonLines.ShouldBe(10, "the route exports as its 10 drawn polygons, not as re-derived waveguides");
    }
}
