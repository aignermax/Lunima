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
/// Visual documentation for the #1227 ISA playground timing line: renders the real
/// <see cref="IsaPlaygroundWindow"/> headless with the "Compute ADD on the photonic
/// chip" toggle on (the shipped 4-bit adder published via
/// <see cref="BuiltLogicNetworkProvider"/>) and captures count-to-5 mid-run — the
/// status line shows the last photonic ADD as binary operands plus its light-travel
/// time in ps, and the header names the photonic adder instead of the golden model.
/// PNG + manifest.json land in <c>artifacts/ui-screenshots/issue-1227/</c>
/// (or <c>UI_SHOT_DIR/issue-1227</c>). Same pattern as
/// <see cref="Issue1215IsaPlaygroundPhotonicScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1227IsaPlaygroundTimingScreenshotTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int TicksBeforeRunningCapture = 6;

    /// <summary>Guards the once-per-run output-directory clearing.</summary>
    private static readonly object OutputDirectoryLock = new();
    private static bool _outputDirectoryCleared;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public Issue1227IsaPlaygroundTimingScreenshotTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground mid-run with the photonic light-travel line visible.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicLightTravelLine()
    {
        var dir = PrepareOutputDirectory();
        var manifest = new List<ManifestEntry>();

        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ToggleRunCommand.Execute(null);
        for (int i = 0; i < TicksBeforeRunningCapture; i++)
        {
            vm.AdvanceRunTick();
        }

        vm.IsRunning.ShouldBeTrue("the capture must show the machine mid-run");
        vm.PhotonicStatusText.ShouldContain("0000 + 0001 = 0001",
            customMessage: "the first photonic ADD of count-to-5 shows its operands in binary");
        vm.PhotonicStatusText.ShouldContain("ps",
            customMessage: "the line names the light-travel time of that addition");
        vm.HeaderTitle.ShouldNotContain("golden",
            customMessage: "while photonic ADD is on the header must not claim the golden model");

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, dir, "isa-playground-photonic-light-travel.png",
                "ISA playground mid-run on the photonic adder: the status line shows the last ADD "
                + "in binary with its light-travel time in ps; the header names the photonic adder.",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(dir, manifest);
    }

    /// <summary>
    /// Repo-root output directory; clears stale PNGs from previous runs once per test run.
    /// </summary>
    private static string PrepareOutputDirectory()
    {
        lock (OutputDirectoryLock)
        {
            var dir = ResolveOutputDirectory();
            Directory.CreateDirectory(dir);
            if (!_outputDirectoryCleared)
            {
                foreach (var stale in Directory.GetFiles(dir, "*.png"))
                {
                    File.Delete(stale);
                }

                var staleManifest = Path.Combine(dir, "manifest.json");
                if (File.Exists(staleManifest))
                {
                    File.Delete(staleManifest);
                }

                _outputDirectoryCleared = true;
            }

            return dir;
        }
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

    /// <summary>Writes the manifest.json next to the PNG.</summary>
    private static void WriteManifest(string dir, List<ManifestEntry> manifest)
    {
        var path = Path.Combine(dir, "manifest.json");
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

    /// <summary>Repo-root walkthrough output directory (env override: <c>UI_SHOT_DIR</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
        {
            return Path.Combine(envDir, "issue-1227");
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1227");
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1227");
    }
}
