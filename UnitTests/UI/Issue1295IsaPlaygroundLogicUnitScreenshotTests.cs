using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1295 ISA playground logic-unit slice: renders the
/// real <see cref="IsaPlaygroundWindow"/> headless with the shipped Logic Unit
/// 4-bit network published via <see cref="BuiltLogicNetworkProvider"/> and the
/// toggle on — one capture of the toggle label naming both operations ("Compute
/// AND + NOT on the photonic chip") and one right after the photonic NOT step of
/// the "Mask &amp; invert" sample, where the status line shows the photonic NOT
/// with operand and result in binary plus the gate count. PNGs + manifest.json
/// land in <c>docs/pr-media/issue-1295/</c> (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or
/// a temp dir. Same pattern as
/// <see cref="Issue1284IsaPlaygroundPhotonicAndScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1295IsaPlaygroundLogicUnitScreenshotTests
    : IClassFixture<LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private const string MaskAndInvertProgram =
        "LOAD 10\nSTORE 0\nLOAD 12\nAND 0\nNOT\nSTORE 1\nHALT";

    private readonly LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture _fixture;
    private readonly string _outputDir;

    /// <summary>Attaches the shared logic-unit fixture and resolves the PR-media output directory.</summary>
    public Issue1295IsaPlaygroundLogicUnitScreenshotTests(
        LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture fixture)
    {
        _fixture = fixture;
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1295");
    }

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground with the Logic Unit network: toggle on, label naming both ops.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicAndNotToggle()
    {
        var manifest = new List<ManifestEntry>();

        var vm = CreatePhotonicVm();
        vm.PhotonicToggleLabel.ShouldContain("AND",
            customMessage: "the toggle must name the photonic AND");
        vm.PhotonicToggleLabel.ShouldContain("NOT",
            customMessage: "the toggle must name the photonic NOT");
        vm.HeaderTitle.ShouldContain("AND + NOT",
            customMessage: "the header must name both photonic operations");

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, "isa-playground-logic-unit-toggle-both-ops.png",
                "ISA playground with the Logic Unit 4-bit network built: the photonic toggle names both operations ('Compute AND + NOT on the photonic chip') and the header reads 'photonic AND + NOT' — one chip computes two ISA instructions.",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(manifest);
    }

    /// <summary>Captures the playground right after the photonic NOT step of Mask &amp; invert.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundAfterPhotonicNotStep()
    {
        var manifest = new List<ManifestEntry>();

        var vm = CreatePhotonicVm();
        vm.ProgramText = MaskAndInvertProgram;
        vm.AssembleCommand.Execute(null);
        for (var i = 0; i < 5; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.Accumulator.ShouldBe(7, "the capture must show the machine right after the photonic NOT");
        vm.PhotonicStatusText.ShouldContain("1000",
            customMessage: "the status names the NOT operand in binary");
        vm.PhotonicStatusText.ShouldContain("0111",
            customMessage: "the status names the NOT result in binary");

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, "isa-playground-logic-unit-after-photonic-not.png",
                "ISA playground running 'Mask & invert' on the Logic Unit 4-bit chip, right after the NOT step: the status line shows the photonic NOT (1000 → 0111) with the gate count — AND and NOT both ran on light on one chip.",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(manifest);
    }

    /// <summary>A playground on the fixture's network with the photonic toggle already on.</summary>
    private IsaPlaygroundViewModel CreatePhotonicVm()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.PhotonicAndAlu.ShouldNotBeNull("the fixture network must run AND photonically");
        vm.PhotonicNotAlu.ShouldNotBeNull("the fixture network must run NOT photonically");
        return vm;
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
