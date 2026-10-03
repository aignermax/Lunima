using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1349 sub-nm sweep grid: renders the Spectrum tab
/// of the shipped <c>EBeam Mach-Zehnder Interferometer.lun</c> (coherent mode on,
/// 1545–1560 nm) twice — with the 16-point integer grid the old rounding produced
/// for a 300-step request, and with the new 300-point sub-nm grid that resolves
/// the fringe notch. PNGs + manifest.json land in <c>docs/pr-media/issue-1349/</c>
/// (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir.
/// Same pattern as <see cref="Issue1335MeasuredOverlayScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1349SubNmSweepScreenshotTests
{
    private const int WindowWidth = 980;
    private const int WindowHeight = 700;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int SpectrumTabIndex = 2;
    private const int SweepStartNm = 1545;
    private const int SweepEndNm = 1560;
    private const int IntegerGridStepCount = 16;
    private const int SubNmStepCount = 300;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the Spectrum tab with the coarse (before) and sub-nm (after) grid.</summary>
    [AvaloniaFact]
    public async Task CaptureIntegerGridVsSubNmSweep()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1349");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(
            "EBeam Mach-Zehnder Interferometer.lun");
        await fileOps.PostLoadRouting;

        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        try
        {
            await CaptureBeforeAndAfter(dir, canvas);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    private static async Task CaptureBeforeAndAfter(
        string dir, CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel canvas)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SelectedTabIndex = SpectrumTabIndex;
        vm.BottomPanel.Analysis.DockHeight = 480;

        var spectrum = vm.BottomPanel.Analysis.Spectrum;
        spectrum.AutoRefreshDelay = TimeSpan.Zero;
        spectrum.Configure(canvas);
        spectrum.IsCoherentInterference.ShouldBeTrue("the EBeam MZI example ships with coherent mode on");
        spectrum.StartNm = SweepStartNm;
        spectrum.EndNm = SweepEndNm;
        spectrum.StepCount = IntegerGridStepCount;

        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Content = new AnalysisDockPanel { DataContext = vm },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var manifest = new List<ManifestEntry>();
        try
        {
            await spectrum.RunSweepCommand.ExecuteAsync(null);
            spectrum.HasResult.ShouldBeTrue($"integer-grid sweep failed: {spectrum.StatusText}");
            Capture(window, dir, "spectrum-integer-grid-before.png",
                "Before: the old rounding collapses a 300-step request to these 16 integer-nm points — the fringe notch is a single sample.", manifest);

            spectrum.StepCount = SubNmStepCount;
            spectrum.PendingAutoRefresh.ShouldNotBeNull("changing the step count re-runs the sweep");
            await spectrum.PendingAutoRefresh!;
            spectrum.HasResult.ShouldBeTrue($"sub-nm sweep failed: {spectrum.StatusText}");
            Capture(window, dir, "spectrum-subnm-after.png",
                "After: 300 requested steps stay 300 distinct wavelengths (0.05 nm) — the interference notch is spectrally resolved.", manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(2);
    }

    /// <summary>Captures the window to a PNG, fails on a near-blank frame, records the caption.</summary>
    private static void Capture(
        Window window, string dir, string filename, string caption, List<ManifestEntry> manifest)
    {
        PumpRenderLoop();
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");
        using (bitmap)
        {
            CountDistinctSampledColors(bitmap!).ShouldBeGreaterThan(MinDistinctSampledColors,
                $"Near-blank render — under {MinDistinctSampledColors} distinct sampled colors in {filename}.");
            ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(dir, filename));
        }
        manifest.Add(new ManifestEntry(filename, caption));
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
}
