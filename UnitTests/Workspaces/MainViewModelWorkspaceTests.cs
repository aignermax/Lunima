using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Services.Localization;
using Moq;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>
/// Run dispatches on the workspace, Run no longer toggles the overlay (the eye button and P
/// do), the workspace persists in the .lun file, and the System overview lists chiplets.
/// </summary>
[Collection("LocalizationSingleton")]
public class MainViewModelWorkspaceTests
{
    [Fact]
    public async Task Run_InLayout_RunsDesignChecks_AndOpensChecksTab()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Layout;

        await vm.RunWorkspaceCommand.ExecuteAsync(null);

        vm.BottomPanel.Analysis.IsVisible.ShouldBeTrue();
        vm.BottomPanel.Analysis.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Checks);
        vm.RightPanel.DesignValidation.StatusText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Run_InLogic_WithoutGates_OpensLogicTab_AndExplainsWhy()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        await vm.RunWorkspaceCommand.ExecuteAsync(null);

        vm.BottomPanel.Analysis.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Logic);
        vm.RightPanel.Logic.HasNetwork.ShouldBeFalse();
        vm.RightPanel.Logic.StatusText.ShouldContain("truth-table");
    }

    [Fact]
    public async Task Run_InLogic_OnGateExample_BuildsTheNetwork()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate NOT-NAND.lun");
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Logic);

        await vm.RunWorkspaceCommand.ExecuteAsync(null);

        vm.RightPanel.Logic.HasNetwork.ShouldBeTrue(vm.RightPanel.Logic.StatusText);
    }

    [Fact]
    public async Task RunSimulation_WhileOverlayShows_DoesNotToggleItOff()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.Canvas.ShowPowerFlow = true;

        await vm.RunSimulationCommand.ExecuteAsync(null);

        vm.Canvas.ShowPowerFlow.ShouldBeTrue("Run always runs; hiding the overlay is the eye button's job");
        vm.StatusText.ShouldNotBe(LocalizationService.Instance.Translate("Status.SimulationOverlayOff"));
    }

    [Fact]
    public async Task TogglePowerOverlay_HidesAVisibleOverlay()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.Canvas.ShowPowerFlow = true;

        await vm.TogglePowerOverlayCommand.ExecuteAsync(null);

        vm.Canvas.ShowPowerFlow.ShouldBeFalse();
    }

    [Fact]
    public async Task Run_InSystem_OnTwoChipletExample_ShowsChipletsLinks_AndOverlay()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Two Chiplets - Edge-Coupler Link.lun");
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.System);

        await vm.RunWorkspaceCommand.ExecuteAsync(null);

        vm.SystemOverview.Chiplets.Count.ShouldBeGreaterThanOrEqualTo(2);
        vm.SystemOverview.Links.ShouldNotBeEmpty();
        vm.Canvas.ShowPowerFlow.ShouldBeTrue(vm.StatusText);
        vm.BottomPanel.Analysis.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Checks);
    }

    [Fact]
    public async Task Workspace_IsSavedInTheLunFile_AndRestoredOnLoad()
    {
        var file = Path.Combine(Path.GetTempPath(), $"workspace_{Guid.NewGuid():N}.lun");
        try
        {
            var saveVm = MainViewModelTestHelper.CreateMainViewModel();
            saveVm.WorkspaceSwitch.Current = Workspace.System;
            saveVm.FileDialogService = DialogReturning(file);
            await saveVm.FileOperations.SaveDesignAsCommand.ExecuteAsync(null);
            File.ReadAllText(file).ShouldContain("\"Workspace\": \"System\"");

            var loadVm = MainViewModelTestHelper.CreateMainViewModel();
            (await loadVm.FileOperations.LoadDesignFromPathAsync(file)).ShouldBeTrue();

            loadVm.WorkspaceSwitch.Current.ShouldBe(Workspace.System,
                "the stored workspace wins over the content default (an empty design would be Layout)");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task NewProject_ResetsToLayout()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        (await vm.FileOperations.TryNewProjectAsync()).ShouldBeTrue();

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
    }

    [Fact]
    public void PlaceLightSource_ArmsPlacementOfALightSourceCoupler()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var coupler = new ComponentTemplate { Name = "Grating Coupler TE 1550", PdkSource = "Built-in" };
        vm.LeftPanel.AllTemplates.Add(new ComponentTemplate { Name = "Straight", PdkSource = "Built-in" });
        vm.LeftPanel.AllTemplates.Add(coupler);

        vm.BottomPanel.Analysis.Readiness.PlaceLightSourceCommand.Execute(null);

        vm.CanvasInteraction.SelectedTemplate.ShouldNotBeNull();
        CAP_Core.Components.ComponentHelpers.LightSourceClassifier
            .IsLightInjectingCoupler(vm.CanvasInteraction.SelectedTemplate!.Name).ShouldBeTrue();
    }

    private static IFileDialogService DialogReturning(string path)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(d => d.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(path);
        return dialog.Object;
    }
}
