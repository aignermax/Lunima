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
/// Visual documentation for the #1468 ISA playground ALU + RAM slice: renders the
/// real <see cref="IsaPlaygroundWindow"/> headless with the shipped combined
/// ALU + RAM network published via <see cref="BuiltLogicNetworkProvider"/>, the
/// toggle on and the multiply-3x4 sample run to HALT — the header names the photonic
/// adder and the photonic data RAM together and the unit-chip row shows ALU and RAM
/// on light, once in English and once in German. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1468/</c> (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir.
/// Same pattern as <see cref="Issue1446IsaPlaygroundPhotonicRamScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1468IsaPlaygroundAluRamScreenshotTests
    : IClassFixture<AluRamChipExampleTests.AluRamFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int StepBudget = 500;
    private const string MultiplySampleFileName = "multiply-3x4.asm";

    private readonly AluRamChipExampleTests.AluRamFixture _fixture;
    private readonly string _outputDir;

    /// <summary>Attaches the shared combined-chip fixture and resolves the PR-media output directory.</summary>
    public Issue1468IsaPlaygroundAluRamScreenshotTests(AluRamChipExampleTests.AluRamFixture fixture)
    {
        _fixture = fixture;
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1468");
    }

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground with ALU and data RAM on light, English locale.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicAluAndDataMemory_En()
    {
        CaptureInLanguage(
            SupportedLanguage.English.Code,
            "isa-playground-photonic-alu-ram-en.png",
            "ISA playground with the combined ALU + RAM network built and the photonic toggle on, multiply-3x4 run to HALT: the header names the photonic adder and the photonic data RAM, and the unit-chip row shows ALU and RAM on light together — one chip, both units on light.");
    }

    /// <summary>Captures the playground with ALU and data RAM on light, German locale.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicAluAndDataMemory_De()
    {
        CaptureInLanguage(
            SupportedLanguage.German.Code,
            "isa-playground-photonic-alu-ram-de.png",
            "ISA-Playground mit aufgebautem kombiniertem ALU-+RAM-Netzwerk und eingeschaltetem Photonic-Toggle, Multiply-3x4 bis HALT gelaufen: Der Titel nennt den photonischen Addierer und das photonische Daten-RAM, und die Baustein-Zeile zeigt ALU und RAM gemeinsam auf Licht — ein Chip, beide Einheiten auf Licht.");
    }

    /// <summary>Renders the window in <paramref name="languageCode"/> and captures it after the run.</summary>
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
            vm.SelectedSample = vm.Samples.Single(s => s.FileName == MultiplySampleFileName);
            RunToHalt(vm);
            vm.PhotonicAlu.ShouldNotBeNull("the adder must run on light on this chip");
            vm.DataMemory.ShouldNotBeNull("the RAM must live on the photonic registers");
            vm.DataMemory!.WriteCount.ShouldBe(6, "multiply-3x4 commits one photonic write per STORE");
            vm.IsPhotonicDataMemoryActive.ShouldBeTrue("the RAM readout must carry the 'on light' chip");
            vm.UnitChips[0].IsOnLight.ShouldBeTrue("the ALU chip must show on light");
            vm.UnitChips[2].IsOnLight.ShouldBeTrue("the RAM chip must show on light");
            vm.Accumulator.ShouldBe(12, "multiply-3x4 halts with ACC = 12 with both units on light");

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

    private static void RunToHalt(IsaPlaygroundViewModel vm)
    {
        var steps = 0;
        while (vm.MachineStatusText != LocalizationService.Instance.Translate("IsaPlayground.StatusHalted")
               && steps < StepBudget)
        {
            vm.StepCommand.Execute(null);
            steps++;
        }
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
