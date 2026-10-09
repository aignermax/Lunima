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
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the "Connect two chiplets" guided tour
/// (issue #1288, slice 4 of #769): captures the Home entry and all six steps
/// anchored in the real MainWindow over the shipped Two-Chiplets example —
/// intro on the canvas, Run simulation, move the die (with the "do it for me"
/// button), the Design Checks finding, the Align-chiplet fix and the closing
/// words. Also proves every tour anchor resolves to a visible named control.
/// PNGs + manifest.json land in <c>docs/pr-media/issue-1288/</c> (only
/// refreshed with CAP_UPDATE_PR_MEDIA=1; ordinary runs write to a temp dir).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1288ConnectChipletsTourScreenshotTests
{
    private const double DockHeight = 380;
    private const int CaptureAttempts = 5;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;
    private const double WavelengthNm = 1550;

    private readonly string _outputDir;

    /// <summary>Resolves the PR-media output directory for this issue.</summary>
    public Issue1288ConnectChipletsTourScreenshotTests()
    {
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1288");
    }

    /// <summary>Captures the Home card's fourth tour entry, below the three existing tours.</summary>
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

    /// <summary>Captures all six steps in the real MainWindow over the shipped example.</summary>
    [AvaloniaFact]
    public async Task CaptureAllSteps_InMainWindow()
    {
        Directory.CreateDirectory(_outputDir);

        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        await TwoChipletsExampleLoader.LoadAsync(
            canvas, CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial.ConnectChipletsTourViewModel.ExampleFileName);
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);

        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            Dispatcher.UIThread.RunJobs();
            FitDesign(window, vm);

            var tour = vm.ConnectChipletsTour;
            tour.Start();
            tour.IsActive.ShouldBeTrue();

            var overlay = window.GetVisualDescendants().OfType<TourAnchorOverlay>()
                .Single(o => o.Card is ConnectChipletsTourPanel);

            // Step 1/6: the two chiplets on the canvas.
            AssertAnchorResolves(window, tour.Steps[0].TargetName);
            CaptureTourStep(window, overlay, () => { },
                tour.Steps[0].TargetName, "step1-two-chiplets.png");

            // Step 2/6: run the simulation.
            tour.NextCommand.Execute(null);
            AssertAnchorResolves(window, tour.Steps[1].TargetName);
            CaptureTourStep(window, overlay, () => { },
                tour.Steps[1].TargetName, "step2-run-simulation.png");

            // Step 3/6: move the receiver die — the card offers "Move it for me".
            canvas.ShowPowerFlow = true;
            tour.CurrentStepIndex.ShouldBe(2, "the finished simulation must advance to the move step");
            CaptureTourStep(window, overlay, () => { },
                tour.Steps[2].TargetName, "step3-move-die.png");

            // Step 4/6: the helper move lands the tour on the Design Checks tab.
            tour.MoveReceiverForMeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            tour.CurrentStepIndex.ShouldBe(3, "the 5 µm move must advance to the Design Checks step");
            vm.BottomPanel.Analysis.IsVisible.ShouldBeTrue("reaching the checks step opens the dock");
            AssertAnchorResolves(window, tour.Steps[3].TargetName);
            CaptureTourStep(window, overlay, () => { },
                tour.Steps[3].TargetName, "step4-design-checks.png");

            // Step 5/6: the facet-gap finding offers the Align-chiplet fix.
            RunChecks(vm);
            tour.CurrentStepIndex.ShouldBe(4, "the finding must advance to the Align step");
            vm.RightPanel.DesignValidation.IsCurrentIssueAlignable
                .ShouldBeTrue("the finding must offer the one-click fix");
            AssertAnchorResolves(window, tour.Steps[4].TargetName);
            CaptureTourStep(window, overlay, () => { },
                tour.Steps[4].TargetName, "step5-align-chiplet.png");

            // Step 6/6: aligned again, checks clean, closing words float.
            var link = canvas.Connections.Select(c => c.Connection)
                .Single(c => c.IsCrossChipletFacetLink);
            var refusal = new ChipletAlignmentService(canvas, new CAP.Avalonia.Commands.CommandManager())
                .TryAlign(link, WavelengthNm);
            refusal.ShouldBeNull("the recorded 5 µm gap must be alignable");
            RunChecks(vm);
            tour.CurrentStepIndex.ShouldBe(5, "the clean checks must advance to the closing words");
            CaptureTourStep(window, overlay, () => { }, null, "step6-closing.png");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        WriteManifest();
        Directory.GetFiles(_outputDir, "*.png").Length.ShouldBeGreaterThanOrEqualTo(7);
    }

    /// <summary>Runs the chiplet checks on the panel the window shows (journey wiring).</summary>
    private static void RunChecks(MainViewModel vm)
    {
        var groups = vm.Canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList();
        var chipletA = groups.Single(g => g.GroupName == ChipletEdgeCouplerJourneyDesign.ChipletAName);
        var chipletB = groups.Single(g => g.GroupName == ChipletEdgeCouplerJourneyDesign.ChipletBName);
        vm.RightPanel.DesignValidation.RunValidation(
            vm.Canvas.ConnectionManager.Connections,
            groups: new[] { chipletA, chipletB },
            allComponents: vm.Canvas.Components.Select(c => c.Component),
            externalPortPins: chipletA.PhysicalPins.Concat(chipletB.PhysicalPins),
            wavelengthNm: WavelengthNm);
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
    /// (same pattern as Issue1267RunProgramTourScreenshotTests).
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
                caption = "The Home card's guided-tour buttons: the new 'Connect two chiplets' tour " +
                    "sits below the three existing tours — the multi-chiplet story (rung 6) gets its " +
                    "guided entry point. Placement: own row under the tours, because all guided " +
                    "tours must be reachable from one glance at the Home card.",
            },
            new
            {
                file = "step1-two-chiplets.png",
                caption = "Tour step 1/6: the shipped Two-Chiplets example on the canvas — two dies joined " +
                    "by an edge-coupler link. The card anchors to the canvas. Placement: beside the design, " +
                    "because both dies and the link between them must stay visible.",
            },
            new
            {
                file = "step2-run-simulation.png",
                caption = "Tour step 2/6: the toolbar's Run-simulation button is spotlighted. Placement: " +
                    "below the toolbar, so the card never covers the chiplets the user is about to simulate.",
            },
            new
            {
                file = "step3-move-die.png",
                caption = "Tour step 3/6: the move step on the canvas, with the 'Move it for me' helper " +
                    "button that shifts the receiver die 5 µm through an undoable group move. Placement: " +
                    "beside the design, keeping both dies in view while the user drags.",
            },
            new
            {
                file = "step4-design-checks.png",
                caption = "Tour step 4/6: the receiver die sits 5 µm off its facet and the tour has opened " +
                    "the analysis dock on the Design Checks tab — the card points at Run checks. " +
                    "Placement: above the dock, so the tab and the canvas stay readable.",
            },
            new
            {
                file = "step5-align-chiplet.png",
                caption = "Tour step 5/6: the checker flags the facet gap and the card points at the " +
                    "'Align chiplet' one-click fix next to Prev/Next. Placement: above the dock, keeping " +
                    "the finding row and the fix button uncovered.",
            },
            new
            {
                file = "step6-closing.png",
                caption = "Tour step 6/6: aligned again, checks clean — the closing card floats " +
                    "bottom-centre with the facet-gap takeaway and the pointer to the Gaussian-beam " +
                    "animation in the Design Checks help.",
            },
        };
        ScreenshotArtifacts.WriteText(
            Path.Combine(_outputDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
