using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1216 help-flyout migration: renders the Logic tab's
/// fan-out help (<see cref="LogicFanOutHelpFlyout"/>) headless and captures its
/// power-splitting animation's first frame (<c>Progress</c> = 0 — the bright pulse
/// leaves the driving gate, all badges idle), a mid frame (0.5 — the ½-power branch
/// arrived: its meter sits above the dashed threshold and its badge reads 1, the two
/// ¼-power pulses are still in flight) and the last frame (1 — all branches arrived,
/// the ¼-power meters fall below the threshold and their badges read "?"). Scrubbing
/// <see cref="HelpAnimationBase.Progress"/> with <c>AutoPlay</c> off makes every frame
/// deterministic — no timer involved. The mid frame must differ from the first (proof
/// the pulses/meters/badges actually move); PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1216/</c> (or <c>UI_SHOT_DIR/issue-1216</c>).
/// Same pattern as <see cref="Issue1206TimelineHelpFlyoutScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1216FanOutHelpFlyoutScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures first/mid/last animation frames of the migrated fan-out flyout.</summary>
    [AvaloniaFact]
    public void CaptureFanOutHelpFlyoutAnimation()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        const string caption =
            "Logic fan-out help: every split divides the power — the ½ branch stays above the threshold (badge 1), the ¼ branches fall below it (badge ?).";

        // Pin the locale: the localization singleton is process-global, so a
        // previously run test may have left another language active — and text
        // length (hence the needed window height) depends on it.
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var window = new Window
        {
            Width = 460,
            SizeToContent = SizeToContent.Height,
            Content = new LogicFanOutHelpFlyout(),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var animations = window.GetVisualDescendants().OfType<HelpAnimationBase>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain a HelpAnimationBase animation");
            animations.OfType<PowerSplittingAnimation>().Count().ShouldBe(1,
                "the flyout must host the power-splitting animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, 0.0);
            var first = Capture(window, dir, "fanouthelp-1-first.png", caption + " (first frame)", manifest);

            SetProgress(animations, 0.5);
            var mid = Capture(window, dir, "fanouthelp-2-mid.png", caption + " (mid loop — the ½ branch arrived)", manifest);

            SetProgress(animations, 1.0);
            using (Capture(window, dir, "fanouthelp-3-last.png", caption + " (last frame — the ¼ branches lost the bit)", manifest))
            {
            }

            using (first)
            using (mid)
            {
                CountDifferingPixels(first, mid).ShouldBeGreaterThan(0,
                    "mid-loop frame must differ from the first frame — the animation is not animating");
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
    /// sample grid): the meter fills and badge glyphs are only a few pixels wide, so a
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

    /// <summary>Repo-root walkthrough output directory (env override: <c>UI_SHOT_DIR</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1216");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1216");
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1216");
    }
}
