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
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1256 Length Matching help flyout: opens the panel's (?)
/// flyout headless (the panel only shows while exactly one connection is selected, so the
/// test selects one) and captures its pulses-arrive-together animation at the three
/// <see cref="LengthMatchArrivalAnimation.ShowcasePhases"/> — pulses in flight on
/// mismatched arms, the phase-slip moment (short arm's pulse arrived, the long arm's is
/// still travelling) and the matched arrival (Δt = 0). The readout uses the real
/// group-delay formula (pinned in <see cref="LengthMatchArrivalAnimationTests"/>). The
/// phase-slip frame must differ from the in-flight frame (proof the pulses move); PNGs +
/// manifest.json land in <c>docs/pr-media/issue-1256/</c> for PR review embedding.
/// Same pattern as <see cref="Issue1247ChipletLinkHelpScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1256LengthMatchHelpScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures in-flight / phase-slip / matched frames of the flyout animation.</summary>
    [AvaloniaFact]
    public async Task CaptureLengthMatchArrivalAnimation()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        const string caption =
            "Length Matching help: two pulses leave a splitter together — unequal arm lengths make them arrive out of step (phase slip), the grown meander matches the lengths and they arrive together; the readout is the real group delay Δt = ΔL·n_g/c.";

        // Pin the locale: the localization singleton is process-global, so a
        // previously run test may have left another language active — and text
        // length (hence the needed window height) depends on it.
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var canvas = new DesignCanvasViewModel();
        var connection = AddStraightConnection(canvas);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.BottomPanel.LengthMatching.SelectedConnection = connection;
        vm.BottomPanel.LengthMatching.HasExactlyOneConnection
            .ShouldBeTrue("the flyout lives on the one-connection panel section");

        var panel = new LengthMatchingPanel { DataContext = vm };
        var window = new Window { Width = 520, Height = 700, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var help = panel.GetVisualDescendants().OfType<HelpFlyoutButton>().FirstOrDefault();
            help.ShouldNotBeNull("the Length Matching panel must carry the (?) help button");
            var innerButton = help.GetVisualDescendants().OfType<Button>().First();
            innerButton.Flyout.ShouldNotBeNull("the help button must host a flyout");
            innerButton.Flyout!.ShowAt(innerButton);
            Dispatcher.UIThread.RunJobs();

            // The HelpFlyoutButton staggers its sections in (Task.Delay + opacity transitions):
            // give the delays real time, then tick the headless render clock so they finish.
            await Task.Delay(600);
            for (int i = 0; i < 30; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10);
            }
            Dispatcher.UIThread.RunJobs();

            var animations = window.GetVisualDescendants()
                .OfType<LengthMatchArrivalAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the pulses-arrive-together animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, LengthMatchArrivalAnimation.ShowcasePhases[0]);
            var first = Capture(window, dir, "length-match-1-pulses-in-flight.png",
                caption + " (mismatched arms — both pulses in flight)", manifest);

            SetProgress(animations, LengthMatchArrivalAnimation.ShowcasePhases[1]);
            var mid = Capture(window, dir, "length-match-2-phase-slip.png",
                caption + " (phase slip — the short arm's pulse has arrived, the long arm's is still travelling)", manifest);

            SetProgress(animations, LengthMatchArrivalAnimation.ShowcasePhases[2]);
            using (Capture(window, dir, "length-match-3-matched-arrival.png",
                caption + " (length matched — the meander brings both pulses in together, Δt = 0)", manifest))
            {
            }

            using (first)
            using (mid)
            {
                CountDifferingPixels(first, mid).ShouldBeGreaterThan(0,
                    "phase-slip frame must differ from the in-flight frame — the animation is not animating");
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(3);
    }

    /// <summary>Adds two facing straight waveguides and connects them with a straight route.</summary>
    private static WaveguideConnectionViewModel AddStraightConnection(DesignCanvasViewModel canvas)
    {
        var startComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        startComp.WidthMicrometers = startComp.HeightMicrometers = 250;
        var endComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        endComp.WidthMicrometers = endComp.HeightMicrometers = 250;
        endComp.PhysicalX = 400;
        canvas.AddComponent(startComp);
        canvas.AddComponent(endComp);

        var startPin = startComp.PhysicalPins.First(p => p.Name == "out");
        var endPin = endComp.PhysicalPins.First(p => p.Name == "in");
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
        var connection = canvas.ConnectPinsWithCachedRoute(startPin, endPin, path);
        connection.ShouldNotBeNull("the straight route between facing pins must connect");
        return connection!;
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

    /// <summary>Samples a grid of pixels and counts distinct ARGB values (blank-frame guard).</summary>
    private static int CountDistinctSampledColors(WriteableBitmap bitmap) =>
        SamplePixels(bitmap).ToHashSet().Count;

    /// <summary>Counts pixels that differ between two same-sized frames. Full-frame, not the
    /// sample grid: the pulses and flashes are only a few pixels wide, a grid can miss them.</summary>
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

    /// <summary>Reads a deterministic grid of ARGB pixels in scan order.</summary>
    private static List<int> SamplePixels(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        var pixels = new List<int>();
        if (width <= 0 || height <= 0)
            return pixels;

        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                pixels.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }
        return pixels;
    }

    /// <summary>Repo-root <c>docs/pr-media/issue-1256</c> — only with <c>CAP_UPDATE_PR_MEDIA=1</c>; otherwise a temp dir.</summary>
    private static string ResolveOutputDirectory() =>
        ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1256");
}
