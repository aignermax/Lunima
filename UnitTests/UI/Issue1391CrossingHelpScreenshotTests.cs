using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1391 crossing (?) help: renders the Design Checks tab
/// with a real waveguide-crossing finding so the new (?) button is visible (en), then
/// opens the flyout and captures the two-scene animation (bare X leaking vs crossing
/// component passing straight through) mid-loop in English and German. The approach and
/// split frames must differ — proof the animation animates. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1391/</c> for PR review embedding.
/// Same pattern as <see cref="Issue1362OptimizationHelpScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1391CrossingHelpScreenshotTests
{
    private const int WindowWidth = 480;
    private const int WindowHeight = 640;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the panel with the (?) button (en) and the open flyout mid-animation (en + de).</summary>
    [AvaloniaFact]
    public async Task CaptureCrossingHelpFlyout()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1391");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            await CaptureInLanguage(SupportedLanguage.English.Code, "en", capturePanel: true, dir, manifest);
            await CaptureInLanguage(SupportedLanguage.German.Code, "de", capturePanel: false, dir, manifest);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(4);
    }

    /// <summary>Renders the Checks tab with a crossing finding and captures the flyout frames in one language.</summary>
    private static async Task CaptureInLanguage(
        string languageCode, string fileTag, bool capturePanel, string dir, List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(languageCode);

        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.RightPanel.DesignValidation.RunValidation(new[]
        {
            CreateConnection("compA", "compB", 0, 50, 100, 50),
            CreateConnection("compC", "compD", 50, 0, 50, 100),
        });
        vm.RightPanel.DesignValidation.HasWaveguideCrossingIssue.ShouldBeTrue(
            "a crossing finding must make the (?) button visible");

        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Background = new SolidColorBrush(0xFF1E1E1E),
            Content = new DesignChecksPanel { DataContext = vm },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var help = window.GetVisualDescendants()
                .OfType<HelpFlyoutButton>()
                .FirstOrDefault(b => b.Name == "CrossingHelpButton");
            help.ShouldNotBeNull("the Checks tab must carry the crossing (?) help button");
            help.IsVisible.ShouldBeTrue("the (?) button must be visible while a crossing finding exists");

            if (capturePanel)
            {
                using (Capture(window, dir, $"checks-tab-crossing-{fileTag}.png",
                    $"Design Checks tab ({fileTag}): a waveguide-crossing finding with the new (?) help button next to it.", manifest))
                {
                }
            }

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
                .OfType<WaveguideCrossingHelpAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the crossing help animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            var mid = CaptureAtProgress(window, animations, dir, $"crossing-flyout-{fileTag}-mid.png",
                $"Crossing help ({fileTag}): at the bare X the pulse splits and leaks into the crossing arm (orange); through the crossing component it continues straight at full brightness.", manifest,
                WaveguideCrossingHelpAnimation.ShowcasePhases[1]);

            if (capturePanel)
            {
                var approach = CaptureAtProgress(window, animations, dir, $"crossing-flyout-{fileTag}-approach.png",
                    $"Crossing help ({fileTag}): the pulses approach both junctions in lockstep — no leak yet.", manifest,
                    WaveguideCrossingHelpAnimation.ShowcasePhases[0]);
                using (approach)
                using (mid)
                {
                    CountDifferingPixels(approach, mid).ShouldBeGreaterThan(0,
                        "approach and split frames must differ — the animation is not animating");
                }
            }
            else
            {
                mid.Dispose();
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Scrubs all animations to one loop position and captures the frame.</summary>
    private static WriteableBitmap CaptureAtProgress(
        Window window, IEnumerable<HelpAnimationBase> animations,
        string dir, string filename, string caption, List<ManifestEntry> manifest, double progress)
    {
        foreach (var animation in animations)
            animation.Progress = progress;
        return Capture(window, dir, filename, caption, manifest);
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

    private static WaveguideConnection CreateConnection(
        string startComponentId, string endComponentId,
        double x1, double y1, double x2, double y2)
    {
        var startPin = AddPin(CreateComponent(startComponentId), "out");
        var endPin = AddPin(CreateComponent(endComponentId), "in");
        var connection = new WaveguideConnection { StartPin = startPin, EndPin = endPin };
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        connection.RestoreCachedPath(path);
        return connection;
    }

    private static Component CreateComponent(string identifier)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = identifier;
        return component;
    }

    private static PhysicalPin AddPin(Component component, string name)
    {
        var pin = new PhysicalPin
        {
            Name = name,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = component
        };
        component.PhysicalPins.Add(pin);
        return pin;
    }
}
