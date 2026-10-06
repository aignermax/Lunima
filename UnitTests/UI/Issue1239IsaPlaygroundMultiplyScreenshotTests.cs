using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
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
/// Visual documentation for the #1239 multiply-3x4 sample: renders the real
/// <see cref="IsaPlaygroundWindow"/> headless with the new sample selected and the
/// photonic-ADD toggle on (the shipped 4-bit adder published via
/// <see cref="BuiltLogicNetworkProvider"/>), run to HALT with ACC = 12 — every loop
/// ADD computed on the photonic adder, the status line naming the last addition and
/// its light-travel time. PNG + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1239/</c> (or <c>UI_SHOT_DIR/issue-1239</c>).
/// Same pattern as <see cref="Issue1230Rung5JourneyScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1239IsaPlaygroundMultiplyScreenshotTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int HaltTickBudget = 500;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public Issue1239IsaPlaygroundMultiplyScreenshotTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    /// <summary>Captures the playground halted after the photonic multiply-3x4 run.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundHaltedAfterPhotonicMultiplyRun()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
        {
            File.Delete(stale);
        }

        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);
        vm.SelectedSample = vm.Samples.Single(s => s.FileName == "multiply-3x4.asm");
        vm.ProgramText.ShouldContain("multiply-3x4", customMessage: "the picker must load the multiply sample");
        vm.UsePhotonicAdder = true;
        vm.UsePhotonicAdder.ShouldBeTrue("the published 4-bit adder must enable the photonic toggle");

        vm.ToggleRunCommand.Execute(null);
        int ticks = 0;
        while (vm.IsRunning && ticks < HaltTickBudget)
        {
            vm.AdvanceRunTick();
            ticks++;
        }

        vm.IsRunning.ShouldBeFalse("multiply-3x4 must halt within the tick budget");
        vm.Accumulator.ShouldBe(12, "3 x 4 by repeated addition halts with ACC = 12");
        vm.PhotonicStatusText.ShouldContain("0000 + 1100 = 1100",
            customMessage: "the last ADD reloads the total (0 + RAM[0] = 12) on the photonic adder");
        vm.PhotonicStatusText.ShouldContain(
            _fixture.Network.Gates.Count.ToString(CultureInfo.InvariantCulture));

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            PumpRenderLoop();
            var bitmap = window.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull("CaptureRenderedFrame returned null");
            CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                "near-blank render — the capture would not document anything");
            ScreenshotArtifacts.SavePng(bitmap, Path.Combine(dir, "isa-playground-multiply-photonic-halted.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        var manifest = new[]
        {
            new
            {
                file = "isa-playground-multiply-photonic-halted.png",
                caption = "ISA playground halted on the multiply-3x4 sample with the photonic-ADD toggle on: " +
                    "ACC = 12, every loop ADD computed on the photonic 4-bit adder — the status line names " +
                    "the last addition and its light-travel time.",
            },
        };
        ScreenshotArtifacts.WriteText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
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
    private static int CountDistinctSampledColors(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var pixels = new HashSet<int>();
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

        return pixels.Count;
    }

    /// <summary>Repo-root walkthrough output directory (env override: <c>UI_SHOT_DIR</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
        {
            return Path.Combine(envDir, "issue-1239");
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1239");
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1239");
    }
}
