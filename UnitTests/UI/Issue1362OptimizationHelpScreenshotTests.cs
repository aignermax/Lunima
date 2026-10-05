using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1362 Optimization (?) help: renders the real
/// Optimization tab in the analysis dock with its new (?) button (en, a slider
/// component on the canvas so the panel is visible), then opens the flyout and
/// captures the hill-climb animation mid-climb and at the reached local best in
/// English and German (the two frames of a pair must differ — proof the animation
/// animates). PNGs + manifest.json land in <c>docs/pr-media/issue-1362/</c> for PR
/// review embedding.
/// Same pattern as <see cref="Issue1352SweepHelpScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1362OptimizationHelpScreenshotTests
{
    private const int WindowWidth = 980;
    private const int WindowHeight = 700;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int OptimizationTabIndex = 4;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the tab with the (?) button (en) and the open flyout at two phases (en + de).</summary>
    [AvaloniaFact]
    public async Task CaptureOptimizationHelpFlyout()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1362");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            await CaptureInLanguage(SupportedLanguage.English.Code, "en", captureTab: true, dir, manifest);
            await CaptureInLanguage(SupportedLanguage.German.Code, "de", captureTab: false, dir, manifest);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(5);
    }

    /// <summary>Captures the open flyout at two animation phases in one language (plus the tab itself in en).</summary>
    private static async Task CaptureInLanguage(
        string languageCode, string fileTag, bool captureTab, string dir, List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(languageCode);

        var vm = MainViewModelTestHelper.CreateMainViewModel();
        // The Optimization panel only renders when a component with a tunable slider
        // sits on the canvas (HasParameters) — add one.
        var component = TestComponentHelper.CreateComponentWithSlider(0, 100, 50);
        vm.Canvas.AddComponent(component, "PhaseShifter1");
        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SelectedTabIndex = OptimizationTabIndex;
        vm.BottomPanel.Analysis.DockHeight = 480;

        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Content = new AnalysisDockPanel { DataContext = vm },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            if (captureTab)
            {
                using (Capture(window, dir, $"optimization-tab-{fileTag}.png",
                    $"Optimization tab ({fileTag}): the new (?) help button sits right of the Run button in the config column.", manifest))
                {
                }
            }

            var optimizationPanel = window.GetVisualDescendants()
                .OfType<CircuitOptimizationPanel>().FirstOrDefault();
            optimizationPanel.ShouldNotBeNull("the Optimization tab must be rendered");
            var help = optimizationPanel!.GetVisualDescendants().OfType<HelpFlyoutButton>().FirstOrDefault();
            help.ShouldNotBeNull("the Optimization tab must carry the (?) help button");
            var innerButton = help.GetVisualDescendants().OfType<Button>().First();
            innerButton.Flyout.ShouldNotBeNull("the help button must host a flyout");
            innerButton.Flyout!.ShowAt(innerButton);
            Dispatcher.UIThread.RunJobs();

            // The HelpFlyoutButton staggers its sections in (Task.Delay + opacity
            // transitions): give the delays real time, then tick the render clock.
            await Task.Delay(600);
            for (int i = 0; i < 30; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10);
            }
            Dispatcher.UIThread.RunJobs();

            var animations = window.GetVisualDescendants()
                .OfType<HillClimbAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the hill-climb animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, HillClimbAnimation.ShowcasePhases[0]); // mid-climb, wide step
            var climbing = Capture(window, dir, $"flyout-{fileTag}-climbing.png",
                $"Optimization help ({fileTag}): the marker travels uphill on the objective curve — the step bracket under the curve is still wide.", manifest);

            SetProgress(animations, HillClimbAnimation.ShowcasePhases[1]); // local best reached, step shrunk
            using var localBest = Capture(window, dir, $"flyout-{fileTag}-local-best.png",
                $"Optimization help ({fileTag}): the climb rests on the local best (green) with a shrunk step after rejected probes — the taller unvisited peak shows the local-best caveat.", manifest);

            using (climbing)
            {
                CountDifferingPixels(climbing, localBest).ShouldBeGreaterThan(0,
                    "climbing and local-best frames must differ — the animation is not animating");
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void SetProgress(IEnumerable<HelpAnimationBase> animations, double progress)
    {
        foreach (var animation in animations)
            animation.Progress = progress;
        PumpRenderLoop();
    }

    /// <summary>Captures the window to a PNG, fails on a near-blank frame, records the caption.</summary>
    private static WriteableBitmap Capture(
        Window window, string dir, string filename, string caption, List<ManifestEntry> manifest)
    {
        PumpRenderLoop();
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");

        var path = Path.Combine(dir, filename);
        CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
            $"Near-blank render — under {MinDistinctSampledColors} distinct sampled colors in {filename}.");
        ScreenshotArtifacts.SavePng(bitmap, path);
        manifest.Add(new ManifestEntry(filename, caption));
        return bitmap;
    }

    /// <summary>Advances the headless render timer so property changes actually paint before capture.</summary>
    private static void PumpRenderLoop()
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
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
                colors.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }
        return colors.Count;
    }

    private static int CountDifferingPixels(WriteableBitmap a, WriteableBitmap b)
    {
        using var fa = a.Lock();
        using var fb = b.Lock();
        fa.Size.ShouldBe(fb.Size, "frames of one flyout must have the same size");

        int differing = 0;
        for (int y = 0; y < fa.Size.Height; y++)
        {
            var rowA = fa.Address + y * fa.RowBytes;
            var rowB = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < fa.Size.Width; x++)
                if (Marshal.ReadInt32(rowA, x * 4) != Marshal.ReadInt32(rowB, x * 4))
                    differing++;
        }
        return differing;
    }
}
