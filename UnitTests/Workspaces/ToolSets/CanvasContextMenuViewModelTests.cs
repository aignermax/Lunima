using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.ToolSets;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Workspaces.ToolSets;

/// <summary>
/// Canvas context menu per workspace and target: "Edit Component" only for real components in
/// Layout and Optics; Logic offers "Show in Layout" and "Edit gate…" for an existing gate.
/// </summary>
public class CanvasContextMenuViewModelTests
{
    private static ComponentViewModel Part() => new(TestComponentFactory.CreateStraightWaveGuide());

    private static ComponentViewModel Group() => new(LogicGateFixtureFactory.CreateCombinerGroup());

    private static ComponentViewModel GateVm() => new(WorkspaceToolSetTests.Gate());

    [Theory]
    [InlineData(Workspace.Layout, true)]
    [InlineData(Workspace.Optics, true)]
    [InlineData(Workspace.Logic, false)]
    [InlineData(Workspace.System, false)]
    public void EditComponent_OnARealComponent_OnlyInLayoutAndOptics(Workspace workspace, bool expected)
    {
        var menu = new CanvasContextMenuViewModel();

        menu.Refresh(workspace, Part());

        menu.ShowsEditComponent.ShouldBe(expected);
    }

    [Theory]
    [InlineData(Workspace.Layout)]
    [InlineData(Workspace.Optics)]
    public void EditComponent_NeverForGroupsGatesOrEmptyCanvas(Workspace workspace)
    {
        var menu = new CanvasContextMenuViewModel();

        menu.Refresh(workspace, Group());
        menu.ShowsEditComponent.ShouldBeFalse("a group is not a component");
        menu.Refresh(workspace, GateVm());
        menu.ShowsEditComponent.ShouldBeFalse("a gate is edited with the gate steps");
        menu.Refresh(workspace, null);
        menu.ShowsEditComponent.ShouldBeFalse();
    }

    [Fact]
    public void Logic_OffersGateActions()
    {
        var menu = new CanvasContextMenuViewModel();

        menu.Refresh(Workspace.Logic, GateVm());
        menu.ShowsShowInLayout.ShouldBeTrue();
        menu.ShowsEditGate.ShouldBeTrue();
        menu.ShowsMakeGate.ShouldBeFalse();

        menu.Refresh(Workspace.Logic, Group());
        menu.ShowsEditGate.ShouldBeFalse();
        menu.ShowsMakeGate.ShouldBeTrue("a group that is not yet a gate can become one");
    }

    [Theory]
    [InlineData(Workspace.Layout)]
    [InlineData(Workspace.Optics)]
    [InlineData(Workspace.System)]
    public void ShowInLayout_IsALogicAction(Workspace workspace)
    {
        var menu = new CanvasContextMenuViewModel();

        menu.Refresh(workspace, GateVm());

        menu.ShowsShowInLayout.ShouldBeFalse();
    }

    [Fact]
    public void MainViewModel_RefreshesTheMenu_OnSelectionAndWorkspace()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var part = vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide());

        vm.CanvasInteraction.SelectedComponent = part;
        vm.CanvasContextMenu.ShowsEditComponent.ShouldBeTrue();

        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.CanvasContextMenu.ShowsEditComponent.ShouldBeFalse();
        vm.CanvasContextMenu.ShowsShowInLayout.ShouldBeTrue();

        vm.ShowInLayoutCommand.Execute(null);
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
    }
}
