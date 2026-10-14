using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;

namespace UnitTests.Workspaces;

/// <summary>
/// The L key's meaning per workspace: the Run label names the next action (build vs. step
/// in Logic), and the first few L presses per action show a short, self-hiding hint.
/// </summary>
[Collection("LocalizationSingleton")]
public class RunShortcutHintTests
{
    [Fact]
    public async Task Hint_ShowsTheFirstFewTimesPerAction_ThenStaysQuiet()
    {
        var hint = new RunShortcutHintViewModel(delay: async (_, _) => await Task.Yield());

        for (var i = 0; i < RunShortcutHintViewModel.MaxShowsPerAction; i++)
        {
            hint.TryShow("Workspace.Action.Checks").ShouldBeTrue();
            hint.IsVisible.ShouldBeTrue();
            await hint.PendingHide;
            hint.IsVisible.ShouldBeFalse("the hint hides itself");
        }

        hint.TryShow("Workspace.Action.Checks").ShouldBeFalse();
        hint.TryShow("Workspace.Action.LogicStep").ShouldBeTrue("another action is announced on its own count");
    }

    [Fact]
    public void Hint_NamesTheAction()
    {
        var loc = LocalizationService.Instance;
        var previous = loc.ActiveLanguageCode;
        loc.SetLanguage("en");
        try
        {
            var hint = new RunShortcutHintViewModel(delay: (_, token) => Task.Delay(Timeout.Infinite, token));

            hint.TryShow("Workspace.Action.Checks");

            hint.Text.ShouldBe("L — Design checks");
        }
        finally
        {
            loc.SetLanguage(previous);
        }
    }

    [Theory]
    [InlineData(Workspace.Layout, "Workspace.Action.Checks")]
    [InlineData(Workspace.Optics, "Workspace.Action.SimulationOn")]
    [InlineData(Workspace.Logic, "Workspace.Action.BuildLogic")]
    [InlineData(Workspace.System, "Workspace.Action.RunSystem")]
    public void RunActionKey_NamesWhatRunDoesInTheWorkspace(Workspace workspace, string expected)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = workspace;

        vm.RunActionKey().ShouldBe(expected);
    }

    [Fact]
    public void RunActionKey_InOpticsWithSimulationOn_IsTurningItOff()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Optics;
        vm.Canvas.ShowPowerFlow = true;

        vm.RunActionKey().ShouldBe("Workspace.Action.SimulationOff");
    }

    [Fact]
    public async Task LogicRunLabel_SwitchesFromBuildToStep_OnceTheCounterIsBuilt()
    {
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(
            Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Counter 2-bit.lun"));
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        vm.RunActionKey().ShouldBe("Workspace.Action.BuildLogic");
        var buildLabel = vm.WorkspaceSwitch.RunLabel;

        await vm.RightPanel.Logic.BuildNetworkCommand.ExecuteAsync(null);

        vm.RunActionKey().ShouldBe("Workspace.Action.LogicStep");
        vm.WorkspaceSwitch.CanStepLogicClock.ShouldBeTrue();
        vm.WorkspaceSwitch.RunLabel.ShouldNotBe(buildLabel, "the label names the step, not the build");
    }

    [Fact]
    public async Task ShortcutRun_ShowsTheHint_AndRunsTheWorkspaceAction()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Layout;

        var run = vm.RunWorkspaceFromShortcutCommand.ExecuteAsync(null);
        vm.RunHint.IsVisible.ShouldBeTrue("the hint shows as the action starts");
        await run;

        vm.RunHint.Text.ShouldNotBeNullOrWhiteSpace();
        vm.BottomPanel.Analysis.IsVisible.ShouldBeTrue("the design checks ran exactly as from the Run button");
    }
}
