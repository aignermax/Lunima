using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>
/// What a workspace switch changes around the canvas: the Truth Table / Make gate section
/// shows only in Logic and System (and only for exactly one selected group), the Probe tool
/// only in Layout and Optics (the edit tools everywhere), the left rail follows the
/// workspace, the Optics quick section appears only in Optics, and the Chiplets panel
/// selects and centers a chiplet.
/// </summary>
[Collection("LocalizationSingleton")]
public class WorkspaceContextTests
{
    private static (MainViewModel vm, ComponentGroup group) WithSelectedGroup()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        var groupVm = vm.Canvas.AddComponent(group);
        vm.Canvas.Selection.SelectSingle(groupVm);
        vm.Canvas.SelectedComponent = groupVm;
        vm.RightPanel.TruthTable.ConfigureForSelection(groupVm, vm.Canvas);
        vm.RightPanel.TruthTable.IsGroupSelected.ShouldBeTrue();
        return (vm, group);
    }

    [Theory]
    [InlineData(Workspace.Layout, false)]
    [InlineData(Workspace.Optics, false)]
    [InlineData(Workspace.Logic, true)]
    [InlineData(Workspace.System, true)]
    public void GateSection_ShowsOnlyInLogicAndSystem(Workspace workspace, bool visible)
    {
        var (vm, _) = WithSelectedGroup();

        vm.WorkspaceSwitch.Current = workspace;

        vm.IsGateSectionVisible.ShouldBe(visible);
    }

    [Fact]
    public void GateSection_StaysHidden_WithoutASingleSelectedGroup()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        vm.IsGateSectionVisible.ShouldBeFalse();
    }

    [Fact]
    public void GateSection_RaisesChange_WhenTheWorkspaceSwitches()
    {
        var (vm, _) = WithSelectedGroup();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.WorkspaceSwitch.Current = Workspace.Logic;

        raised.ShouldContain(nameof(MainViewModel.IsGateSectionVisible));
    }

    [Fact]
    public void MakeGate_FromLayout_MovesToLogic_SoTheStepsAreVisible()
    {
        var (vm, _) = WithSelectedGroup();
        vm.WorkspaceSwitch.Current = Workspace.Layout;

        vm.MakeGateCommand.Execute(null);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Logic);
        vm.IsGateSectionVisible.ShouldBeTrue();
        vm.RightPanel.MakeGate.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void MakeGate_InSystem_StaysInSystem()
    {
        var (vm, _) = WithSelectedGroup();
        vm.WorkspaceSwitch.Current = Workspace.System;

        vm.MakeGateCommand.Execute(null);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.System);
    }

    [Theory]
    [InlineData(Workspace.Layout, true)]
    [InlineData(Workspace.Optics, true)]
    [InlineData(Workspace.Logic, false)]
    [InlineData(Workspace.System, false)]
    public void ProbeTool_ShowsOnlyInLayoutAndOptics(Workspace workspace, bool visible)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();

        vm.WorkspaceSwitch.Current = workspace;

        vm.WorkspaceSwitch.ShowsProbeTool.ShouldBe(visible);
    }

    [Fact]
    public void LeavingProbeWorkspaces_DropsBackToSelect_ButKeepsOtherEditModes()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Optics;
        vm.SetProbeModeCommand.Execute(null);

        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.Select);

        vm.SetConnectModeCommand.Execute(null);
        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.Connect, "edit tools are never taken away");
    }

    [Fact]
    public void LeftRail_FollowsTheWorkspace_AndRemembersPerWorkspace()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.LeftRail.ActiveSection.ShouldBe(LeftRailSection.Library);

        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.LeftRail.ActiveSection.ShouldBe(LeftRailSection.Gates);
        vm.LeftRail.ShowHierarchyCommand.Execute(null);

        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.LeftRail.ActiveSection.ShouldBe(LeftRailSection.Chiplets);

        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.LeftRail.ActiveSection.ShouldBe(LeftRailSection.Hierarchy, "the user's choice in Logic is kept");
    }

    [Fact]
    public void InstrumentLibrary_IsShownOnlyInOptics()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();

        vm.WorkspaceSwitch.Current = Workspace.Optics;
        vm.IsInstrumentLibrary.ShouldBeTrue();
        vm.IsFullLibrary.ShouldBeFalse();

        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.IsInstrumentLibrary.ShouldBeFalse();
        vm.IsFullLibrary.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChipletsPanel_SingleChipListsItsDie_NotLogicGroups(int gateCount)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        for (int i = 0; i < gateCount; i++)
            vm.Canvas.AddComponent(LogicGateFixtureFactory.CreateCombinerGroup());

        vm.LeftRail.ShowChipletsCommand.Execute(null);

        var row = vm.SystemOverview.Chiplets.ShouldHaveSingleItem();
        row.Name.ShouldBe("Die 1");
        row.IsDie.ShouldBeTrue();
        vm.SystemOverview.IsAssembly.ShouldBeFalse();
        vm.SystemOverview.ActivateChipletCommand.Execute(row);
        vm.Canvas.ChipDie.IsSelected.ShouldBeTrue();
        vm.Canvas.Components.Count.ShouldBe(gateCount, "viewing the chip must not convert or mutate the design");
    }

    [Fact]
    public void ChipletsPanel_AfterCreationAndUndo_TracksActualDies()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.Canvas.AddComponent(LogicGateFixtureFactory.CreateCombinerGroup());
        vm.NewDie.Prepare();
        vm.NewDie.CreateCommand.Execute(null);
        vm.RefreshSystemOverview();
        vm.SystemOverview.Chiplets.Count.ShouldBe(2);
        vm.SystemOverview.Chiplets.ShouldAllBe(row => row.IsDie);
        vm.SystemOverview.IsAssembly.ShouldBeTrue();
        vm.CommandManager.Undo();
        vm.RefreshSystemOverview();
        vm.SystemOverview.Chiplets.ShouldHaveSingleItem().Name.ShouldBe("Die 1");
        vm.SystemOverview.IsAssembly.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ChipletsPanel_InsideLogicGroupStillListsRootDies(bool assembly, int expectedDies)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        vm.Canvas.AddComponent(group);
        if (assembly)
        {
            vm.NewDie.Prepare();
            vm.NewDie.CreateCommand.Execute(null);
        }
        vm.Canvas.EnterGroupEditMode(group);
        vm.SystemOverview.Chiplets.Clear();
        vm.RefreshSystemOverview();
        vm.SystemOverview.Chiplets.Count.ShouldBe(expectedDies);
        var row = vm.SystemOverview.Chiplets.First();
        row.Name.ShouldBe("Die 1");
        vm.SystemOverview.ActivateChipletCommand.Execute(row);
        vm.Canvas.IsInGroupEditMode.ShouldBeFalse();
        vm.Canvas.SelectedComponent!.Component.ShouldBeSameAs(row.Group);
    }
}
