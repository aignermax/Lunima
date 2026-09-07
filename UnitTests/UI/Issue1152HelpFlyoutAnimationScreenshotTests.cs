using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1152 help-flyout rewrite: renders each rewritten flyout
/// headless and captures its animation's first frame (<c>Progress</c> = 0), a mid frame
/// (0.5) and the last frame (1). Scrubbing <see cref="HelpAnimationBase.Progress"/> with
/// <c>AutoPlay</c> off makes every frame deterministic — no timer involved. The mid frame
/// must differ from the first (proof the animation actually animates); PNGs + manifest.json
/// land in <c>artifacts/ui-screenshots/issue-1152/</c> (or <c>UI_SHOT_DIR/issue-1152</c>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1152HelpFlyoutAnimationScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures first/mid/last animation frames for all five rewritten flyouts.</summary>
    [AvaloniaFact]
    public void CaptureRewrittenHelpFlyoutAnimations()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        CaptureFlyout(dir, manifest, "transient", 460, 520, () => new TransientHelpFlyout(),
            "Transient help: the light pulse leaves the laser, enters the circuit and splits to the outputs.");
        CaptureFlyout(dir, manifest, "eye", 460, 420, () => new EyeHelpFlyout(),
            "Eye/BER help: the four bit trajectories stack into the eye, then the dashed eye-opening marker appears.");
        CaptureFlyout(dir, manifest, "process", 460, 560, () => new ProcessHelpFlyout(),
            "Process help: substrate, core, cladding and metal layers rise into the finished chip stack.");
        CaptureFlyout(dir, manifest, "lineshape", 340, 260, () => new LineShapeHelpFlyout(),
            "Line-shape help: the marker sweeps the Lorentzian peak — power spreads around the center wavelength.");
        CaptureFlyout(dir, manifest, "rin", 340, 280, () => new RinHelpFlyout(),
            "RIN help: the marker runs the noisy power-over-time trace that relative intensity noise describes.");

        ScreenshotArtifacts.WriteText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        manifest.Count.ShouldBe(15);
    }

    /// <summary>Renders one flyout and captures the animation at Progress 0, 0.5 and 1.</summary>
    private static void CaptureFlyout(
        string dir, List<ManifestEntry> manifest, string name, int width, int height,
        Func<Control> createFlyout, string caption)
    {
        var window = new Window { Width = width, Height = height, Content = createFlyout() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var animations = window.GetVisualDescendants().OfType<HelpAnimationBase>().ToList();
            animations.ShouldNotBeEmpty($"{name} flyout must contain a HelpAnimationBase animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, 0.0);
            var first = Capture(window, dir, $"{name}-1-first.png", caption + " (first frame)", manifest);

            SetProgress(animations, 0.5);
            var mid = Capture(window, dir, $"{name}-2-mid.png", caption + " (mid loop)", manifest);

            SetProgress(animations, 1.0);
            using (Capture(window, dir, $"{name}-3-last.png", caption + " (last frame — the loop's end state stays visible)", manifest))
            {
            }

            using (first)
            using (mid)
            {
                CountDifferingSampledPixels(first, mid).ShouldBeGreaterThan(0,
                    $"{name}: mid-loop frame must differ from the first frame — the animation is not animating");
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

    /// <summary>Counts grid-sampled pixels that differ between two same-sized frames.</summary>
    private static int CountDifferingSampledPixels(WriteableBitmap a, WriteableBitmap b)
    {
        var pa = SamplePixels(a);
        var pb = SamplePixels(b);
        pa.Count.ShouldBe(pb.Count, "frames of one flyout must have the same size");
        return pa.Zip(pb).Count(pair => pair.First != pair.Second);
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
            return Path.Combine(envDir, "issue-1152");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1152");
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1152");
    }
}
