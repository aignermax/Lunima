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
/// Visual documentation for the #1215 ISA playground photonic-ADD slice: renders the
/// real <see cref="IsaPlaygroundWindow"/> headless and captures (1) count-to-5
/// mid-run with the "Compute ADD on the photonic chip" toggle on — the network from
/// the shipped 4-bit adder published via <see cref="BuiltLogicNetworkProvider"/>,
/// the photonic status line showing the binary addition and its light-travel time —
/// and (2) the toggle disabled
/// with its hint when no adder network has been built. PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1215/</c> (or <c>UI_SHOT_DIR/issue-1215</c>).
/// Same pattern as <see cref="Issue1204IsaPlaygroundRunScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1215IsaPlaygroundPhotonicScreenshotTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int TicksBeforeRunningCapture = 7;

    /// <summary>Guards the once-per-run output-directory clearing shared by both capture tests.</summary>
    private static readonly object OutputDirectoryLock = new();
    private static bool _outputDirectoryCleared;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public Issue1215IsaPlaygroundPhotonicScreenshotTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground mid-run with the photonic-ADD toggle on.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicAdderToggleOn()
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
        vm.PhotonicStatusText.ShouldContain(" = ",
            customMessage: "a photonic ADD ran, so the status shows the binary addition");
        vm.PhotonicStatusText.ShouldContain("ps",
            customMessage: "the status names the light-travel time of that addition (issue #1227)");

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, dir, "isa-playground-photonic-toggle-on.png",
                "ISA playground with 'Compute ADD on the photonic chip' on: count-to-5 mid-run, the status shows the binary addition and its light-travel time.",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(dir, manifest);
    }

    /// <summary>Captures the playground with the toggle disabled and its hint showing.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundWithPhotonicToggleDisabled()
    {
        var dir = PrepareOutputDirectory();
        var manifest = new List<ManifestEntry>();

        var vm = new IsaPlaygroundViewModel();
        vm.IsPhotonicAddAvailable.ShouldBeFalse("no network was built, so the toggle stays disabled");
        vm.IsPhotonicToggleEnabled.ShouldBeFalse();

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, dir, "isa-playground-photonic-toggle-disabled.png",
                "ISA playground without a built logic network: the photonic-ADD toggle is disabled and the hint points at the 4-bit adder example.",
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
    /// Repo-root output directory; clears stale PNGs from previous runs. Both capture
    /// tests share the folder, so the first test to run does the clearing (the second
    /// must not delete the first test's fresh output).
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

    /// <summary>
    /// Merges this test's entries into the shared manifest.json (both capture tests
    /// write the same file; run order is not guaranteed, so merge instead of overwrite).
    /// </summary>
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
            return Path.Combine(envDir, "issue-1215");
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1215");
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1215");
    }
}
