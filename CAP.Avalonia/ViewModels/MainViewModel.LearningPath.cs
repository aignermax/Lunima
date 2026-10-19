using System.ComponentModel;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Onboarding.LearningPath;

namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel
{
    public LearningPathViewModel LearningPath { get; private set; } = null!;
    public ManufacturingTourViewModel ManufacturingTour { get; private set; } = null!;
    private int _learningNavigationVersion;

    private void InitializeLearningPath(UserPreferencesService preferences)
    {
        LearningPath = new LearningPathViewModel(preferences);
        Home.LearningPath = LearningPath;
        ManufacturingTour = new ManufacturingTourViewModel(FileOperations.GdsExport)
        {
            ActiveProcessProvider = () => FileOperations.ActiveProcess,
            ShowProcessDetails = () => LeftRail.ShowPdksCommand.Execute(null)
        };
        Home.ManufacturingTourRequested = StartManufacturingTourAsync;
        LearningPath.CloseRequested = () => { _learningNavigationVersion++; StopLearningChapters(); };
        LearningPath.StartChapterRequested = number => number switch
        {
            1 => StartVisualTutorialAsync(), 2 => StartFirstGateTourAsync(), 3 => StartWatchComputeTourAsync(),
            4 => StartStoreNumberTourAsync(), 5 => StartRunProgramTourAsync(), 6 => StartConnectChipletsTourAsync(),
            7 => StartManufacturingTourAsync(), _ => Task.CompletedTask
        };
        ObserveChapter(Tutorial, 1, () => Tutorial.IsActive, () => Tutorial.IsCompleted);
        ObserveChapter(FirstGateTour, 2, () => FirstGateTour.IsActive, () => FirstGateTour.IsCompleted);
        ObserveChapter(WatchTour, 3, () => WatchTour.IsActive, () => WatchTour.IsCompleted);
        ObserveChapter(StoreNumberTour, 4, () => StoreNumberTour.IsActive, () => StoreNumberTour.IsCompleted);
        ObserveChapter(RunProgramTour, 5, () => RunProgramTour.IsActive, () => RunProgramTour.IsCompleted);
        ObserveChapter(ConnectChipletsTour, 6, () => ConnectChipletsTour.IsActive, () => ConnectChipletsTour.IsCompleted);
        ObserveChapter(ManufacturingTour, 7, () => ManufacturingTour.IsActive, () => ManufacturingTour.IsCompleted);
    }

    private void ObserveChapter(INotifyPropertyChanged chapter, int number, Func<bool> active, Func<bool> completed)
    {
        chapter.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == "IsActive" && active()) LearningPath.Activate(number);
            if (change.PropertyName == "IsCompleted" && completed()) LearningPath.Complete(number);
            if (change.PropertyName == "IsActive" && !active() && LearningPath.CurrentNumber == number) LearningPath.Pause();
        };
    }

    private async Task StartManufacturingTourAsync()
    {
        var navigation = _learningNavigationVersion;
        var example = Home.Examples.FirstOrDefault(item => Path.GetFileName(item.FilePath) == ManufacturingTourViewModel.ExampleFileName);
        if (example == null || !await FileOperations.OpenDesignAsCopyAsync(example.FilePath, () => navigation == _learningNavigationVersion)) return;
        if (navigation != _learningNavigationVersion) return;
        WorkspaceSwitch.Current = Workspaces.Workspace.Layout;
        ManufacturingTour.Start();
        FitStartedLearningJourney(() => ManufacturingTour.IsActive);
    }
}
