using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.Views;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1194 ISA playground window: renders the real
/// <see cref="IsaPlaygroundWindow"/> headless and captures (1) the count-to-5
/// sample after six steps — state readout advanced, current source line
/// highlighted — and (2) an assembler error shown inline under the state.
/// PNGs + manifest.json land in <c>artifacts/ui-screenshots/issue-1194/</c>
/// (or <c>UI_SHOT_DIR/issue-1194</c>). Same pattern as
/// <see cref="Issue1171NewComponentHelpFlyoutScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1194IsaPlaygroundScreenshotTests
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int StepsBeforeCapture = 6;

    /// <summary>Guards the once-per-run output-directory clearing shared by both capture tests.</summary>
    private static readonly object OutputDirectoryLock = new();
    private static bool _outputDirectoryCleared;

    /// <summary>One manifest row: PNG file name plus its one-sentence caption.</summary>
    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>Captures the playground mid-run: state readout filled, current line highlighted.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundAfterSteps()
    {
        var dir = PrepareOutputDirectory();
        var manifest = new List<ManifestEntry>();

        var vm = new IsaPlaygroundViewModel();
        vm.IsAssembled.ShouldBeTrue("the window must open with the count-to-5 sample pre-assembled");
        for (int i = 0; i < StepsBeforeCapture; i++)
            vm.StepCommand.Execute(null);
        vm.Accumulator.ShouldBe(1, "after 6 steps count-to-5 has completed its first increment");

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, dir, "isa-playground-after-steps.png",
                "ISA playground: count-to-5 after 6 steps — PC/ACC/RAM updated, current source line highlighted.",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(dir, manifest);
    }

    /// <summary>Captures the playground with an inline assembler error under the state readout.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundAssemblerError()
    {
        var dir = PrepareOutputDirectory();
        var manifest = new List<ManifestEntry>();

        var vm = new IsaPlaygroundViewModel();
        vm.ProgramText = "LOAD 1\nFROB 2\nHALT";
        vm.AssembleCommand.Execute(null);
        vm.ErrorText.ShouldContain("2"); // the error must name the source line of the bad mnemonic
        vm.IsAssembled.ShouldBeFalse();

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            using var bitmap = Capture(window, dir, "isa-playground-assembler-error.png",
                "ISA playground: unknown mnemonic on line 2 — inline error, Step/Reset disabled.",
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
                    File.Delete(stale);
                var staleManifest = Path.Combine(dir, "manifest.json");
                if (File.Exists(staleManifest))
                    File.Delete(staleManifest);
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
            return pixels;

        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                pixels.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }
        return pixels;
    }

    /// <summary>Repo-root walkthrough output directory (env override: <c>UI_SHOT_DIR</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1194");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1194");
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1194");
    }
}
