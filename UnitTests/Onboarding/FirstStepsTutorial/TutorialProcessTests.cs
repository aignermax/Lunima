using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP_Core.Components.Process;
using Moq;
using UnitTests.Helpers;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class TutorialProcessTests
{
    [AvaloniaFact]
    public async Task SwitchingProcess_PausesLessonInsteadOfOfferingUnavailableParts()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        await vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        Assert.True(vm.Tutorial.IsActive);
        var process = ActiveProcessSelection.ForGroup(vm.FileOperations.ProcessCatalogProvider!().First(g => !g.MemberPdkNames.Contains("Demo PDK")));
        vm.FileOperations.SetActiveProcess(process);
        Assert.False(vm.Tutorial.IsActive);
        Assert.True(vm.LearningPath.IsPaused);
        Assert.Same(process, vm.FileOperations.ActiveProcess);
    }

    [AvaloniaFact]
    public async Task LearningNewProject_ConfirmedStartsBlankDemoWithoutFreePicker()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground());
        vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        vm.FileOperations.HasUnsavedChanges = true;
        var dialogs = new Mock<IMessageBoxService>();
        dialogs.Setup(d => d.ShowChoicePromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).ReturnsAsync(0);
        dialogs.Setup(d => d.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(SavePromptResult.DontSave);
        vm.FileOperations.MessageBoxService = dialogs.Object;
        vm.ShowProcessSelectionAsync = _ => throw new InvalidOperationException("Learning must not show the free picker");
        await vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        Assert.True(vm.Tutorial.IsActive);
        Assert.Empty(vm.Canvas.Components);
        Assert.Contains("Demo PDK", vm.FileOperations.ActiveProcess!.MemberPdkNames);
    }

    [AvaloniaFact]
    public async Task PausingDuringLearningSavePrompt_PreservesOriginalDesign()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground());
        var part = vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        vm.FileOperations.HasUnsavedChanges = true;
        var pendingSave = new TaskCompletionSource<SavePromptResult>();
        var dialogs = new Mock<IMessageBoxService>();
        dialogs.Setup(d => d.ShowChoicePromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).ReturnsAsync(0);
        dialogs.Setup(d => d.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(pendingSave.Task);
        vm.FileOperations.MessageBoxService = dialogs.Object;
        var starting = vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        vm.LearningPath.CloseCommand.Execute(null);
        pendingSave.SetResult(SavePromptResult.DontSave);
        await starting;
        Assert.False(vm.Tutorial.IsActive);
        Assert.Contains(part, vm.Canvas.Components);
        Assert.True(vm.FileOperations.ActiveProcess!.IsPlayground);
    }

    [AvaloniaFact]
    public async Task FreshLearning_SelectsDemoBeforeStartupPicker_AndOffersSourceAndReceiver()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var prompts = 0;
        vm.ShowProcessSelectionAsync = _ => { prompts++; return Task.FromResult<ActiveProcessSelection?>(ActiveProcessSelection.Playground()); };
        await vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        await vm.PromptForInitialProcessAsync();
        Assert.Equal(0, prompts);
        Assert.Contains("Demo PDK", vm.FileOperations.ActiveProcess!.MemberPdkNames);
        Assert.True(vm.Tutorial.IsActive);
        Assert.NotNull(vm.Tutorial.CurrentTargetItem);
        Assert.Contains(vm.LeftPanel.FilteredTemplates, t => t.PdkSource == "Demo PDK" && t.Name.Contains("Straight"));
    }

    [AvaloniaFact]
    public async Task ExistingIncompatibleProject_CanDeclineLearningWithoutChangingAnything()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var process = ActiveProcessSelection.ForGroup(vm.FileOperations.ProcessCatalogProvider!().First(g => !g.MemberPdkNames.Contains("Demo PDK")));
        vm.FileOperations.SetActiveProcess(process);
        var part = vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        var dialogs = new Mock<IMessageBoxService>();
        dialogs.Setup(d => d.ShowChoicePromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).ReturnsAsync(1);
        vm.FileOperations.MessageBoxService = dialogs.Object;
        await vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        Assert.False(vm.Tutorial.IsActive);
        Assert.Same(process, vm.FileOperations.ActiveProcess);
        Assert.Contains(part, vm.Canvas.Components);
        dialogs.Verify(d => d.ShowChoicePromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    [AvaloniaFact]
    public async Task OrdinaryNewProject_KeepsFreeProcessChoice_WithoutStartingIncompatibleTutorial()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var process = ActiveProcessSelection.ForGroup(vm.FileOperations.ProcessCatalogProvider!().First(g => !g.MemberPdkNames.Contains("Demo PDK")));
        vm.ShowProcessSelectionAsync = _ => Task.FromResult<ActiveProcessSelection?>(process);
        await vm.NewProjectCommand.ExecuteAsync(null);
        Assert.Same(process, vm.FileOperations.ActiveProcess);
        Assert.False(vm.Tutorial.IsActive);
    }

    [AvaloniaFact]
    public async Task LearningNewProject_SaveCancellationPreservesIncompatibleDesign()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground());
        var part = vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        vm.FileOperations.HasUnsavedChanges = true;
        var dialogs = new Mock<IMessageBoxService>();
        dialogs.Setup(d => d.ShowChoicePromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).ReturnsAsync(0);
        dialogs.Setup(d => d.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(SavePromptResult.Cancel);
        vm.FileOperations.MessageBoxService = dialogs.Object;
        await vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        Assert.False(vm.Tutorial.IsActive);
        Assert.True(vm.FileOperations.ActiveProcess!.IsPlayground);
        Assert.Contains(part, vm.Canvas.Components);
    }
}
