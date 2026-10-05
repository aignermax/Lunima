using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1240 rung-5 visualizer seam: renders the analysis
/// dock's Logic tab headless right after the ISA playground executed a photonic ADD
/// (LOAD 5 / STORE 0 / LOAD 3 / ADD 0 on the shipped 4-bit adder, handed over through
/// the shared <see cref="BuiltLogicNetworkProvider"/>) — the driven input toggles read
/// A = 0011 and B = 0101 and the output chips read S = 1000, Cout = 0, exactly as if
/// the user had clicked the toggles. PNG + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1240/</c> (or <c>UI_SHOT_DIR/issue-1240</c>).
/// Same pattern as <see cref="Issue1230Rung5JourneyScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1240PlaygroundDrivesLogicTabScreenshotTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const int WindowWidth = 1200;
    private const int WindowHeight = 700;
    private const int DockHeight = 560;

    /// <summary>Whitespace kept above the scrolled-to inputs header.</summary>
    private const int HeaderTopMargin = 8;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (loads the design once).</summary>
    public Issue1240PlaygroundDrivesLogicTabScreenshotTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    /// <summary>Captures the dock Logic tab after the playground's photonic ADD drove its inputs.</summary>
    [AvaloniaFact]
    public async Task CaptureLogicTabAfterPhotonicAddDroveInputs()
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
        {
            File.Delete(stale);
        }

        // Playground and the MainViewModel's Logic panel share one provider, as in DI.
        var provider = new BuiltLogicNetworkProvider();
        var vm = MainViewModelTestHelper.CreateMainViewModel(
            canvas: _fixture.Canvas, logicNetworkProvider: provider);
        var logic = vm.RightPanel.Logic;
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        logic.HasNetwork.ShouldBeTrue(logic.StatusText);

        var playground = new IsaPlaygroundViewModel(provider)
        {
            UsePhotonicAdder = true,
            ProgramText = "LOAD 5\nSTORE 0\nLOAD 3\nADD 0\nHALT",
        };
        playground.AssembleCommand.Execute(null);
        for (var i = 0; i < 4; i++)
        {
            playground.StepCommand.Execute(null);
        }

        playground.Accumulator.ShouldBe(8);
        logic.Inputs.Single(i => i.PinName == "A0").IsOn.ShouldBeTrue(
            "the playground's photonic ADD drove the Logic tab's A inputs");
        logic.Outputs.Single(o => o.PinName == "S3").IsOne.ShouldBeTrue(
            "the Logic tab's outputs show the sum 8 = 1000");

        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Content = new AnalysisDockPanel { DataContext = vm },
        };
        window.Show();
        try
        {
            vm.BottomPanel.Analysis.OpenLogic();
            PumpRenderLoop();

            // Scroll inside the tab past the (very tall) fan-out warning block to
            // the driven input toggles and the output chips — the payoff this
            // capture documents. The header's position is measured, not guessed.
            var logicPanel = window.GetVisualDescendants().OfType<LogicPanel>().Single();
            var scroller = logicPanel.GetVisualAncestors().OfType<ScrollViewer>().First();
            Dispatcher.UIThread.RunJobs();
            var inputsHeader = logicPanel.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == LocalizationService.Instance.Translate("LogicPanel.Inputs"));
            var headerY = inputsHeader.TranslatePoint(new Avalonia.Point(), scroller);
            headerY.ShouldNotBeNull("the inputs header must be realized inside the scroll viewer");
            scroller.Offset = new Avalonia.Vector(0, scroller.Offset.Y + headerY.Value.Y - HeaderTopMargin);
            PumpRenderLoop();

            var bitmap = window.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull("CaptureRenderedFrame returned null");
            CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                "near-blank render — the capture would not document anything");
            ScreenshotArtifacts.SavePng(bitmap, Path.Combine(dir, "logic-tab-after-photonic-add.png"));

            // Second capture: the named output chips S = 8 (1000) and Cout = 0 sit
            // behind the 344 raw gate-output rows, so scroll to the S bus header.
            var sumHeader = logicPanel.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text?.StartsWith("S = ", StringComparison.Ordinal) == true);
            var sumY = sumHeader.TranslatePoint(new Point(), scroller);
            sumY.ShouldNotBeNull("the S bus header must be realized inside the scroll viewer");
            scroller.Offset = new Vector(0, scroller.Offset.Y + sumY.Value.Y - HeaderTopMargin);
            PumpRenderLoop();

            var outputsBitmap = window.CaptureRenderedFrame();
            outputsBitmap.ShouldNotBeNull("CaptureRenderedFrame returned null");
            CountDistinctSampledColors(outputsBitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                "near-blank render — the capture would not document anything");
            ScreenshotArtifacts.SavePng(outputsBitmap, Path.Combine(dir, "logic-tab-outputs-after-photonic-add.png"));
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
                file = "logic-tab-after-photonic-add.png",
                caption = "Dock Logic tab after the ISA playground ran LOAD 5 / STORE 0 / LOAD 3 / ADD 0 " +
                    "photonically (issue #1240): the driven input toggles read A = 3 (0011) and B = 5 (0101), " +
                    "Cin off — exactly as if the user had clicked the toggles.",
            },
            new
            {
                file = "logic-tab-outputs-after-photonic-add.png",
                caption = "Same Logic tab scrolled to the named output chips: S = 8 (1000) and Cout = 0 — " +
                    "the live evaluation refreshed on the driven inputs shows the addition the program ran.",
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
            return Path.Combine(envDir, "issue-1240");
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1240");
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1240");
    }
}
