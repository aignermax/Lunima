using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.Integration.RamScale;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the "Store a number in light" guided tour
/// (issue #1422, slice 5 of #769): captures steps 1, 3 and 4 — the Build button
/// of the dock Logic tab spotlighted over the shipped RAM 2x4 example, the
/// Step-clock button after the word 5 was committed on a real clock edge, and
/// the output rows while the read-back shows the stored word again — each in
/// English and in German, against the real MainWindow with the Logic panel
/// driven like a user would (build, toggle, step). Also proves every captured
/// anchor resolves to a visible named control. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1422/</c> (only refreshed with CAP_UPDATE_PR_MEDIA=1;
/// ordinary runs write to a temp dir). Same pattern as
/// <see cref="Issue1267RunProgramTourScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1422StoreNumberTourScreenshotTests
{
    private const double DockHeight = 380;
    private const int CaptureAttempts = 5;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private readonly string _outputDir;

    public Issue1422StoreNumberTourScreenshotTests()
    {
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1422");
    }

    /// <summary>Captures journey steps 1, 3 and 4 in English and in German.</summary>
    [AvaloniaFact]
    public async Task CaptureTourSteps_EnglishAndGerman()
    {
        Directory.CreateDirectory(_outputDir);
        foreach (var stale in Directory.GetFiles(_outputDir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        foreach (var language in new[] { SupportedLanguage.English, SupportedLanguage.German })
            await CaptureTourInLanguage(language, manifest);

        ScreenshotArtifacts.WriteText(Path.Combine(_outputDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Directory.GetFiles(_outputDir, "*.png").Length.ShouldBe(6);
        manifest.Count.ShouldBe(6);
    }

    /// <summary>
    /// Boots the real MainWindow over the RAM 2x4 example in the given language
    /// and captures the three tour steps, driving the Logic panel for real first
    /// so the panel behind the card shows honest state.
    /// </summary>
    private async Task CaptureTourInLanguage(SupportedLanguage language, List<object> manifest)
    {
        // Pin the locale: the localization singleton is process-global, and the
        // published PR media must match the requested language regardless of the
        // runner's OS language.
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(language.Code);
        try
        {
            // Load the RAM 2x4 example exactly like the pinned integration fixture.
            var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
            fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
            var path = Path.Combine(
                ExampleDesignFilesTests.ExamplesDirectory(), StoreNumberTourViewModel.RamExampleFileName);
            (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
                $"'{StoreNumberTourViewModel.RamExampleFileName}' must load through the real load path");
            await fileOps.PostLoadRouting;

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

                var tour = vm.StoreNumberTour;
                var logic = vm.RightPanel.Logic;

                AssertAnchorResolves(window, tour.Steps[0].TargetName);

                var overlay = window.GetVisualDescendants().OfType<TourAnchorOverlay>()
                    .Single(o => o.Card is StoreNumberTourPanel);

                // Step 1: the Build button — honest state: nothing built yet.
                tour.IsActive = true;
                CaptureTourStep(window, overlay, () => tour.CurrentStepIndex = 0,
                    tour.Steps[0].TargetName, $"step1-build-logic-{language.Code}.png");
                manifest.Add(new
                {
                    file = $"step1-build-logic-{language.Code}.png",
                    caption = $"Tour step 1/5 ({language.Code}): the Build-logic-network button of the " +
                        "analysis dock's Logic tab is spotlighted in the real MainWindow, the shipped " +
                        "RAM 2x4 example loaded on the canvas behind it. Placement: below the Build " +
                        "button, because the dock sits at the bottom edge and the card must not cover " +
                        "the memory cells on the canvas.",
                });

                // Drive like a user: build the network, pick 5 (D0+D2), LOAD, one clock edge.
                await logic.BuildNetworkCommand.ExecuteAsync(null);
                logic.HasNetwork.ShouldBeTrue(logic.StatusText);
                logic.HasRegisters.ShouldBeTrue("the RAM 2x4 has eight register bits");
                Set(logic, "D0", true);
                Set(logic, "D2", true);
                Set(logic, "LOAD", true);
                logic.StepClockCommand.Execute(null);
                ReadQWord(logic).ShouldBe(5, "the clock edge committed D0+D2 = 5 into word 0");

                // Step 3: the Step-clock button — the commit already happened, so the
                // register readout and the Q bus behind the card show the stored word.
                BringIntoView(window, "LogicStepClockButton");
                AssertAnchorResolves(window, tour.Steps[2].TargetName);
                CaptureTourStep(window, overlay, () => tour.CurrentStepIndex = 2,
                    tour.Steps[2].TargetName, $"step3-store-word-{language.Code}.png");
                manifest.Add(new
                {
                    file = $"step3-store-word-{language.Code}.png",
                    caption = $"Tour step 3/5 ({language.Code}): the card points at the Step-clock " +
                        "button right after one real clock edge committed 5 (D0+D2) into word 0 — " +
                        "the register readout behind the card lists the eight committed cell bits. " +
                        "Placement: next to the clock controls, so the input toggles and the Q readout " +
                        "stay visible while the user steps.",
                });

                // Step 4: LOAD off, address away and back — Q reads the stored word again.
                Set(logic, "LOAD", false);
                Set(logic, "A", true);
                ReadQWord(logic).ShouldBe(0, "word 1 was never written");
                Set(logic, "A", false);
                ReadQWord(logic).ShouldBe(5, "back at word 0 the cell answers with the stored number");

                BringIntoView(window, "LogicOutputRows");
                AssertAnchorResolves(window, tour.Steps[3].TargetName);
                CaptureTourStep(window, overlay, () => tour.CurrentStepIndex = 3,
                    tour.Steps[3].TargetName, $"step4-read-back-{language.Code}.png");
                manifest.Add(new
                {
                    file = $"step4-read-back-{language.Code}.png",
                    caption = $"Tour step 4/5 ({language.Code}): the output rows are spotlighted while " +
                        "the Q bus reads 5 again — LOAD went off, the address A was flipped to word 1 " +
                        "(Q fell to 0) and back, and the cell held its state in light. Placement: " +
                        "beside the output rows, keeping the Q chips readable behind the card.",
                });
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

    /// <summary>Toggles one network input exactly like the panel's toggle row.</summary>
    private static void Set(LogicPanelViewModel logic, string pinName, bool on) =>
        logic.Inputs.Single(i => i.PinName == pinName).IsOn = on;

    /// <summary>Reads the Q bus (Q0–Q3) as a decimal word.</summary>
    private static int ReadQWord(LogicPanelViewModel logic)
    {
        var word = 0;
        for (var bit = 0; bit < 4; bit++)
            if (logic.Outputs.FirstOrDefault(o => o.PinName == "Q" + bit)?.IsOne == true)
                word |= 1 << bit;
        return word;
    }

    /// <summary>Scrolls the named control of the Logic tab into view (the tab scrolls in the 380 px dock).</summary>
    private static void BringIntoView(Window window, string name)
    {
        var control = window.GetVisualDescendants().OfType<Control>()
            .First(c => c.Name == name);
        control.BringIntoView();
        PumpRenderLoop();
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
}
