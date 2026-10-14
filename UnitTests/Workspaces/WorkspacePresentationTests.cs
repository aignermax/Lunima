using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas.ViewSettings;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>
/// Applying and restoring a workspace's presentation on the main view model: Layout hides the
/// light and marks the checks, Optics brings the light back and recedes the geometry, the
/// user's own overlay toggles come back per workspace, stale check markers disappear on edit,
/// and the dock returns to the tab last used in a workspace.
/// </summary>
[Collection("LocalizationSingleton")]
public class WorkspacePresentationTests
{
    private const string MziExample = "EBeam Mach-Zehnder Interferometer.lun";

    private static async Task<MainViewModel> OpenMziInOpticsWithLightOnAsync()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), MziExample);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        vm.WorkspaceSwitch.Current = Workspace.Optics;
        await vm.RunWorkspaceCommand.ExecuteAsync(null);
        vm.Canvas.ShowPowerFlow.ShouldBeTrue(vm.StatusText);
        return vm;
    }

    [Fact]
    public async Task SwitchingToLayout_HidesTheLight_AndEmphasisesGeometry()
    {
        var vm = await OpenMziInOpticsWithLightOnAsync();
        vm.Canvas.ViewSettings.Emphasis.ShouldBe(CanvasEmphasis.Light);

        vm.WorkspaceSwitch.Current = Workspace.Layout;

        vm.Canvas.ShowPowerFlow.ShouldBeFalse("Layout is geometry first: the power overlay is off");
        vm.WorkspaceSwitch.IsSimulationOn.ShouldBeFalse();
        vm.Canvas.ViewSettings.Emphasis.ShouldBe(CanvasEmphasis.Geometry);
        vm.Canvas.ViewSettings.ShowDesignCheckMarkers.ShouldBeTrue();
    }

    [Fact]
    public async Task RunInLayout_MarksTheFindingsOnTheCanvas()
    {
        var vm = await OpenMziInOpticsWithLightOnAsync();
        vm.WorkspaceSwitch.Current = Workspace.Layout;

        await vm.RunWorkspaceCommand.ExecuteAsync(null);

        vm.Canvas.ViewSettings.HasDesignCheckFindings.ShouldBeTrue("the MZI's open pins are hints to mark");
        vm.Canvas.ViewSettings.DesignCheckFindings.Count
            .ShouldBe(vm.RightPanel.DesignValidation.AllFindings.Count);
    }

    [Fact]
    public async Task ReturningToOptics_BringsTheLightBack()
    {
        var vm = await OpenMziInOpticsWithLightOnAsync();
        vm.WorkspaceSwitch.Current = Workspace.Layout;

        vm.WorkspaceSwitch.Current = Workspace.Optics;

        vm.Canvas.ShowPowerFlow.ShouldBeTrue("Optics restores the simulation the user left on");
        vm.Canvas.PowerFlowVisualizer.IsEnabled.ShouldBeTrue();
        vm.Canvas.ViewSettings.Emphasis.ShouldBe(CanvasEmphasis.Light);
        vm.Canvas.ViewSettings.ShowDesignCheckMarkers.ShouldBeFalse();
    }

    [Fact]
    public async Task TheUsersOwnToggles_AreRememberedPerWorkspace()
    {
        var vm = await OpenMziInOpticsWithLightOnAsync();
        await vm.RunWorkspaceCommand.ExecuteAsync(null);
        vm.Canvas.ShowPowerFlow.ShouldBeFalse("the user switched the light off in Optics");
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.Canvas.ViewSettings.ToggleDesignCheckMarkersCommand.Execute(null);

        vm.WorkspaceSwitch.Current = Workspace.Optics;
        vm.Canvas.ShowPowerFlow.ShouldBeFalse("Optics keeps the light off as the user left it");
        vm.Canvas.ViewSettings.ShowDesignCheckMarkers.ShouldBeFalse();

        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.Canvas.ViewSettings.ShowDesignCheckMarkers.ShouldBeFalse("Layout keeps the markers hidden as the user left them");
    }

    [Fact]
    public void Optics_WithoutASimulationResult_KeepsTheLightOff()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();

        vm.WorkspaceSwitch.Current = Workspace.Optics;

        vm.Canvas.ShowPowerFlow.ShouldBeFalse("nothing to show yet; Run turns it on");
        vm.Canvas.ViewSettings.Emphasis.ShouldBe(CanvasEmphasis.Light);
    }

    [Fact]
    public void Optics_InTransientMode_DoesNotResumeTheCwOverlay()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.SimulationMode = SimulationMode.Transient;

        vm.WorkspaceSwitch.Current = Workspace.Optics;

        vm.Canvas.ShowPowerFlow.ShouldBeFalse();
    }

    [Fact]
    public async Task AnEdit_DropsTheStaleCheckMarkers()
    {
        var vm = await OpenMziInOpticsWithLightOnAsync();
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        await vm.RunWorkspaceCommand.ExecuteAsync(null);
        vm.Canvas.ViewSettings.HasDesignCheckFindings.ShouldBeTrue();

        vm.CommandManager.ExecuteCommand(new NoOpCommand());

        vm.Canvas.ViewSettings.HasDesignCheckFindings.ShouldBeFalse("a marker must never point at a problem that may be gone");
    }

    [Fact]
    public void ToolPlacement_FollowsTheProfile_AndNoToolIsLost()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var ws = vm.WorkspaceSwitch;

        foreach (var workspace in Enum.GetValues<Workspace>())
        {
            ws.Current = workspace;
            (ws.ShowsProbeTool ^ ws.IsProbeInOverflow).ShouldBeTrue($"Probe must be reachable in {workspace}");
            (ws.IsCutPrimary ^ ws.IsCutInOverflow).ShouldBeTrue($"Cut must be reachable in {workspace}");
            ws.HasOverflowTools.ShouldBe(ws.IsProbeInOverflow || ws.IsCutInOverflow);
        }

        ws.Current = Workspace.Layout;
        (ws.ShowsProbeTool && ws.IsCutPrimary && !ws.HasOverflowTools).ShouldBeTrue();
        ws.Current = Workspace.Optics;
        (ws.ShowsProbeTool && ws.IsCutInOverflow).ShouldBeTrue();
    }

    [Fact]
    public void TheDock_ReturnsToTheTabLastUsedInAWorkspace()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var dock = vm.BottomPanel.Analysis;
        vm.WorkspaceSwitch.Current = Workspace.Optics;
        dock.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Transient, "Optics opens on the optical tabs");
        dock.Open(AnalysisDockTab.Spectrum);

        vm.WorkspaceSwitch.Current = Workspace.Layout;
        dock.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Checks);
        vm.WorkspaceSwitch.Current = Workspace.Optics;

        dock.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Spectrum);
    }

    private sealed class NoOpCommand : IUndoableCommand
    {
        public string Description => "no-op";
        public void Execute() { }
        public void Undo() { }
    }
}
