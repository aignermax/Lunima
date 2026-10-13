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
        vm.LeftRail.ShowLibraryCommand.Execute(null);

        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.LeftRail.ActiveSection.ShouldBe(LeftRailSection.Chiplets);

        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.LeftRail.ActiveSection.ShouldBe(LeftRailSection.Library, "the user's choice in Logic is kept");
    }

    [Fact]
    public void SourcesQuickSection_IsVisibleOnlyInOptics()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.LeftPanel.FilteredTemplates.Add(new CAP.Avalonia.ViewModels.Library.ComponentTemplate { Name = "Grating Coupler TE 1550" });

        vm.WorkspaceSwitch.Current = Workspace.Optics;
        vm.IsSourcesQuickSectionVisible.ShouldBeTrue();

        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.IsSourcesQuickSectionVisible.ShouldBeFalse();
    }

    [Fact]
    public void ChipletsPanel_ListsTopLevelGroups_AndActivatingOneSelectsIt()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var chiplet = LogicGateFixtureFactory.CreateCombinerGroup();
        chiplet.GroupName = "Die A";
        vm.Canvas.AddComponent(chiplet);

        vm.LeftRail.ShowChipletsCommand.Execute(null);

        var row = vm.SystemOverview.Chiplets.ShouldHaveSingleItem();
        row.Name.ShouldBe("Die A");
        vm.SystemOverview.ActivateChipletCommand.Execute(row);
        vm.Canvas.SelectedComponent?.Component.ShouldBeSameAs(chiplet);
    }
}
