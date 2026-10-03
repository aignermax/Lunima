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
/// Visual documentation for the #1333 coherent-interference mode: renders the real
/// Spectrum tab in the analysis dock with the new "Coherent interference" toggle in
/// English and German, then opens the toggle's (?) flyout and captures the
/// ΔL→fringes animation at a bright peak and a deep null (the two frames must
/// differ — proof the animation animates). PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1333/</c> for PR review embedding.
/// Same pattern as <see cref="Issue1256LengthMatchHelpScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1333CoherentSpectrumScreenshotTests
{
    private const int WindowWidth = 980;
    private const int WindowHeight = 700;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int SpectrumTabIndex = 2;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the tab with the toggle (en + de) and the open flyout at two phases.</summary>
    [AvaloniaFact]
    public async Task CaptureCoherentSpectrumToggleAndFlyout()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1333");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            await CaptureInLanguage(SupportedLanguage.English.Code, "en", dir, manifest);
            await CaptureInLanguage(SupportedLanguage.German.Code, "de", dir, manifest);
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

    /// <summary>Captures the toggle row and the open flyout (peak + null) in one language.</summary>
    private static async Task CaptureInLanguage(
        string languageCode, string fileTag, string dir, List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(languageCode);

        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SelectedTabIndex = SpectrumTabIndex;
        vm.BottomPanel.Analysis.DockHeight = 480;
        vm.BottomPanel.Analysis.Spectrum.IsCoherentInterference = true;

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
            using (Capture(window, dir, $"spectrum-tab-{fileTag}.png",
                $"Wavelength Spectrum tab ({fileTag}): the \"Coherent interference (propagation phase)\" toggle with its (?) help below the sweep parameters.", manifest))
            {
            }

            var spectrumPanel = window.GetVisualDescendants()
                .OfType<WavelengthSpectrumPanel>().FirstOrDefault();
            spectrumPanel.ShouldNotBeNull("the Spectrum tab must be rendered");
            var help = spectrumPanel!.GetVisualDescendants().OfType<HelpFlyoutButton>().FirstOrDefault();
            help.ShouldNotBeNull("the Spectrum tab must carry the (?) help button next to the toggle");
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
                .OfType<MziFringeAnimation>().ToList();
            animations.ShouldNotBeEmpty("the flyout must contain the ΔL→fringes animation");
            foreach (var animation in animations)
                animation.AutoPlay = false;

            SetProgress(animations, MziFringeAnimation.ShowcasePhases[2]); // bright peak
            var peak = Capture(window, dir, $"flyout-{fileTag}-peak.png",
                $"Coherent interference help ({fileTag}): the wave in the longer meander arm lags, the swept λ makes the output pulse — bright peak on the fringe curve.", manifest);

            SetProgress(animations, MziFringeAnimation.ShowcasePhases[1]); // deep null
            using var nullFrame = Capture(window, dir, $"flyout-{fileTag}-null.png",
                $"Coherent interference help ({fileTag}): anti-phase arms cancel — the output goes dark, the marker sits in a fringe null.", manifest);

            using (peak)
            {
                CountDifferingPixels(peak, nullFrame).ShouldBeGreaterThan(0,
                    "peak and null frames must differ — the animation is not animating");
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
