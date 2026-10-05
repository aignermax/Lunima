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
/// Visual documentation for the #1411 cell-instance help flyout: renders the Logic
/// tab's cell-instance help (<see cref="LogicCellInstanceHelpFlyout"/>) headless in
/// English and German and captures the animation's first frame (<c>Progress</c> = 0
/// — template only, no stamp yet), a mid frame (0.45 — the second stamp ghost is
/// sliding onto the CELL1 slot, CELL0 already placed) and the last frame (1 — the
/// register bit lit in CELL0 only, CELL1 dark: independent state). Scrubbing
/// <see cref="HelpAnimationBase.Progress"/> with <c>AutoPlay</c> off makes every
/// frame deterministic — no timer involved. The mid frame must differ from the
/// first (proof the stamp actually moves); PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1411/</c> for PR review embedding. Same pattern as
/// <see cref="Issue1220LaserHelpFlyoutScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1411CellInstanceHelpScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures first/mid/last animation frames of the flyout in en and de.</summary>
    [AvaloniaFact]
    public void CaptureCellInstanceHelpFlyoutAnimation()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1411");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            CaptureInLanguage(SupportedLanguage.English.Code, "en", dir, manifest);
            CaptureInLanguage(SupportedLanguage.German.Code, "de", dir, manifest);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        manifest.Count.ShouldBe(6);
    }

    private static void CaptureInLanguage(
        string languageCode, string fileTag, string dir, List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(languageCode);
        var caption = languageCode == SupportedLanguage.German.Code
            ? "Zellinstanz-Hilfe: eine Vorlage, zweimal gestempelt — das Bit leuchtet nur in CELL0, jede Instanz hat eigenen Zustand."
            : "Cell-instance help: one template, stamped twice — the lit bit exists only in CELL0, every instance keeps its own state.";

        var window = new Window
        {
            Width = 460,
            SizeToContent = SizeToContent.Height,
            Content = new LogicCellInstanceHelpFlyout(),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var animations = window.GetVisualDescendants().OfType<HelpAnimationBase>().ToList();
            animations.OfType<CellInstanceStampAnimation>().Count().ShouldBe(1,
                "the flyout must host the cell-instance stamp animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, 0.0);
            var first = Capture(window, dir, $"cellhelp-{fileTag}-1-first.png",
                caption + " (first frame — template only)", manifest);

            SetProgress(animations, 0.45);
            var mid = Capture(window, dir, $"cellhelp-{fileTag}-2-mid.png",
                caption + " (mid loop — the second stamp slides onto CELL1)", manifest);

            SetProgress(animations, 1.0);
            using (Capture(window, dir, $"cellhelp-{fileTag}-3-last.png",
                       caption + " (last frame — bit lit in CELL0 only)", manifest))
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

    /// <summary>Samples a grid of pixels and counts distinct ARGB values (blank-frame guard).</summary>
    private static int CountDistinctSampledColors(WriteableBitmap bitmap) =>
        SamplePixels(bitmap).ToHashSet().Count;

    /// <summary>
    /// Counts pixels that differ between two same-sized frames. Full-frame (not the
    /// sample grid): the stamp ghosts and the lit bit are only a few pixels wide, so
    /// a coarse sampling grid can miss them entirely.
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
}
