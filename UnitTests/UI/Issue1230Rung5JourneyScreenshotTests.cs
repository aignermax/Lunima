using System.Globalization;
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
/// Visual documentation for the #1230 rung-5 journey: renders the real
/// <see cref="IsaPlaygroundWindow"/> headless at the journey's payoff — count-to-5
/// halted with ACC = 5, computed with the photonic-ADD toggle on (the network of the
/// shipped 4-bit adder published via <see cref="BuiltLogicNetworkProvider"/>), the
/// status line naming the photonic adder and its gate count. PNG + manifest.json land
/// in <c>artifacts/ui-screenshots/issue-1230/</c> (or <c>UI_SHOT_DIR/issue-1230</c>).
/// Same pattern as <see cref="Issue1215IsaPlaygroundPhotonicScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1230Rung5JourneyScreenshotTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public Issue1230Rung5JourneyScreenshotTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    /// <summary>Captures the playground halted after the photonic count-to-5 run.</summary>
    [AvaloniaFact]
    public void CaptureIsaPlaygroundHaltedAfterPhotonicRun()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
        {
            File.Delete(stale);
        }

        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ToggleRunCommand.Execute(null);
        while (vm.IsRunning)
        {
            vm.AdvanceRunTick();
        }

        vm.Accumulator.ShouldBe(5, "count-to-5 halts with ACC = 5");
        vm.PhotonicStatusText.ShouldBe(string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicAdd"),
            _fixture.Network.Gates.Count));

        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            PumpRenderLoop();
            var bitmap = window.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull("CaptureRenderedFrame returned null");
            CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                "near-blank render — the capture would not document anything");
            ScreenshotArtifacts.SavePng(bitmap, Path.Combine(dir, "isa-playground-photonic-halted.png"));
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
                file = "isa-playground-photonic-halted.png",
                caption = "ISA playground after the rung-5 journey: count-to-5 halted with ACC = 5, " +
                    "computed on the photonic 4-bit adder — the status line names the photonic adder and its gate count.",
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
            return Path.Combine(envDir, "issue-1230");
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1230");
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1230");
    }
}
