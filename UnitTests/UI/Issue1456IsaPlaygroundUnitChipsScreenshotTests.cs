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
/// Visual documentation for the #1456 "what runs on light" row: renders the real
/// <see cref="IsaPlaygroundWindow"/> headless with the shipped RAM 4x4 network
/// published via <see cref="BuiltLogicNetworkProvider"/>, the toggle on and the
/// multiply-3x4 sample run to HALT — the unit chips under the machine state show
/// RAM green (on light) and ALU, Z, ACC, PC grey (electronic), once in English and
/// once in German. PNGs + manifest.json land in <c>docs/pr-media/issue-1456/</c>
/// (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir. Same pattern as
/// <see cref="Issue1446IsaPlaygroundPhotonicRamScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1456IsaPlaygroundUnitChipsScreenshotTests
    : IClassFixture<LogicGateRam4x4ExampleTests.Ram4x4Fixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int StepBudget = 500;
    private const string MultiplySampleFileName = "multiply-3x4.asm";

    private readonly LogicGateRam4x4ExampleTests.Ram4x4Fixture _fixture;
    private readonly string _outputDir;

    /// <summary>Attaches the shared RAM 4x4 fixture and resolves the PR-media output directory.</summary>
    public Issue1456IsaPlaygroundUnitChipsScreenshotTests(
        LogicGateRam4x4ExampleTests.Ram4x4Fixture fixture)
    {
        _fixture = fixture;
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1456");
    }

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground's unit-chip row in photonic-RAM mode, English locale.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundUnitChips_En()
    {
        CaptureInLanguage(
            SupportedLanguage.English.Code,
            "isa-playground-unit-chips-en.png",
            "ISA playground with the RAM 4x4 network built and the photonic toggle on, multiply-3x4 run to HALT: the 'what runs on light' row shows RAM green and ALU, Z, ACC, PC grey — only the data memory lives on light.");
    }

    /// <summary>Captures the playground's unit-chip row in photonic-RAM mode, German locale.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundUnitChips_De()
    {
        CaptureInLanguage(
            SupportedLanguage.German.Code,
            "isa-playground-unit-chips-de.png",
            "ISA-Playground mit aufgebautem RAM-4x4-Netzwerk und eingeschaltetem Photonic-Toggle, Multiply-3x4 bis HALT gelaufen: Die „Was läuft auf Licht“-Zeile zeigt RAM grün und ALU, Z, ACC, PC grau — nur der Datenspeicher lebt im Licht.");
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
            vm.DataMemory.ShouldNotBeNull("the RAM 4x4 network must hold the data memory on light");
            vm.UnitChips[2].IsOnLight.ShouldBeTrue("the RAM chip must be green");
            vm.UnitChips[0].IsOnLight.ShouldBeFalse("the ALU chip stays grey on the RAM network");
            vm.UnitChips[3].IsOnLight.ShouldBeFalse("ACC is always electronic for now");
            vm.UnitChips[4].IsOnLight.ShouldBeFalse("PC is always electronic for now");

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
