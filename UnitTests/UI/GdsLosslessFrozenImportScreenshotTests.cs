using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.Views.Dialogs;
using Shouldly;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual walkthrough of the lossless frozen GDS import on the synthetic Nazca-style
/// chip (<see cref="NazcaStyleChipFixture"/>): the dialog defaults, the imported chip
/// drawn exactly as the file has it, the selected waveguide's fitted centerline, and the
/// same waveguide after "re-route selected". Opt-in (<c>UI_SHOT_DIR</c>); writes PNGs and
/// a manifest to <c>UI_SHOT_DIR/gds-lossless-frozen/</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public sealed class GdsLosslessFrozenImportScreenshotTests : IDisposable
{
    private const int PanelWidthPx = 1400;
    private const double MarginUm = 15;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lunima-lossless-shot-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [AvaloniaFact]
    public async Task CaptureLosslessFrozenImportWalkthrough()
    {
        var shotRoot = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(shotRoot))
            return; // opt-in: heavy headless render, only on explicit request
        var outputDir = Path.Combine(shotRoot, "gds-lossless-frozen");
        Directory.CreateDirectory(outputDir);

        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, 1000, 1000);
        var executor = new GdsPlacementExecutor(canvas, new CommandManager(), () => _host.Templates.ToList());
        var vm = new GdsImportDialogViewModel(NazcaStyleChipFixture.WriteTo(_root), _host.CreateService(), executor);
        await vm.StartAnalysisAsync();

        var dialog = new GdsImportDialog { DataContext = vm };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        CaptureWindow(dialog, Path.Combine(outputDir, "01-dialog-defaults.png"));

        await vm.ImportCommand.ExecuteAsync(null);
        vm.HasError.ShouldBeFalse(vm.ErrorText);
        var world = ContentWindow(canvas);
        CaptureCanvas(canvas, world, Path.Combine(outputDir, "02-imported-as-drawn.png"));

        var connection = canvas.Connections.Single();
        connection.IsSelected = true;
        CaptureCanvas(canvas, world, Path.Combine(outputDir, "03-selected-centerline.png"));

        var reroute = new RerouteImportedRoutesCommand(canvas, new[] { connection });
        reroute.Execute();
        await canvas.RecalculateRoutesAsync();
        connection.IsSelected = false;
        CaptureCanvas(canvas, world, Path.Combine(outputDir, "04-rerouted-selected-waveguide.png"));

        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), """
        [
          {"file": "01-dialog-defaults.png", "caption": "Import dialog defaults: routes stay as drawn (re-routing off) and the import stays flat (grouping off)."},
          {"file": "02-imported-as-drawn.png", "caption": "The imported chip renders its original polygons — 2 µm core and 6 µm cladding ribbons, the mirrored device on the right."},
          {"file": "03-selected-centerline.png", "caption": "Selecting the waveguide shows its fitted centerline (straights + two R = 40 µm bends) over the drawing; this is what simulation and A* use."},
          {"file": "04-rerouted-selected-waveguide.png", "caption": "After 'Re-route selected' only this waveguide is routed by Lunima; the drawing disappears for it (Ctrl+Z restores it)."}
        ]
        """);
    }

    private static Rect ContentWindow(DesignCanvasViewModel canvas)
    {
        var comps = canvas.Components.Select(c => c.Component).ToList();
        var drawn = canvas.Connections.Select(c => c.Connection.AsDrawnGeometry).OfType<CAP_Core.Components.Connections.AsDrawnGeometry>().ToList();
        double minX = Math.Min(comps.Min(c => c.PhysicalX), drawn.Min(d => d.Bounds.MinX));
        double minY = Math.Min(comps.Min(c => c.PhysicalY), drawn.Min(d => d.Bounds.MinY));
        double maxX = Math.Max(comps.Max(c => c.PhysicalX + c.WidthMicrometers), drawn.Max(d => d.Bounds.MaxX));
        double maxY = Math.Max(comps.Max(c => c.PhysicalY + c.HeightMicrometers), drawn.Max(d => d.Bounds.MaxY));
        return new Rect(minX - MarginUm, minY - MarginUm, maxX - minX + 2 * MarginUm, maxY - minY + 2 * MarginUm);
    }

    private static void CaptureCanvas(DesignCanvasViewModel canvas, Rect world, string path)
    {
        int height = (int)Math.Ceiling(PanelWidthPx * world.Height / world.Width);
        var scene = new CanvasLabelDeclutterSceneControl(canvas, new CanvasInteractionState(), world)
        {
            Width = PanelWidthPx,
            Height = height,
        };
        var window = new Window { Width = PanelWidthPx, Height = height, Content = scene, Background = Brushes.Black };
        window.Show();
        CaptureWindow(window, path);
    }

    private static void CaptureWindow(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        var bitmap = window.CaptureRenderedFrame();
        window.Close();
        Dispatcher.UIThread.RunJobs();
        bitmap.ShouldNotBeNull($"render miss for {Path.GetFileName(path)}");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap!, path).Length.ShouldBeGreaterThan(0);
    }
}
