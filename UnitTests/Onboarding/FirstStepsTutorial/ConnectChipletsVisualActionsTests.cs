using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Workspaces;
using UnitTests.Helpers;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class ConnectChipletsVisualActionsTests
{
    [AvaloniaFact]
    public async Task RunStep_PointsToSystemUntilRunWouldActuallySimulate()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        await UnitTests.Integration.TwoChipletsExampleLoader.LoadAsync(vm.Canvas, CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial.ConnectChipletsTourViewModel.ExampleFileName);
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.ConnectChipletsTour.Start();
        vm.ConnectChipletsTour.Next();
        Assert.Equal("SystemWorkspaceButton", vm.ConnectChipletsTour.CurrentTargetName);

        vm.WorkspaceSwitch.Current = Workspace.System;
        Assert.Equal("RunSimulationButton", vm.ConnectChipletsTour.CurrentTargetName);
        vm.ConnectChipletsTour.Next();
        Assert.Equal(1, vm.ConnectChipletsTour.CurrentStepIndex);
        Assert.False(vm.ConnectChipletsTour.IsCompleted);
        vm.Canvas.ShowPowerFlow = true;
        Assert.Equal(2, vm.ConnectChipletsTour.CurrentStepIndex);
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        Assert.Equal("SystemWorkspaceButton", vm.ConnectChipletsTour.CurrentTargetName);
        vm.WorkspaceSwitch.Current = Workspace.System;
        Assert.Equal(2, vm.ConnectChipletsTour.CurrentStepIndex);
        vm.ConnectChipletsTour.Skip();
    }
}
