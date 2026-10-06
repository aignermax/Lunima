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
/// Visual documentation for the #1421 "Blocked wires" section of the Design Checks help:
/// opens the panel's (?) flyout headless and captures the two-vignette animation
/// (sealed pin → move the component; no free lane → make room) at the three showcase
/// phases — approach, fix-in-progress, freed — in English and German. The mid frame must
/// differ from the first (proof the animation animates). Scrubbing
/// <see cref="HelpAnimationBase.Progress"/> with <c>AutoPlay</c> off makes every frame
/// deterministic. PNGs + manifest.json land in <c>docs/pr-media/issue-1421/</c> for PR
/// review embedding. Same pattern as <see cref="Issue1247ChipletLinkHelpScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1421BlockedWireHelpScreenshotTests
{
    private const int WindowWidth = 480;
    private const int WindowHeight = 760;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures approach / fix / freed frames of the flyout section in en and de.</summary>
    [AvaloniaFact]
    public async Task CaptureBlockedWireHelpFlyout()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1421");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            await CaptureInLanguage(SupportedLanguage.English.Code, "en", compareMidToFirst: true, dir, manifest);
            await CaptureInLanguage(SupportedLanguage.German.Code, "de", compareMidToFirst: false, dir, manifest);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(6);
    }

    /// <summary>Opens the (?) flyout in one language and captures the three showcase frames.</summary>
    private static async Task CaptureInLanguage(
        string languageCode, string fileTag, bool compareMidToFirst, string dir, List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(languageCode);

        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var panel = new DesignChecksPanel { DataContext = vm };
        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Background = new Avalonia.Media.SolidColorBrush(0xFF1E1E1E),
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
            // transitions): wait until the entrance has actually finished instead of
            // guessing a duration — the reveal is real-time, so a fixed wait flakes
            // under CI load or when a section is added to the flyout.
            await WaitForFlyoutRevealedAsync(help);
            Dispatcher.UIThread.RunJobs();

            var animations = window.GetVisualDescendants()
                .OfType<BlockedWireHelpAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the Blocked wires animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            // The flyout caps at 460 px and scrolls: make the captures independent of
            // how many sections sit above "Blocked wires" by scrolling it into view.
            foreach (var animation in animations)
                animation.BringIntoView();
            PumpRenderLoop();

            const string caption =
                "Design Checks help, \"Blocked wires\": left, a pulse sealed in by a component footprint stalls and turns red until the component slides away; right, a corridor full of parallel waveguides shows the dashed red blocked fallback until the neighbours spread apart and a lane opens.";

            var first = CaptureAtProgress(window, animations, dir, $"blocked-wires-{fileTag}-1-approach.png",
                caption + " (approach — both pulses still on their way)", manifest,
                BlockedWireHelpAnimation.ShowcasePhases[0]);

            var mid = CaptureAtProgress(window, animations, dir, $"blocked-wires-{fileTag}-2-fix.png",
                caption + " (fix-in-progress — the component slides away, the neighbours spread)", manifest,
                BlockedWireHelpAnimation.ShowcasePhases[1]);

            using (CaptureAtProgress(window, animations, dir, $"blocked-wires-{fileTag}-3-freed.png",
                caption + " (freed — fresh pulses cross both routes end to end)", manifest,
                BlockedWireHelpAnimation.ShowcasePhases[2]))
            {
            }

            if (compareMidToFirst)
            {
                using (first)
                using (mid)
                {
                    CountDifferingPixels(first, mid).ShouldBeGreaterThan(0,
                        "mid frame must differ from the first — the animation is not animating");
                }
            }
            else
            {
                first.Dispose();
                mid.Dispose();
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Waits until the (?) flyout's staggered entrance has fully revealed every section.
    /// Polls the real opacity of the HelpContent panel's children (the stagger targets):
    /// first until the entrance has started hiding them, then until all are opaque again.
    /// Falls through after 15 s — the capture's blank-frame guard reports what is missing.
    /// </summary>
    private static async Task WaitForFlyoutRevealedAsync(HelpFlyoutButton help)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var entranceStarted = false;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            if (help.HelpContent is Panel panel)
            {
                var children = panel.Children.OfType<Control>().ToList();
                if (children.Count > 0 && children.All(c => c.Opacity >= 0.999))
                {
                    if (entranceStarted)
                        return;
                }
                else
                {
                    entranceStarted = true;
                }
            }
            await Task.Delay(20);
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
}
