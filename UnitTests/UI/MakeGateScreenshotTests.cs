using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis.MakeGate;
using UnitTests.Helpers;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation of the Make-gate steps and the "Your first gate" tour: the
/// right-panel gate section on the grouped gate kit (entry, pin roles, simulated power
/// bars with the automatic NAND threshold, saved gate with "Open in Logic", the overlap
/// warning for a wrong function), the animated help, the Logic tab's "not a gate yet"
/// list, the Home entry and two tour steps in the real MainWindow. PNGs land in
/// <c>docs/pr-media/make-gate/</c> only with CAP_UPDATE_PR_MEDIA=1 (temp dir otherwise).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class MakeGateScreenshotTests
{
    private const double PanelWidth = 380;
    private const int CaptureAttempts = 5;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private readonly string _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("make-gate");

    /// <summary>Captures the gate section through every Make-gate step.</summary>
    [AvaloniaFact]
    public async Task CaptureMakeGateSteps()
    {
        Directory.CreateDirectory(_outputDir);
        await WithEnglish(async () =>
        {
            var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
            var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
            // The gate section belongs to the Logic and System workspaces.
            vm.WorkspaceSwitch.Current = CAP.Avalonia.ViewModels.Workspaces.Workspace.Logic;
            canvas.Selection.SelectSingle(groupVm);
            vm.RightPanel.TruthTable.ConfigureForSelection(groupVm, canvas);
            vm.RightPanel.MakeGate.ConfigureForSelection(groupVm, canvas);
            var makeGate = vm.RightPanel.MakeGate;

            var window = ShowPanel(new TruthTablePanel { DataContext = vm });
            try
            {
                Save(window, "01-gate-section-entry.png");

                makeGate.OpenCommand.Execute(null);
                Save(window, "02-make-gate-pin-roles.png");

                await makeGate.SimulateCommand.ExecuteAsync(null);
                makeGate.HasResult.ShouldBeTrue(makeGate.StatusText);
                makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.And);
                makeGate.HasOverlap.ShouldBeTrue();
                Save(window, "05-make-gate-overlap-warning.png");

                makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);
                makeGate.MatchesFunction.ShouldBeTrue();
                Save(window, "03-make-gate-threshold.png");

                makeGate.SaveCommand.Execute(null);
                makeGate.IsSaved.ShouldBeTrue(makeGate.StatusText);
                Save(window, "04-make-gate-saved.png");
            }
            finally
            {
                Close(window);
            }

            await CaptureLogicCandidatesAsync();
        });
    }

    /// <summary>Captures a mid-loop frame of the Make-gate help animation.</summary>
    [AvaloniaFact]
    public async Task CaptureMakeGateHelp()
    {
        Directory.CreateDirectory(_outputDir);
        await WithEnglish(() =>
        {
            var window = ShowPanel(new MakeGateHelpFlyout(), width: 440);
            try
            {
                var animations = window.GetVisualDescendants().OfType<HelpAnimationBase>().ToList();
                animations.Count.ShouldBeGreaterThanOrEqualTo(2, "roles and threshold each carry an animation");
                foreach (var animation in animations)
                {
                    animation.AutoPlay = false;
                    animation.Progress = 0.5;
                }
                Save(window, "06-make-gate-help.png");
            }
            finally
            {
                Close(window);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Captures the Home entry and two anchored steps of the tour in the real MainWindow.</summary>
    [AvaloniaFact]
    public async Task CaptureFirstGateTour()
    {
        Directory.CreateDirectory(_outputDir);
        await WithEnglish(async () =>
        {
            var preferences = new UserPreferencesService(
                Path.Combine(Path.GetTempPath(), $"firstgate-shot-prefs-{Guid.NewGuid():N}.json"));
            var home = new CAP.Avalonia.ViewModels.Home.HomeViewModel(
                new RecentProjectsService(preferences), preferences, new ExampleDesignsService());
            var homeWindow = new Window { Width = 900, Height = 720, Content = new HomeView { DataContext = home } };
            homeWindow.Show();
            try
            {
                Save(homeWindow, "07-home-first-gate-tour.png");
            }
            finally
            {
                Close(homeWindow);
            }

            var canvas = await GateKitFixture.LoadAsync(
                CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial.FirstGateTourViewModel.GateKitExampleFileName);
            var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
            // The gate section belongs to the Logic and System workspaces.
            vm.WorkspaceSwitch.Current = CAP.Avalonia.ViewModels.Workspaces.Workspace.Logic;
            var window = ShowcaseCircuit.BootMainWindow(vm);
            try
            {
                vm.Home.IsHomeVisible = false;
                Dispatcher.UIThread.RunJobs();
                ShowcaseCircuit.FitView(window, vm);
                var tour = vm.FirstGateTour;
                tour.Start();
                var overlay = window.GetVisualDescendants().OfType<TourAnchorOverlay>()
                    .Single(o => o.Card is FirstGateTourPanel);
                CaptureTourStep(window, overlay, null, "08-tour-step1-parts.png");

                tour.NextCommand.Execute(null);
                var groupVm = GateKitFixture.GroupEverything(canvas);
                canvas.SelectedComponent = groupVm;
                Dispatcher.UIThread.RunJobs();
                tour.CurrentStepIndex.ShouldBe(2, "grouping advances to the Make-gate step");
                CaptureTourStep(window, overlay, tour.CurrentTargetName, "09-tour-step3-make-gate.png");
            }
            finally
            {
                Close(window);
            }
        });
    }

    /// <summary>The Logic tab's empty state listing a group that is not a gate yet.</summary>
    private async Task CaptureLogicCandidatesAsync()
    {
        var (canvas, _) = await GateKitFixture.GroupedKitAsync();
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        // The gate section belongs to the Logic and System workspaces.
        vm.WorkspaceSwitch.Current = CAP.Avalonia.ViewModels.Workspaces.Workspace.Logic;
        vm.RightPanel.Logic.HasGateCandidates.ShouldBeTrue();
        var window = ShowPanel(new LogicPanel { DataContext = vm }, width: 520);
        try
        {
            Save(window, "10-logic-not-a-gate-yet.png");
        }
        finally
        {
            Close(window);
        }
    }

    private void CaptureTourStep(Window window, TourAnchorOverlay overlay, string? targetName, string fileName)
    {
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
        Save(window, fileName);
    }

    private static Window ShowPanel(Control content, double width = PanelWidth)
    {
        var window = new Window
        {
            Width = width,
            SizeToContent = SizeToContent.Height,
            Content = new Border { Padding = new Avalonia.Thickness(16, 4, 16, 16), Child = content },
        };
        window.Show();
        PumpRenderLoop();
        return window;
    }

    private static void Close(Window window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WithEnglish(Func<Task> body)
    {
        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        try
        {
            await body();
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    private void Save(Window window, string fileName)
    {
        PumpRenderLoop();
        ScreenshotArtifacts.SettleAnimations();
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

        bitmap.ShouldNotBeNull($"render miss for {fileName}");
        using (bitmap)
        {
            CountDistinctSampledColors(bitmap!).ShouldBeGreaterThan(MinDistinctSampledColors,
                $"near-blank render for {fileName}");
            ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(_outputDir, fileName)).Length.ShouldBeGreaterThan(0);
        }
    }

    private static void PumpRenderLoop()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static int CountDistinctSampledColors(Bitmap bitmap)
    {
        using var fb = ((WriteableBitmap)bitmap).Lock();
        var pixels = new HashSet<int>();
        int stepX = Math.Max(1, fb.Size.Width / SampleGridSize);
        int stepY = Math.Max(1, fb.Size.Height / SampleGridSize);
        for (int y = 0; y < fb.Size.Height; y += stepY)
        {
            var row = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < fb.Size.Width; x += stepX)
                pixels.Add(Marshal.ReadInt32(row, x * 4));
        }
        return pixels.Count;
    }
}
