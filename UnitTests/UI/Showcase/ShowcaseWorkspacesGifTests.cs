using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Feature showcase: <c>workspaces.gif</c> — the shipped two-chiplet edge-coupler link seen
/// through Layout → Optics → Logic → System: the accent colour, the Run button's meaning,
/// the contextual left panel (library, sources, gates, chiplets) and the analysis dock's
/// tabs follow the workspace. Opt-in via <c>UI_SHOT_DIR</c>.
/// </summary>
[Trait("Category", "Showcase")]
[Collection("LocalizationSingleton")]
public class ShowcaseWorkspacesGifTests
{
    private const string ExampleFile = "Two Chiplets - Edge-Coupler Link.lun";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const double DockHeight = 150;
    private const int SwitchHoldMs = 650;
    private const int RunHoldMs = 1050;
    private const int ViewHoldMs = 1200;

    private static readonly Workspace[] Sequence =
        { Workspace.Layout, Workspace.Optics, Workspace.Logic, Workspace.System };

    /// <summary>Run is pressed where it has something to show on this design: the design
    /// checks in Layout and the CW simulation in Optics (the design has no logic gates;
    /// System keeps the simulation running and adds the chiplet strip).</summary>
    private static bool RunsIn(Workspace workspace) =>
        workspace is Workspace.Layout or Workspace.Optics;

    [AvaloniaFact]
    public async Task CaptureWorkspaces()
    {
        if (!ShowcaseCapture.Enabled) return;
        await ShowcaseCapture.WithEnglishUiAsync(async () =>
        {
            var vm = ShowcaseCircuit.CreateStagedViewModel();
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFile);
            (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{ExampleFile}' must load");
            await vm.FileOperations.PostLoadRouting;
            vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);

            var window = ShowcaseCircuit.BootMainWindow(vm, WindowWidth, WindowHeight);
            ShowcaseFraming.HideRightPanel(window);
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            vm.BottomPanel.Analysis.IsVisible = true;
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            ShowcaseFraming.ZoomToFit(window, vm);

            var gif = new ShowcaseGifRecorder("workspaces", window,
                new PixelRect(0, 0, WindowWidth, WindowHeight));
            foreach (var workspace in Sequence)
            {
                vm.WorkspaceSwitch.Current = workspace;
                Dispatcher.UIThread.RunJobs();
                vm.WorkspaceSwitch.Current.ShouldBe(workspace);
                if (workspace == Workspace.System)
                    vm.BottomPanel.Analysis.Open(AnalysisDockTab.Spectrum);
                if (!RunsIn(workspace))
                {
                    gif.Capture(ViewHoldMs);
                    continue;
                }
                gif.Capture(SwitchHoldMs);
                await RunAsync(vm);
                gif.Capture(RunHoldMs);
            }
            vm.Canvas.ShowPowerFlow.ShouldBeTrue("Run in Optics switches the simulation on");

            await gif.EncodeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>Presses the workspace's Run button (simulate / build logic / system run)
    /// unless it is already active, and waits for the work to settle.</summary>
    private static async Task RunAsync(CAP.Avalonia.ViewModels.MainViewModel vm)
    {
        if (!vm.WorkspaceSwitch.IsRunActive)
            await vm.RunWorkspaceCommand.ExecuteAsync(null);
        await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
        Dispatcher.UIThread.RunJobs();
    }
}
