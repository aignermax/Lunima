using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class MainRetirementTests
{
    [Fact]
    public void Retirement_DetachesFileStatusProjection()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        main.Dispose();
        var title = main.WindowTitle;

        main.FileOperations.HasUnsavedChanges = true;

        main.WindowTitle.ShouldBe(title);
    }

    [Fact]
    public void Retirement_DetachesOwnedSelectionStatus()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        main.Dispose();

        main.Canvas.Selection.SelectedComponents.Add(new ComponentViewModel(TestComponentFactory.CreateStraightWaveGuide()));

        main.StatusBar.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void Retirement_DetachesWorkspaceProjection()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        main.Dispose();
        var rail = main.LeftRail.ActiveSection;

        main.WorkspaceSwitch.Current = Workspace.Logic;

        main.LeftRail.ActiveSection.ShouldBe(rail);
    }

    [Fact]
    public void Retirement_DetachesLiveSimulationEntryPoint()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();

        main.Dispose();

        main.Canvas.SimulationRequested.ShouldBeNull();
    }
}
