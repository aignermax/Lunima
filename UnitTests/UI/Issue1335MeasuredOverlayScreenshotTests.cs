using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1335 measured-spectrum overlay: sweeps the shipped
/// <c>EBeam Mach-Zehnder Interferometer.lun</c> (coherent mode on) through the real
/// <see cref="CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum.WavelengthSpectrumViewModel"/>
/// pipeline, loads the synthetic MZI fixture CSV as the dashed "Measured" overlay with
/// the FSR/n_g result line, then captures the malformed-CSV inline error and the open
/// (?) flyout — in English and German. Also asserts the new overlay row is not clipped
/// at the dock's default height. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1335/</c> (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir.
/// Same pattern as <see cref="Issue1333CoherentSpectrumScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1335MeasuredOverlayScreenshotTests
{
    private const int WindowWidth = 980;
    private const int WindowHeight = 700;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int SpectrumTabIndex = 2;
    private const int SweepStepCount = 201;
    private const double FixtureArmImbalanceUm = 50;

    private static readonly string FixtureCsvPath = Path.Combine(
        AppContext.BaseDirectory, "Analysis", "MeasuredSpectrum", "Fixtures", "synthetic_mzi_spectrum.csv");

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures overlay + result line, inline CSV error, and the open flyout (en + de).</summary>
    [AvaloniaFact]
    public async Task CaptureMeasuredOverlayErrorAndFlyout()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1335");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(
            "EBeam Mach-Zehnder Interferometer.lun");
        await fileOps.PostLoadRouting;

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            await CaptureInLanguage(SupportedLanguage.English.Code, "en", dir, manifest, canvas);
            await CaptureInLanguage(SupportedLanguage.German.Code, "de", dir, manifest, canvas);
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

    private static async Task CaptureInLanguage(string languageCode, string fileTag, string dir,
        List<ManifestEntry> manifest, CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel canvas)
    {
        LocalizationService.Instance.SetLanguage(languageCode);

        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SelectedTabIndex = SpectrumTabIndex;

        var spectrum = vm.BottomPanel.Analysis.Spectrum;
        spectrum.Configure(canvas);
        spectrum.IsCoherentInterference.ShouldBeTrue("the EBeam MZI example ships with coherent mode on");
        spectrum.StepCount = SweepStepCount;
        await spectrum.RunSweepCommand.ExecuteAsync(null);
        spectrum.HasResult.ShouldBeTrue($"sweep failed: {spectrum.StatusText}");

        spectrum.Overlay.ArmImbalanceUm = FixtureArmImbalanceUm;
        await spectrum.Overlay.LoadFromFileAsync(FixtureCsvPath);
        spectrum.Overlay.HasOverlay.ShouldBeTrue($"fixture CSV load failed: {spectrum.Overlay.ErrorText}");
        spectrum.Overlay.ResultText.ShouldContain("n_g", Case.Sensitive,
            "ΔL > 0 must yield the group-index result line");

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
            AssertOverlayRowFitsDefaultDockHeight(window, vm);

            vm.BottomPanel.Analysis.DockHeight = 480;
            Capture(window, dir, $"spectrum-overlay-{fileTag}.png",
                $"Spectrum tab ({fileTag}): swept EBeam Mach-Zehnder (coherent on) with the fixture CSV as the dashed \"Measured\" overlay and the extracted FSR / n_g result line.", manifest);

            var malformedPath = Path.Combine(Path.GetTempPath(), $"cap-1335-bad-{Guid.NewGuid()}.csv");
            File.WriteAllText(malformedPath, "Wavelength_nm,Transmission\n1550.0,0.5\nnot-a-number,oops\n");
            try
            {
                spectrum.Overlay.ClearMeasuredCommand.Execute(null);
                await spectrum.Overlay.LoadFromFileAsync(malformedPath);
            }
            finally
            {
                File.Delete(malformedPath);
            }
            spectrum.Overlay.ErrorText.ShouldNotBeNullOrEmpty("the malformed CSV must surface an inline error");
            Capture(window, dir, $"csv-error-{fileTag}.png",
                $"Spectrum tab ({fileTag}): a malformed CSV shows an inline red parse error with the offending line — never a modal dialog.", manifest);

            await spectrum.Overlay.LoadFromFileAsync(FixtureCsvPath);
            spectrum.Overlay.ErrorText.ShouldBeEmpty("a successful reload clears the inline error");
            await OpenMeasuredHelpFlyout(window);
            Capture(window, dir, $"help-flyout-{fileTag}.png",
                $"Spectrum tab ({fileTag}): the measured-overlay (?) flyout explaining the CSV format, FSR and n_g = λ²/(FSR·ΔL).", manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>At the dock's default height the new overlay row must sit inside the viewport.</summary>
    private static void AssertOverlayRowFitsDefaultDockHeight(Window window, MainViewModel vm)
    {
        vm.BottomPanel.Analysis.DockHeight.ShouldBe(260, "test assumes the shipped default dock height");
        PumpRenderLoop();

        string loadLabel = LocalizationService.Instance.Translate("Spectrum.Measured.LoadButton");
        var loadButton = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => Equals(b.Content, loadLabel));
        loadButton.ShouldNotBeNull("the \"Load measured spectrum…\" button must be rendered");

        var scroller = loadButton!.FindAncestorOfType<ScrollViewer>();
        scroller.ShouldNotBeNull("the Spectrum tab content lives in a ScrollViewer");
        var bottom = loadButton.TranslatePoint(new Point(0, loadButton.Bounds.Height), scroller!);
        bottom.ShouldNotBeNull();
        bottom!.Value.Y.ShouldBeLessThanOrEqualTo(scroller!.Viewport.Height,
            "the overlay row must not be clipped at the dock's default height");
    }

    private static async Task OpenMeasuredHelpFlyout(Window window)
    {
        string helpTitle = LocalizationService.Instance.Translate("Spectrum.Measured.HelpTitle");
        var help = window.GetVisualDescendants().OfType<HelpFlyoutButton>()
            .FirstOrDefault(h => h.Title == helpTitle);
        help.ShouldNotBeNull("the overlay row must carry its own (?) help button");
        var innerButton = help!.GetVisualDescendants().OfType<Button>().First();
        innerButton.Flyout.ShouldNotBeNull("the help button must host a flyout");
        innerButton.Flyout!.ShowAt(innerButton);
        Dispatcher.UIThread.RunJobs();

        // HelpFlyoutButton staggers its sections in (Task.Delay + opacity transitions):
        // give the delays real time, then tick the render clock so text is fully opaque.
        await Task.Delay(600);
        for (int i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
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
