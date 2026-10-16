using System.Diagnostics;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Logic.GateView;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;

namespace UnitTests.Logic.GateView;

/// <summary>
/// The Logic workspace's gate/physical toggle: default per workspace entry, hand-over to
/// the physical view for waveguide tools, selection sync with Layout, off-thread rebuilds
/// and the live net levels shared with the Logic tab.
/// </summary>
[Collection("LocalizationSingleton")]
public class GateViewViewModelTests
{
    private const int UiBudgetMs = 100;

    [Fact]
    public void EnteringLogic_ShowsTheGateView_OtherWorkspacesShowPhysical()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();

        vm.GateView.IsShown.ShouldBeFalse("Layout shows the physical design");
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.GateView.IsShown.ShouldBeTrue("Logic opens in the gate view");

        vm.GateView.ToggleCommand.Execute(null);
        vm.GateView.IsShown.ShouldBeFalse();
        vm.GateView.IsPhysicalShown.ShouldBeTrue();

        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.GateView.IsAvailable.ShouldBeFalse();
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.GateView.IsShown.ShouldBeTrue("every entry into Logic starts in the gate view again");
    }

    [Fact]
    public void Toggle_OutsideLogic_DoesNothing()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();

        vm.GateView.ToggleCommand.Execute(null);

        vm.GateView.IsGateViewPreferred.ShouldBeTrue();
        vm.GateView.IsShown.ShouldBeFalse();
    }

    [Theory]
    [InlineData(InteractionMode.Cut)]
    public void PickingAWaveguideTool_SwitchesToThePhysicalView(InteractionMode mode)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        vm.CanvasInteraction.CurrentMode = mode;

        vm.GateView.IsShown.ShouldBeFalse("waveguides are edited in the physical view, not silently ignored");
        vm.GateView.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void SelectAndDelete_KeepTheGateView()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        vm.CanvasInteraction.CurrentMode = InteractionMode.Delete;
        vm.CanvasInteraction.CurrentMode = InteractionMode.Select;

        vm.GateView.IsShown.ShouldBeTrue();
    }

    [Fact]
    public async Task ShowInLayout_SwitchesToLayout_WithTheGateSelected()
    {
        var vm = await LoadAsync("Logic Gate Half Adder.lun");
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        var not1 = vm.Canvas.Components.Single(c => c.Component is ComponentGroup { GroupName: "NOT1" });
        vm.Canvas.Selection.SelectSingle(not1);

        vm.GateView.ShowInLayoutCommand.Execute(null);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
        vm.Canvas.Selection.SelectedComponents.ShouldBe([not1]);
        vm.Canvas.SelectedComponent.ShouldBe(not1);
    }

    [Fact]
    public async Task Rebuild_OnTheCpuExample_LeavesTheCallingThreadFree()
    {
        var vm = await LoadAsync("Logic Gate ALU + RAM + ACC.lun");
        var stopwatch = Stopwatch.StartNew();

        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.GateView.RequestRebuild();

        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(UiBudgetMs,
            "entering Logic only schedules the gate view; extraction runs off the UI thread");
        await vm.GateView.PendingRebuild;
        vm.GateView.Graph.Nodes.Count.ShouldBeGreaterThan(500);
        vm.GateView.IsStale.ShouldBeFalse();
    }

    [Fact]
    public async Task DesignEdits_AreDebounced_AndRebuildTheGraph()
    {
        var vm = await LoadAsync("Logic Gate Half Adder.lun");
        var release = new TaskCompletionSource();
        var debounces = 0;
        var gateView = new GateViewViewModel(vm.Canvas, vm.WorkspaceSwitch, () => 1550, delay: (_, token) =>
        {
            debounces++;
            return release.Task.WaitAsync(token);
        });
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        await gateView.RebuildNowAsync();
        gateView.Graph.Nodes.Count.ShouldBe(7);

        var not1 = vm.Canvas.Components.Single(c => c.Component is ComponentGroup { GroupName: "NOT1" });
        vm.Canvas.Components.Remove(not1);
        vm.Canvas.Components.Add(not1);
        vm.Canvas.Components.Remove(not1);
        gateView.IsStale.ShouldBeTrue();
        release.SetResult();
        await gateView.PendingRebuild;

        debounces.ShouldBe(3, "every edit restarts the quiet period");
        gateView.Graph.Nodes.Count.ShouldBe(6, "only the last edit's design is published");
    }

    [Fact]
    public async Task NetLevels_FollowTheLogicTabEvaluation()
    {
        var vm = await LoadAsync("Logic Gate Half Adder.lun");
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        await vm.GateView.RebuildNowAsync();
        var graph = vm.GateView.Graph;
        var sum = graph.Nets.Single(n => n.Driver == new LogicPinRef("NAND4", "Y"));
        var carry = graph.Nets.Single(n => n.Driver == new LogicPinRef("NOT1", "Y"));
        var inputA = graph.Nets.Single(n => n.Driver == null && n.Name == "A");
        vm.GateView.Levels.LevelOf(sum).ShouldBe(GateNetLevel.Unknown, "nothing is evaluated before Run");

        await vm.RightPanel.Logic.BuildNetworkCommand.ExecuteAsync(null);
        vm.RightPanel.Logic.Inputs.Single(i => i.PinName == "A").IsOn = true;

        vm.GateView.Levels.LevelOf(inputA).ShouldBe(GateNetLevel.One);
        vm.GateView.Levels.LevelOf(sum).ShouldBe(GateNetLevel.One, "1 XOR 0 = 1");
        vm.GateView.Levels.LevelOf(carry).ShouldBe(GateNetLevel.Zero, "1 AND 0 = 0");

        vm.RightPanel.Logic.Inputs.Single(i => i.PinName == "B").IsOn = true;
        vm.GateView.Levels.LevelOf(sum).ShouldBe(GateNetLevel.Zero);
        vm.GateView.Levels.LevelOf(carry).ShouldBe(GateNetLevel.One);
    }

    private static async Task<MainViewModel> LoadAsync(string file)
    {
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(
            Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), file));
        return MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
    }
}
