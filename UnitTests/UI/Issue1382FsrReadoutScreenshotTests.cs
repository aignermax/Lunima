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
/// Visual documentation for #1382: renders the Spectrum tab of the shipped
/// <c>EBeam Add-Drop Ring.lun</c> swept over 1500–1600 nm and captures the FSR
/// readout lines under the plot — one per simulated curve ("In → Through.port 2:
/// FSR 9.8 nm (9 dips)", "In → Drop.port 2: FSR 9.7 nm (10 peaks)") — in English
/// and German. PNGs + manifest.json land in <c>docs/pr-media/issue-1382/</c>
/// (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir. Same pattern as
/// <see cref="Issue1359AddDropRingScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1382FsrReadoutScreenshotTests
{
    private const int WindowWidth = 980;
    private const int WindowHeight = 700;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int SpectrumTabIndex = 2;
    private const int SweepStartNm = 1500;
    private const int SweepEndNm = 1600;
    private const int SweepStepCount = 500;
    private const double ExpectedRingFsrNm = 9.8;
    private const double FsrToleranceNm = 0.2;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the Spectrum tab FSR readout in English and German.</summary>
    [AvaloniaFact]
    public async Task CaptureFsrReadout_EnglishAndGerman()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1382");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        await fileOps.PostLoadRouting;

        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        var manifest = new List<ManifestEntry>();
        try
        {
            await CaptureLocale(dir, canvas, SupportedLanguage.English.Code, "spectrum-fsr-readout-en.png",
                "EBeam add-drop ring, 1500–1600 nm coherent sweep: FSR readout under the plot — "
                + "Through dips and Drop peaks both report the ring FSR ≈ 9.8 nm (#1382).", manifest);
            await CaptureLocale(dir, canvas, SupportedLanguage.German.Code, "spectrum-fsr-readout-de.png",
                "EBeam Add-Drop-Ring, kohärenter Sweep 1500–1600 nm: FSR-Anzeige unter dem Plot — "
                + "Through-Minima und Drop-Maxima melden beide die Ring-FSR ≈ 9,8 nm (#1382).", manifest);
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

    private static async Task CaptureLocale(
        string dir,
        CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel canvas,
        string languageCode,
        string filename,
        string caption,
        List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(languageCode);

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

            spectrum.FsrReadouts.Count.ShouldBeGreaterThanOrEqualTo(2,
                "the ring's Through and Drop curves must both yield an FSR readout");
            foreach (var line in spectrum.FsrReadouts)
            {
                line.MeanFsrNm.ShouldBe(ExpectedRingFsrNm, FsrToleranceNm,
                    $"{line.CurveLabel}: readout must match the ring FSR ≈ {ExpectedRingFsrNm} nm");
            }

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
