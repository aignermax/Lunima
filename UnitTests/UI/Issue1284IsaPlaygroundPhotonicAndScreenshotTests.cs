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
/// Visual documentation for the #1284 ISA playground photonic-AND slice: renders the
/// real <see cref="IsaPlaygroundWindow"/> headless with the shipped AND 4-bit network
/// published via <see cref="BuiltLogicNetworkProvider"/>, the toggle on, right after
/// the AND step — the status line shows the photonic AND with both operands and the
/// result in binary plus the gate count — plus the disabled toggle whose hint now
/// names all three examples (4-bit adder, NOT 4-bit, AND 4-bit). PNGs +
/// manifest.json land in <c>docs/pr-media/issue-1284/</c> (with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir. Same pattern as
/// <see cref="Issue1275IsaPlaygroundPhotonicNotScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1284IsaPlaygroundPhotonicAndScreenshotTests
    : IClassFixture<LogicGateAnd4BitExampleTests.And4BitFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private readonly LogicGateAnd4BitExampleTests.And4BitFixture _fixture;
    private readonly string _outputDir;

    /// <summary>Attaches the shared AND-4-bit fixture and resolves the PR-media output directory.</summary>
    public Issue1284IsaPlaygroundPhotonicAndScreenshotTests(
        LogicGateAnd4BitExampleTests.And4BitFixture fixture)
    {
        _fixture = fixture;
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1284");
    }

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground with the AND network, toggle on, after the AND step.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicAndAfterStep()
    {
        var manifest = new List<ManifestEntry>();

        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ProgramText = "LOAD 12\nSTORE 1\nLOAD 10\nAND 1\nSTORE 0\nHALT";
        vm.AssembleCommand.Execute(null);
        for (var i = 0; i < 4; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.Accumulator.ShouldBe(8, "the capture must show the machine right after the photonic AND");
        vm.PhotonicStatusText.ShouldContain("1010",
            customMessage: "the status names the first operand of the photonic AND in binary");
        vm.PhotonicStatusText.ShouldContain("1100",
            customMessage: "the status names the second operand of the photonic AND in binary");
        vm.HeaderTitle.ShouldContain("AND",
            customMessage: "the header must name the photonic AND, never the adder or NOT");

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, "isa-playground-photonic-and-after-step.png",
                "ISA playground with the AND 4-bit network built: toggle on ('Compute AND on the photonic chip'), right after the AND step — the status line shows the photonic AND with both operands and the result in binary plus the gate count.",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(manifest);
    }

    /// <summary>Captures the playground with no built network: disabled toggle, hint names all three examples.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicToggleDisabled()
    {
        var manifest = new List<ManifestEntry>();

        var vm = new IsaPlaygroundViewModel();
        vm.IsAnyPhotonicAvailable.ShouldBeFalse("no network was built, so the toggle stays disabled");
        vm.IsPhotonicToggleEnabled.ShouldBeFalse();

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, "isa-playground-photonic-toggle-disabled.png",
                "ISA playground without a built logic network: the photonic toggle is disabled and the hint names all three accepted examples (4-bit adder, NOT 4-bit, AND 4-bit).",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
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
