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
/// Visual documentation for the #1247 "Chiplet links" section of the Design Checks help:
/// opens the panel's (?) flyout headless and captures its Gaussian-beam animation at the
/// three showcase phases — butt-coupled (<c>Progress</c> = 0: facets touch, η = 100 %),
/// facet gap (0.5: the beam has widened across the grown gap, facet B still on axis) and
/// gap + lateral offset (1: facet B additionally shifted sideways, lowest η). The readout
/// is computed with the real core coupling functions (pinned in
/// <see cref="ChipletLinkCouplingAnimationTests"/>), never hard-coded. Scrubbing
/// <see cref="HelpAnimationBase.Progress"/> with <c>AutoPlay</c> off makes every frame
/// deterministic. The gap frame must differ from the first (proof the gap actually grows);
/// PNGs + manifest.json land in <c>docs/pr-media/issue-1247/</c> for PR review embedding.
/// Same pattern as <see cref="Issue1237CarryRippleScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1247ChipletLinkHelpScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures butt-coupled / gap / gap+offset frames of the flyout section.</summary>
    [AvaloniaFact]
    public async Task CaptureChipletLinkAnimation()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        const string caption =
            "Design Checks help, \"Chiplet links\": light leaves facet A as a widening Gaussian beam — a facet gap lets it diverge, a lateral offset lets it miss facet B's mode; the readout is the real simulated coupling η at 1550 nm.";

        // Pin the locale: the localization singleton is process-global, so a
        // previously run test may have left another language active — and text
        // length (hence the needed window height) depends on it.
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var panel = new DesignChecksPanel { DataContext = vm };
        var window = new Window
        {
            Width = 520,
            Height = 700,
            Content = panel,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var help = panel.GetVisualDescendants().OfType<HelpFlyoutButton>().FirstOrDefault();
            help.ShouldNotBeNull("the Design Checks panel must carry the (?) help button");
            var innerButton = help.GetVisualDescendants().OfType<Button>().First();
            innerButton.Flyout.ShouldNotBeNull("the help button must host a flyout");
            innerButton.Flyout!.ShowAt(innerButton);
            Dispatcher.UIThread.RunJobs();

            // The HelpFlyoutButton staggers its sections in (Task.Delay + opacity
            // transitions): give the delays real time, then advance the headless render
            // clock so the transitions finish — otherwise later sections stay at opacity 0.
            await Task.Delay(600);
            for (int i = 0; i < 30; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10);
            }
            Dispatcher.UIThread.RunJobs();

            var animations = window.GetVisualDescendants()
                .OfType<ChipletLinkCouplingAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the Chiplet links animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, ChipletLinkCouplingAnimation.ShowcasePhases[0]);
            var first = Capture(window, dir, "chiplet-link-1-butt-coupled.png",
                caption + " (butt-coupled — facets touch, η = 100 %)", manifest);

            SetProgress(animations, ChipletLinkCouplingAnimation.ShowcasePhases[1]);
            var mid = Capture(window, dir, "chiplet-link-2-facet-gap.png",
                caption + " (facet gap — the beam has diverged across the gap)", manifest);

            SetProgress(animations, ChipletLinkCouplingAnimation.ShowcasePhases[2]);
            using (Capture(window, dir, "chiplet-link-3-gap-and-offset.png",
                caption + " (gap + lateral offset — facet B is shifted sideways, lowest η)", manifest))
            {
            }

            using (first)
            using (mid)
            {
                CountDifferingPixels(first, mid).ShouldBeGreaterThan(0,
                    "gap frame must differ from the butt-coupled frame — the animation is not animating");
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        manifest.Count.ShouldBe(3);
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

    /// <summary>
    /// Counts pixels that differ between two same-sized frames. Full-frame (not the
    /// sample grid): the beam and the spillover spot are only a few pixels wide, so a
    /// coarse sampling grid can miss them entirely.
    /// </summary>
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
            {
                if (Marshal.ReadInt32(rowA, x * 4) != Marshal.ReadInt32(rowB, x * 4))
                    differing++;
            }
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

    /// <summary>Repo-root <c>docs/pr-media/issue-1247</c> — only with <c>CAP_UPDATE_PR_MEDIA=1</c>; otherwise a temp dir.</summary>
    private static string ResolveOutputDirectory() =>
        ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1247");
}
