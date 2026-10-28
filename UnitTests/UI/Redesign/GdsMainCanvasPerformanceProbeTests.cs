using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using UnitTests.Services.GdsImport;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.UI.Redesign;

/// <summary>Opt-in whole-canvas probe. Private layouts stay outside the repository.</summary>
[Collection("LocalizationSingleton")]
public class GdsMainCanvasPerformanceProbeTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task LocalLayout_FullCanvasZoomSequence()
    {
        var path = Environment.GetEnvironmentVariable("GDS_MAIN_CANVAS_PROBE");
        if (string.IsNullOrWhiteSpace(path)) return;
        using var host = new GdsDesignScopeTestHost();
        var service = host.CreateService();
        var analysis = await new GdsImportService(new CAP_DataAccess.Import.Gds.GdsSourceReader()).AnalyzeAsync(path);
        var top = analysis.TopCellCandidates[0];
        var review = new GdsImportInterpretation
        {
            SourceSha256 = analysis.SourceSha256,
            TopCellName = top,
            Cells = analysis.Library!.Cells.Where(p => p.Key != top && p.Value.Elements.OfType<GdsReference>().Count() > 10)
                .Select(p => new GdsCellInterpretation { CellName = p.Key, ExpandChildren = true }).ToList(),
            Layers = [new(18, 0, GdsLayerRole.OpticalCore), new(404, 0, GdsLayerRole.OpticalCore)]
        };
        // Optional richer review scenario; keeps geometry-only baseline reproducible.
        if (Environment.GetEnvironmentVariable("GDS_MAIN_CANVAS_PORTS") == "1")
            review.Layers.AddRange([new(234, 0, GdsLayerRole.PortMarker),
                new(235, 0, GdsLayerRole.PortLabel), new(51, 0, GdsLayerRole.Electrical),
                new(451, 0, GdsLayerRole.Electrical)]);
        var watch = Stopwatch.StartNew();
        var outcome = await service.ImportAsync(path, top, preParsedLibrary: analysis.Library,
            interpretation: review, sourceBytes: analysis.SourceBytes);
        var canvas = new DesignCanvasViewModel();
        var report = await new GdsPlacementExecutor(canvas, new CommandManager(), () => host.Templates)
            .ExecuteAsync(GdsPlacementPlan.FromOutcome(outcome), rerouteImportedConnections: false);
        output.WriteLine($"Import + placement: {watch.Elapsed.TotalSeconds:F2}s; components {report.PlacedCount}; connections {canvas.Connections.Count}");
        var groups = canvas.Components.Select(c => c.Component).OfType<CAP_Core.Components.Core.ComponentGroup>().ToArray();
        output.WriteLine($"Top-level groups {groups.Length}; internal paths {groups.Sum(g => g.InternalPaths.Count)}; external pins {groups.Sum(g => g.ExternalPins.Count)}; frozen canvas {canvas.CanvasFrozenPaths.Count}");
        output.WriteLine($"Selected top-level components: {canvas.Components.Count(c => c.IsSelected)}");
        output.WriteLine($"Internal paths: two pins {groups.Sum(g => g.InternalPaths.Count(p => p.StartPin != null && p.EndPin != null))}; no pins {groups.Sum(g => g.InternalPaths.Count(p => p.StartPin == null && p.EndPin == null))}");
        output.WriteLine($"Internal paths with source polygons: {groups.Sum(g => g.InternalPaths.Count(p => p.AsDrawnGeometry != null))}");
        const int width = 1600, height = 1000;
        var fit = CanvasRenderHarness.FitToView(canvas, width, height);
        var bounds = BoundingBoxCalculator.Calculate(canvas.Components)!.Value;
        var control = CanvasRenderHarness.CreateCanvas(canvas, fit, width, height);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        foreach (var factor in new[] { 1.0, 4, 2, 1, .5, .25, 1 })
        {
            var zoom = fit * factor;
            CanvasRenderHarness.CenterOn(canvas, (bounds.MinX + bounds.MaxX) / 2,
                (bounds.MinY + bounds.MaxY) / 2, zoom, width, height);
            control.Zoom = zoom;
            var samples = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                watch.Restart();
                CanvasRenderHarness.RenderInto(control, bitmap);
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            if (Environment.GetEnvironmentVariable("GDS_MAIN_CANVAS_SNAPSHOT_DIR") is { Length: > 0 } snapshotDirectory)
            {
                Directory.CreateDirectory(snapshotDirectory);
                bitmap.Save(Path.Combine(snapshotDirectory, $"main-canvas-{factor:F2}.png"));
            }
            output.WriteLine($"zoom {zoom:F5}: first {samples[0]:F2}ms; warm median {samples.Skip(1).Order().ElementAt(3):F2}ms; max {samples.Max():F2}ms");
            var rc = new CanvasRenderContext { ViewModel = canvas, InteractionState = control.InteractionState,
                Zoom = zoom, Bounds = new Rect(0, 0, width, height) };
            using var context = bitmap.CreateDrawingContext();
            using var pan = context.PushTransform(Matrix.CreateTranslation(canvas.PanX, canvas.PanY));
            using var scale = context.PushTransform(Matrix.CreateScale(zoom, zoom));
            foreach (var name in new[] { "_companionBandRenderer", "_waveguideConnectionRenderer", "_canvasFrozenPathRenderer", "_componentRenderer" })
            {
                var renderer = (ICanvasRenderer)control.GetType().GetField(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(control)!;
                watch.Restart();
                renderer.Render(context, rc);
                output.WriteLine($"  {name}: {watch.Elapsed.TotalMilliseconds:F2}ms");
                if (renderer is ComponentRenderer components)
                    output.WriteLine($"  group images {components.IssuedGroupPathImageCount}; builds {components.GroupPathBitmapBuildCount}");
            }
        }
        if (groups.Length > 0)
        {
            control.InteractionState.HoveredGroup = groups[0];
            var hoverSamples = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                watch.Restart();
                CanvasRenderHarness.RenderInto(control, bitmap);
                hoverSamples.Add(watch.Elapsed.TotalMilliseconds);
            }
            output.WriteLine($"Hovered group warm median: {hoverSamples.Skip(1).Order().ElementAt(3):F2}ms");
        }
        if (Environment.GetEnvironmentVariable("GDS_MAIN_CANVAS_UNGROUP") == "1")
        {
            control.InteractionState.HoveredGroup = null;
            foreach (var group in groups) new UngroupCommand(canvas, group).Execute();
            var samples = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                watch.Restart();
                CanvasRenderHarness.RenderInto(control, bitmap);
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            output.WriteLine($"Ungrouped: {canvas.CanvasFrozenPaths.Count} canvas paths; warm median {samples.Skip(2).Order().ElementAt(2):F2}ms");
        }
        if (Environment.GetEnvironmentVariable("GDS_MAIN_CANVAS_SELECTION") == "1")
        {
            control.InteractionState.HoveredGroup = null;
            foreach (var component in canvas.Components) component.IsSelected = true;
            foreach (var connection in canvas.Connections) connection.IsSelected = true;
            foreach (var pathVm in canvas.CanvasFrozenPaths) pathVm.IsSelected = true;
            var samples = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                control.InteractionState.HoveredComponent = canvas.Components[i % canvas.Components.Count];
                control.InteractionState.HoveredConnection = canvas.Connections.Count > 0
                    ? canvas.Connections[i % canvas.Connections.Count] : null;
                watch.Restart();
                CanvasRenderHarness.RenderInto(control, bitmap);
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            output.WriteLine($"Selected components + routes, changing hover: first {samples[0]:F2}ms; warm median {samples.Skip(2).Order().ElementAt(8):F2}ms; max {samples.Max():F2}ms");
            var frozenRenderer = (CanvasFrozenPathRenderer)control.GetType().GetField("_canvasFrozenPathRenderer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(control)!;
            output.WriteLine($"Frozen source image draws after selection: {frozenRenderer.IssuedSourceImageCount}");
            if (Environment.GetEnvironmentVariable("GDS_MAIN_CANVAS_SNAPSHOT_DIR") is { Length: > 0 } selectedSnapshotDirectory)
                bitmap.Save(Path.Combine(selectedSnapshotDirectory, "main-canvas-selected.png"));
        }
        Assert.True(report.PlacedCount > 0);
    }
}
