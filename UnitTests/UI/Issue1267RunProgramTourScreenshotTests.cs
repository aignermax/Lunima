using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the "Run a program on your chip" guided tour
/// (issue #1267, slice 3 of #769): captures the three key steps — Logic Build
/// anchored in the real MainWindow over the shipped 4-bit adder example, and
/// the photonic-ADD toggle plus photonic stepping anchored inside the real
/// <see cref="IsaPlaygroundWindow"/>. Also proves every tour anchor resolves to
/// a visible named control in its window. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1267/</c> (only refreshed with CAP_UPDATE_PR_MEDIA=1;
/// ordinary runs write to a temp dir).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1267RunProgramTourScreenshotTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const double DockHeight = 380;
    private const int CaptureAttempts = 5;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;
    private readonly string _outputDir;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public Issue1267RunProgramTourScreenshotTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture)
    {
        _fixture = fixture;
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1267");
    }

    /// <summary>Captures the Home card's third tour entry, next to the two existing tours.</summary>
    [AvaloniaFact]
    public void CaptureHomeTourEntry()
    {
        Directory.CreateDirectory(_outputDir);

        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        try
        {
            var preferences = new UserPreferencesService(
                Path.Combine(Path.GetTempPath(), $"home-shot-prefs-{Guid.NewGuid():N}.json"));
            var home = new CAP.Avalonia.ViewModels.Home.HomeViewModel(
                new RecentProjectsService(preferences), preferences, new ExampleDesignsService());
            var window = new Window { Width = 900, Height = 720, Content = new HomeView { DataContext = home } };
            window.Show();
            try
            {
                PumpRenderLoop();
                SaveFrame(window, Path.Combine(_outputDir, "home-tour-entry.png"));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    /// <summary>Captures journey step 2: the Build step anchored in the real MainWindow.</summary>
    [AvaloniaFact]
    public async Task CaptureBuildStep_InMainWindow()
    {
        Directory.CreateDirectory(_outputDir);

        // Pin the locale: the localization singleton is process-global, and the
        // published PR media must be English regardless of the runner's OS language.
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var canvas = _fixture.Canvas;
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);

        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            Dispatcher.UIThread.RunJobs();
            FitDesign(window, vm);

            vm.BottomPanel.Analysis.IsVisible = true;
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            vm.BottomPanel.Analysis.OpenLogic();
            Dispatcher.UIThread.RunJobs();

            AssertAnchorResolves(window, vm.RunProgramTour.Steps[0].TargetName);
            AssertAnchorResolves(window, vm.RunProgramTour.Steps[1].TargetName);

            var overlay = window.GetVisualDescendants().OfType<TourAnchorOverlay>()
                .Single(o => o.Card is RunProgramTourPanel);

            vm.RunProgramTour.IsActive = true;
            CaptureTourStep(window, overlay, () => vm.RunProgramTour.CurrentStepIndex = 0,
                vm.RunProgramTour.Steps[0].TargetName, "step2-build-logic.png");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    /// <summary>Captures journey steps 4 and 5: photonic toggle and photonic stepping in the playground.</summary>
    [AvaloniaFact]
    public void CapturePhotonicSteps_InIsaPlayground()
    {
        Directory.CreateDirectory(_outputDir);

        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var playground = new IsaPlaygroundViewModel(provider);
        playground.SelectedSample = playground.Samples.Single(
            s => s.FileName == RunProgramTourViewModel.MultiplySampleFileName);
        playground.IsAssembled.ShouldBeTrue(playground.ErrorText);

        var tour = new RunProgramTourViewModel(new LogicPanelViewModel(), MakeDock(), playground);
        var window = new IsaPlaygroundWindow { DataContext = playground, Tour = tour };
        window.Show();
        try
        {
            PumpRenderLoop();
            AssertAnchorResolves(window, tour.Steps[2].TargetName);
            AssertAnchorResolves(window, tour.Steps[3].TargetName);
            AssertAnchorResolves(window, tour.Steps[4].TargetName);

            var overlay = window.GetVisualDescendants().OfType<TourAnchorOverlay>().Single();

            // Journey step 4: the card points at the photonic-ADD toggle (still off).
            tour.IsActive = true;
            CaptureTourStep(window, overlay, () => tour.CurrentStepIndex = 3,
                tour.Steps[3].TargetName, "step4-photonic-toggle.png");

            // Journey step 5: a few photonic ADDs in — the status line names the
            // last addition, the card points at the Step button.
            playground.UsePhotonicAdder = true;
            playground.UsePhotonicAdder.ShouldBeTrue(
                "the published 4-bit adder must enable the photonic toggle");
            StepPhotonicAdds(playground, count: 3);
            playground.Accumulator.ShouldBeGreaterThan(0);

            CaptureTourStep(window, overlay, () => tour.CurrentStepIndex = 4,
                tour.Steps[4].TargetName, "step5-photonic-stepping.png");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        WriteManifest();
        Directory.GetFiles(_outputDir, "*.png").Length.ShouldBeGreaterThanOrEqualTo(3);
    }

    /// <summary>Steps the playground until <paramref name="count"/> photonic ADDs were reported.</summary>
    private static void StepPhotonicAdds(IsaPlaygroundViewModel playground, int count)
    {
        var reported = 0;
        var lastStatus = string.Empty;
        for (var i = 0; i < 60 && reported < count; i++)
        {
            playground.StepCommand.Execute(null);
            if (playground.PhotonicStatusText.Length > 0 && playground.PhotonicStatusText != lastStatus)
            {
                reported++;
                lastStatus = playground.PhotonicStatusText;
            }
        }

        reported.ShouldBe(count, "three photonic ADDs must land within the step budget");
    }

    /// <summary>Proves the named tour anchor resolves to a visible control in this window.</summary>
    private static void AssertAnchorResolves(Window window, string? targetName)
    {
        targetName.ShouldNotBeNullOrEmpty("every actionable tour step names an anchor");
        window.GetVisualDescendants().OfType<Control>()
            .Any(c => c.Name == targetName && c.IsEffectivelyVisible)
            .ShouldBeTrue($"tour anchor '{targetName}' must resolve to a visible control");
    }

    /// <summary>
    /// Advances the engine, then re-applies the target name so the overlay recomputes
    /// after layout — headless substitute for the overlay's 150 ms poll timer
    /// (same pattern as Issue1167TourAnchorScreenshotTests).
    /// </summary>
    private void CaptureTourStep(
        Window window, TourAnchorOverlay overlay, Action advance, string? targetName, string fileName)
    {
        advance();
        Dispatcher.UIThread.RunJobs();
        overlay.TargetName = targetName;
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            using var settleFrame = window.CaptureRenderedFrame();
        }

        overlay.TargetName = null;
        Dispatcher.UIThread.RunJobs();
        overlay.TargetName = targetName;
        SaveFrame(window, Path.Combine(_outputDir, fileName));
    }

    private static void SaveFrame(Window window, string path)
    {
        Bitmap? bitmap = null;
        for (var attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            (bitmap as IDisposable)?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"render miss for {Path.GetFileName(path)}");
        using (bitmap)
        {
            CountDistinctSampledColors(bitmap!).ShouldBeGreaterThan(MinDistinctSampledColors,
                "near-blank render — the capture would not document anything");
            ScreenshotArtifacts.SavePng(bitmap!, path).Length.ShouldBeGreaterThan(0);
        }
    }

    /// <summary>Fits the loaded design into the canvas viewport (zoom + pan).</summary>
    private static void FitDesign(MainWindow window, MainViewModel vm)
    {
        var components = vm.Canvas.Components;
        if (components.Count == 0)
            return;
        double minX = components.Min(c => c.X);
        double minY = components.Min(c => c.Y);
        double maxX = components.Max(c => c.X + c.Width);
        double maxY = components.Max(c => c.Y + c.Height);
        ShowcaseCircuit.SetView(window, vm, (minX, minY, maxX - minX, maxY - minY));
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
    private static int CountDistinctSampledColors(Bitmap bitmap)
    {
        using var fb = ((WriteableBitmap)bitmap).Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        if (width <= 0 || height <= 0)
            return 0;

        var pixels = new HashSet<int>();
        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                pixels.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }

        return pixels.Count;
    }

    private void WriteManifest()
    {
        var manifest = new[]
        {
            new
            {
                file = "home-tour-entry.png",
                caption = "The Home card's 'Learn Lunima' row: the new 'Run a program on your chip' tour " +
                    "sits next to 'Learn Lunima' and 'Watch it compute' — the rung-5 wow moment gets its " +
                    "guided entry point.",
            },
            new
            {
                file = "step2-build-logic.png",
                caption = "Tour step 1/6 (journey step 2): the Build-logic-network button in the analysis " +
                    "dock's Logic tab is spotlighted in the real MainWindow, the shipped 4-bit adder example " +
                    "loaded on the canvas behind it.",
            },
            new
            {
                file = "step4-photonic-toggle.png",
                caption = "Tour step 4/6 (journey step 4): the card docks next to the 'Compute ADD on the " +
                    "photonic chip' toggle inside the ISA playground window, the multiply-3x4 sample assembled.",
            },
            new
            {
                file = "step5-photonic-stepping.png",
                caption = "Tour step 5/6 (journey step 5): three photonic ADDs in — the status line names the " +
                    "last addition and its light-travel time while the card points at the Step button.",
            },
        };
        ScreenshotArtifacts.WriteText(
            Path.Combine(_outputDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static CAP.Avalonia.ViewModels.Panels.AnalysisDockViewModel MakeDock() =>
        new(new CAP.Avalonia.ViewModels.Analysis.TimeDomainViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.EyeDiagram.EyeDiagramViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum.WavelengthSpectrumViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.AnalysisOutput.AnalysisOutputPanelViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis.MonteCarloViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.CircuitOptimization.CircuitOptimizationViewModel(
                new CAP.Avalonia.Commands.CommandManager()));
}
