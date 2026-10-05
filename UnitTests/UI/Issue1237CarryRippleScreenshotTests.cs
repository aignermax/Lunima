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
using CAP.Avalonia.Views;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1237 "Why light adds" help section: renders the ISA
/// playground help (<see cref="IsaPlaygroundHelpFlyout"/>) headless and captures its
/// carry-ripple animation's first frame (<c>Progress</c> = 0 — pulse still off-canvas,
/// all full adders dark, sum 0000, time bar empty), a mid frame (0.58 — the carry has
/// reached FA2: the box is lit, the time bar has grown with the first two hops) and
/// the last frame (1 — carry rippled through FA0…FA3, sum flipped to 1000, FA3 stays
/// lit, time bar full). Scrubbing <see cref="HelpAnimationBase.Progress"/> with
/// <c>AutoPlay</c> off makes every frame deterministic — no timer involved. The mid
/// frame must differ from the first (proof the carry actually moves); PNGs +
/// manifest.json land in <c>docs/pr-media/issue-1237/</c> for PR review embedding.
/// Same pattern as <see cref="Issue1229PdkHelpFlyoutScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1237CarryRippleScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures first/mid/last animation frames of the new help section.</summary>
    [AvaloniaFact]
    public void CaptureCarryRippleAnimation()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        const string caption =
            "ISA playground help, \"Why light adds\": for 0111 + 0001 a light pulse ripples the carry through the four full adders FA0–FA3; each hop is more light path, so the time bar grows — that is the picosecond number in the status line.";

        // Pin the locale: the localization singleton is process-global, so a
        // previously run test may have left another language active — and text
        // length (hence the needed window height) depends on it.
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var window = new Window
        {
            Width = 460,
            SizeToContent = SizeToContent.Height,
            Content = new IsaPlaygroundHelpFlyout(),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var animations = window.GetVisualDescendants().OfType<HelpAnimationBase>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain a HelpAnimationBase animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, 0.0);
            var first = Capture(window, dir, "carry-ripple-1-first.png", caption + " (first frame — pulse not yet entered, sum 0000)", manifest);

            SetProgress(animations, 0.58);
            var mid = Capture(window, dir, "carry-ripple-2-mid.png", caption + " (mid loop — the carry is inside FA2)", manifest);

            SetProgress(animations, 1.0);
            using (Capture(window, dir, "carry-ripple-3-last.png", caption + " (last frame — sum flipped to 1000, time bar full)", manifest))
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
    /// sample grid): the pulse and the box glows are only a few pixels wide, so a
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

    /// <summary>Repo-root <c>docs/pr-media/issue-1237</c> — only with <c>CAP_UPDATE_PR_MEDIA=1</c>; otherwise a temp dir.</summary>
    private static string ResolveOutputDirectory() =>
        ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1237");
}
