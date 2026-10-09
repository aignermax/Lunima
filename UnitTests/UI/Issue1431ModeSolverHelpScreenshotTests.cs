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
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.Views.Dialogs;
using CAP_Core.Solvers.ModeSolver;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1431 Mode Solver help flyout ("What is a mode /
/// effective index"): opens the dialog's (?) flyout headless and captures the
/// core-width confinement animation at the three showcase phases — narrow / mid /
/// wide core — in English and German. The mid frame must differ from the first
/// (proof the animation animates). Scrubbing <see cref="HelpAnimationBase.Progress"/>
/// with <c>AutoPlay</c> off makes every frame deterministic. PNGs + manifest.json
/// land in <c>docs/pr-media/issue-1431/</c> for PR review embedding (only when the
/// run opts in via <c>CAP_UPDATE_PR_MEDIA=1</c>; otherwise a temp directory is used
/// so ordinary suite runs can never re-render published PR media).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1431ModeSolverHelpScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures narrow / mid / wide frames of the open flyout in en and de.</summary>
    [AvaloniaFact]
    public async Task CaptureModeSolverHelpFlyout()
    {
        var dir = ResolveOutputDirectory();
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

        var service = new Mock<IModeSolverService>();
        var dialog = new ModeSolverDialog { DataContext = new ModeSolverViewModel(service.Object) };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var help = dialog.GetVisualDescendants().OfType<HelpFlyoutButton>().FirstOrDefault();
            help.ShouldNotBeNull("the Mode Solver dialog must carry the (?) help button");
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

            var animations = dialog.GetVisualDescendants().OfType<ModeConfinementAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the confinement animation");
            foreach (var animation in animations)
            {
                animation.AutoPlay = false;
                // The flyout caps at 460 px and scrolls: keep the capture independent
                // of how much text sits above the animation.
                animation.BringIntoView();
            }
            PumpRenderLoop();

            const string caption =
                "Mode Solver help: a waveguide cross-section whose core widens and narrows; the mode profile "
                + "tightens or spreads with it and the n_eff marker slides between the n_clad and n_core ticks.";

            var first = CaptureAtProgress(dialog, animations, dir, $"modesolver-help-{fileTag}-1-narrow.png",
                caption + " (narrow core — the mode spills into the cladding, n_eff near n_clad)", manifest,
                ModeConfinementAnimation.ShowcasePhases[0]);
            var mid = CaptureAtProgress(dialog, animations, dir, $"modesolver-help-{fileTag}-2-mid.png",
                caption + " (mid-width core)", manifest,
                ModeConfinementAnimation.ShowcasePhases[1]);
            using (CaptureAtProgress(dialog, animations, dir, $"modesolver-help-{fileTag}-3-wide.png",
                caption + " (wide core — the mode is tightly confined, n_eff near n_core)", manifest,
                ModeConfinementAnimation.ShowcasePhases[2]))
            {
            }

            using (first)
            using (mid)
            {
                if (compareMidToFirst)
                    CountDifferingPixels(first, mid).ShouldBeGreaterThan(0,
                        "mid frame must differ from the first — the animation is not animating");
            }
        }
        finally
        {
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Waits until the (?) flyout's staggered entrance has fully revealed every section.
    /// Polls the real opacity of the HelpContent panel's children (the stagger targets)
    /// instead of guessing a duration. Falls through after 15 s — the capture's
    /// blank-frame guard reports what is missing.
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

        PumpRenderLoop();
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");
        CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
            $"Near-blank render — under {MinDistinctSampledColors} distinct sampled colors in {filename}.");
        ScreenshotArtifacts.SavePng(bitmap, Path.Combine(dir, filename));
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
        if (fb.Size.Width <= 0 || fb.Size.Height <= 0) return 0;
        var colors = new HashSet<int>();
        for (int y = 0; y < fb.Size.Height; y += Math.Max(1, fb.Size.Height / SampleGridSize))
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < fb.Size.Width; x += Math.Max(1, fb.Size.Width / SampleGridSize))
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

    /// <summary>
    /// Repo-root <c>docs/pr-media/issue-1431</c> (walks up from the test output for the
    /// .sln) — but only when the run opts into refreshing published PR media via
    /// <c>CAP_UPDATE_PR_MEDIA=1</c>. Otherwise a temp directory is returned so an ordinary
    /// test run still captures and validates frames but can never overwrite media that
    /// the issue's docs embed.
    /// </summary>
    private static string ResolveOutputDirectory()
    {
        if (Environment.GetEnvironmentVariable("CAP_UPDATE_PR_MEDIA") != "1")
            return Path.Combine(Path.GetTempPath(), "cap-pr-media", "issue-1431");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "docs", "pr-media", "issue-1431");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "docs", "pr-media", "issue-1431");
    }
}
