using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using UnitTests.Helpers;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class FirstUseGuidanceTests
{
    [AvaloniaFact]
    public void EmptyNewDesign_StartsGuidance_AndDismissalPersists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lunima-guide-{Guid.NewGuid():N}.json");
        try
        {
            var vm = MainViewModelTestHelper.CreateMainViewModel(preferencesService: new UserPreferencesService(path));
            Assert.False(vm.Tutorial.IsActive);
            vm.FileOperations.ProjectOpened!();
            Assert.True(vm.Tutorial.IsActive);
            vm.Tutorial.SkipCommand.Execute(null);
            Assert.False(vm.Tutorial.IsActive);
            var restarted = MainViewModelTestHelper.CreateMainViewModel(preferencesService: new UserPreferencesService(path));
            restarted.FileOperations.ProjectOpened!();
            Assert.False(restarted.Tutorial.IsActive);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [AvaloniaFact]
    public void OpeningExistingDesign_DoesNotStartFirstUseGuidanceOrRemoveParts()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var part = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        vm.Canvas.AddComponent(part);
        vm.FileOperations.ProjectOpened!();
        Assert.False(vm.Tutorial.IsActive);
        Assert.Contains(vm.Canvas.Components, c => c.Component == part);
    }

    [AvaloniaFact]
    public async Task ExplicitResume_PreservesExistingWork()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var part = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        vm.Canvas.AddComponent(part);
        await vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        Assert.True(vm.Tutorial.IsActive);
        Assert.False(vm.Home.IsHomeVisible);
        Assert.Contains(vm.Canvas.Components, c => c.Component == part);
    }
}
