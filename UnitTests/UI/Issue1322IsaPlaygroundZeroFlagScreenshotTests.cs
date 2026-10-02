using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1322 ISA playground zero-flag slice: renders the
/// real <see cref="IsaPlaygroundWindow"/> headless with the shipped Zero Detect
/// 4-bit network published via <see cref="BuiltLogicNetworkProvider"/> and the
/// toggle on — the toggle label reads "Decide JZ on the photonic chip" and the
/// header names the photonic zero flag (JZ), once in English and once in German.
/// PNGs + manifest.json land in <c>docs/pr-media/issue-1322/</c> (with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir. Same pattern as
/// <see cref="Issue1295IsaPlaygroundLogicUnitScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1322IsaPlaygroundZeroFlagScreenshotTests
    : IClassFixture<LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private readonly LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture _fixture;
    private readonly string _outputDir;

    /// <summary>Attaches the shared zero-detect fixture and resolves the PR-media output directory.</summary>
    public Issue1322IsaPlaygroundZeroFlagScreenshotTests(
        LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture fixture)
    {
        _fixture = fixture;
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1322");
    }

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground in photonic-zero-flag mode, English locale.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicZeroFlagToggle_En()
    {
        CaptureInLanguage(
            SupportedLanguage.English.Code,
            "isa-playground-zero-flag-toggle-en.png",
            "ISA playground with the Zero Detect 4-bit network built and the photonic toggle on: the toggle reads 'Decide JZ on the photonic chip' and the header names the photonic zero flag (JZ) — the program's branch decision runs on light.");
    }

    /// <summary>Captures the playground in photonic-zero-flag mode, German locale.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicZeroFlagToggle_De()
    {
        CaptureInLanguage(
            SupportedLanguage.German.Code,
            "isa-playground-zero-flag-toggle-de.png",
            "ISA-Playground mit aufgebautem Zero-Detect-4-Bit-Netzwerk und eingeschaltetem Photonic-Toggle: Der Toggle liest „JZ auf dem photonischen Chip entscheiden“ und der Titel nennt das photonische Zero-Flag (JZ) — die Sprungentscheidung des Programms läuft über Licht.");
    }

    /// <summary>Renders the window in <paramref name="languageCode"/> and captures the toggle/header row.</summary>
    private void CaptureInLanguage(string languageCode, string filename, string caption)
    {
        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(languageCode);
        var manifest = new List<ManifestEntry>();
        try
        {
            var provider = new BuiltLogicNetworkProvider();
            provider.Publish(_fixture.Network);
            var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
            vm.ZeroFlag.ShouldNotBeNull("the Zero Detect network must drive JZ photonically");
            vm.PhotonicToggleLabel.ShouldContain("JZ",
                customMessage: "the toggle must name the photonic JZ decision");
            vm.HeaderTitle.ShouldContain("JZ",
                customMessage: "the header must name the photonic zero flag (JZ)");

            var window = new IsaPlaygroundWindow { DataContext = vm };
            window.Show();
            try
            {
                using var bitmap = Capture(window, filename, caption, manifest);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }

        WriteManifest(manifest);
    }

    /// <summary>Captures the window to a PNG, fails on a near-blank frame, records the caption.</summary>
    private WriteableBitmap Capture(
        Window window, string filename, string caption, List<ManifestEntry> manifest)
    {
        Directory.CreateDirectory(_outputDir);
        PumpRenderLoop();
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");

        var path = Path.Combine(_outputDir, filename);
        CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
            $"Near-blank render — under {MinDistinctSampledColors} distinct sampled colors in {filename}.");
        ScreenshotArtifacts.SavePng(bitmap, path);
        manifest.Add(new ManifestEntry(filename, caption));
        return bitmap;
    }

    /// <summary>
    /// Merges this test's entries into the shared manifest.json (both capture tests
    /// write the same file; run order is not guaranteed, so merge instead of overwrite).
    /// </summary>
    private void WriteManifest(List<ManifestEntry> manifest)
    {
        var path = Path.Combine(_outputDir, "manifest.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        var merged = new List<ManifestEntry>();
        if (File.Exists(path))
        {
            merged.AddRange(
                JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path), options) ?? new());
        }

        foreach (var entry in manifest)
        {
            merged.RemoveAll(existing => existing.File == entry.File);
            merged.Add(entry);
        }

        merged.Sort((a, b) => string.CompareOrdinal(a.File, b.File));
        ScreenshotArtifacts.WriteText(path, JsonSerializer.Serialize(merged, options));
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

    /// <summary>Reads a deterministic grid of ARGB pixels in scan order.</summary>
    private static List<int> SamplePixels(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        var pixels = new List<int>();
        if (width <= 0 || height <= 0)
        {
            return pixels;
        }

        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
            {
                pixels.Add(Marshal.ReadInt32(rowAddr, x * 4));
            }
        }

        return pixels;
    }
}
