using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1455 example <c>Logic Gate Across Two Chiplets.lun</c>:
/// renders the shipped file — loaded through the real load path — as one canvas overview
/// (<see cref="Issue1158SceneControl"/>) plus the dock Logic tab after a real
/// <c>BuildNetworkCommand</c>, with A=0 and B=1 toggled so the cross-chiplet intermediate
/// <c>NOT_A = 1</c> and the final output <c>Y = 1</c> both light up. PNGs + manifest.json
/// land in <c>docs/pr-media/issue-1455/</c> when refreshed with <c>CAP_UPDATE_PR_MEDIA=1</c>,
/// otherwise in a temp dir. Same pattern as <see cref="Issue1389Ram2x4ScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1455LogicAcrossTwoChipletsScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;
    private const int DockWindowWidth = 1200;
    private const int DockWindowHeight = 700;
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public async Task CaptureLogicAcrossTwoChipletsExample()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1455");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(),
            LogicAcrossTwoChipletsExampleAuthoringTests.ExampleFileName);
        var canvas = await LogicAcrossTwoChipletsExampleTests.LoadCanvas(path);

        var bounds = ComputeSceneBounds(canvas).Inflate(60);
        Capture(new Issue1158SceneControl(canvas, bounds), bounds, dir,
            "logic-across-two-chiplets-overview.png", canvasWidthPixels: 2000);
        manifest.Add(new
        {
            file = "logic-across-two-chiplets-overview.png",
            caption = "The shipped 'Logic Gate Across Two Chiplets' example (issue #1455): chiplet A " +
                "carries the NOT gate and an edge coupler, chiplet B the AND gate and an edge coupler, " +
                "facets abutted across the chiplet boundary — the cross-chiplet link carries the NOT " +
                "output to the AND's A input.",
        });

        await CaptureLogicPanelAfterBuild(path, dir, manifest);

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        manifest.Count.ShouldBe(2);
    }

    /// <summary>
    /// Builds the network through the real Logic panel, toggles A=0 and B=1 so the final
    /// output Y = NOT(0) AND 1 = 1 lights up alongside the intermediate NOT_A = 1, and
    /// captures the Logic tab in English.
    /// </summary>
    private static async Task CaptureLogicPanelAfterBuild(
        string path, string dir, List<object> manifest)
    {
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        try
        {
            var canvas = await LogicAcrossTwoChipletsExampleTests.LoadCanvas(path);
            var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
            var logic = vm.RightPanel.Logic;
            await logic.BuildNetworkCommand.ExecuteAsync(null);
            logic.HasNetwork.ShouldBeTrue(logic.StatusText);

            logic.Inputs.Single(i => i.PinName == "A").IsOn = false;
            logic.Inputs.Single(i => i.PinName == "B").IsOn = true;
            logic.Outputs.Single(o => o.PinName == "Y").IsOne.ShouldBeTrue(
                "Y = NOT(0) AND 1 = 1 must light up after the toggles");
            logic.Outputs.Single(o => o.PinName == "NOT_A").IsOne.ShouldBeTrue(
                "the cross-chiplet intermediate NOT_A = NOT(0) = 1 must light up");

            vm.BottomPanel.Analysis.IsVisible = true;
            vm.BottomPanel.Analysis.SetDockHeight(560);
            var dock = new AnalysisDockPanel { DataContext = vm };
            var dockWindow = new Window { Width = DockWindowWidth, Height = DockWindowHeight, Content = dock };
            dockWindow.Show();
            try
            {
                vm.BottomPanel.Analysis.OpenLogic();
                Dispatcher.UIThread.RunJobs();

                CaptureWithRetry(dockWindow, Path.Combine(dir, "logic-across-two-chiplets-logic-panel-en.png"));
                manifest.Add(new
                {
                    file = "logic-across-two-chiplets-logic-panel-en.png",
                    caption = "The Logic tab (en) of 'Logic Gate Across Two Chiplets' after Build: " +
                        "named toggles A=0 and B=1, the cross-chiplet intermediate NOT_A = 1 and the " +
                        "final output Y = 1 — the truth table of NOT(A) AND B evaluated across the link.",
                });
            }
            finally
            {
                dockWindow.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    private static Rect ComputeSceneBounds(DesignCanvasViewModel canvas)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var comp in canvas.Components)
        {
            minX = Math.Min(minX, comp.X);
            minY = Math.Min(minY, comp.Y);
            maxX = Math.Max(maxX, comp.X + comp.Width);
            maxY = Math.Max(maxY, comp.Y + comp.Height);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static void Capture(Issue1158SceneControl scene, Rect world, string outputDir,
        string filename, int canvasWidthPixels)
    {
        scene.Width = canvasWidthPixels;
        scene.Height = canvasWidthPixels * world.Height / world.Width;
        var window = new Window
        {
            Width = scene.Width,
            Height = scene.Height,
            Content = scene,
            Background = Brushes.Black,
        };
        window.Show();
        try
        {
            WriteableBitmap? bitmap = null;
            for (var attempt = 0; attempt < CaptureAttempts && bitmap == null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                bitmap = window.CaptureRenderedFrame();
            }
            bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts for {filename}");
            using (bitmap)
            {
                CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                    $"Near-blank render for {filename} — likely a missing Skia setup.");
                ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(outputDir, filename));
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the window, pumping the dispatcher and keeping the last good frame.</summary>
    private static void CaptureWithRetry(Window window, string path)
    {
        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts for {path}");
        using (bitmap)
        {
            ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }

    private static int CountDistinctSampledColors(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        if (width <= 0 || height <= 0) return 0;

        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        var colors = new HashSet<int>();
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                colors.Add(System.Runtime.InteropServices.Marshal.ReadInt32(rowAddr, x * 4));
        }
        return colors.Count;
    }
}
