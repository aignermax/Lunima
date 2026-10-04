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
/// Visual documentation for #1374: renders the Spectrum tab of the shipped
/// <c>EBeam Add-Drop Ring.lun</c> in English and German — the legend sits
/// outside the plot area and every entry is unique and readable
/// ("In → Through.port 2", "In → Drop.port 2") instead of four identical
/// "Grating Coupler TE 1550.port 2" rows. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1374/</c> (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir.
/// Same pattern as <see cref="Issue1359AddDropRingScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
// Two full-MainWindow 500-step sweeps — CI covers it, local default runs exclude Category=Slow.
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class Issue1374SpectrumLegendScreenshotTests
{
    private const int WindowWidth = 980;
    private const int WindowHeight = 700;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int SpectrumTabIndex = 2;
    private const int SweepStartNm = 1500;
    private const int SweepEndNm = 1600;
    private const int SweepStepCount = 500;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the Spectrum tab with the disambiguated, outside-plot legend (en + de).</summary>
    [AvaloniaFact]
    public async Task CaptureSpectrumWithReadableLegend()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1374");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            await CaptureSpectrum(dir, SupportedLanguage.English.Code,
                "spectrum-legend-readable-en.png",
                "Spectrum of the EBeam add-drop ring: unique, readable legend entries "
                + "(\"In → Through.port 2\", \"In → Drop.port 2\") and the legend box moved "
                + "outside the plot area so it never covers curve data.", manifest);
            await CaptureSpectrum(dir, SupportedLanguage.German.Code,
                "spectrum-legend-readable-de.png",
                "Spektrum des EBeam-Add-Drop-Rings: eindeutige, lesbare Legende "
                + "(„In → Through.port 2“, „In → Drop.port 2“), Legendenbox außerhalb "
                + "des Diagrammbereichs — sie verdeckt keine Kurven mehr.", manifest);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(2);
    }

    private static async Task CaptureSpectrum(
        string dir, string languageCode, string filename, string caption,
        List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(languageCode);

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        await fileOps.PostLoadRouting;

        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SelectedTabIndex = SpectrumTabIndex;
        vm.BottomPanel.Analysis.DockHeight = 480;

        var spectrum = vm.BottomPanel.Analysis.Spectrum;
        spectrum.AutoRefreshDelay = TimeSpan.Zero;
        spectrum.Configure(canvas);
        spectrum.StartNm = SweepStartNm;
        spectrum.EndNm = SweepEndNm;
        spectrum.StepCount = SweepStepCount;

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
            await spectrum.RunSweepCommand.ExecuteAsync(null);
            spectrum.HasResult.ShouldBeTrue($"ring sweep failed: {spectrum.StatusText}");

            var titles = spectrum.PlotModel.Series.Select(s => s.Title).ToList();
            titles.Distinct().Count().ShouldBe(titles.Count,
                $"legend entries must be unique (#1374). Titles: {string.Join(" | ", titles)}");

            Capture(window, dir, filename, caption, manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
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
