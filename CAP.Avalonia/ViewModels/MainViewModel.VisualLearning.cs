using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Workspaces;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;

namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _showLearningSuccess;
    private UserPreferencesService? _guidancePreferences;
    private bool _resettingGuidance;
    private IDisposable? _learningSuccessTimeout;

    private void InitializeVisualLearning(UserPreferencesService preferences)
    {
        _guidancePreferences = preferences;
        Home.ContinueWithoutProjectRequested = () =>
        {
            if (FileOperations.CurrentFilePath == null && Canvas.Components.Count == 0 &&
                preferences.GetGuidedLearningEnabled())
                StartVisualLearning();
        };
        Tutorial.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Tutorial.IsCompleted))
            {
                _learningSuccessTimeout?.Dispose();
                ShowLearningSuccess = Tutorial.IsCompleted;
                if (ShowLearningSuccess)
                    _learningSuccessTimeout = DispatcherTimer.RunOnce(() => ShowLearningSuccess = false, TimeSpan.FromSeconds(3));
            }
            if (change.PropertyName == nameof(Tutorial.IsActive) && !Tutorial.IsActive && !_resettingGuidance)
                preferences.SetGuidedLearningEnabled(false);
        };
    }

    private void UpdateGuidanceForOpenedProject(bool cameFromHome)
    {
        // A replaced document must never inherit pin references or progress from the previous one.
        StopLearningChapters();
        // File loads restore their own workspace, even when the saved design is empty.
        // Open-as-copy also still has its source path during ProjectOpened.
        if (cameFromHome && FileOperations.CurrentFilePath == null && Canvas.Components.Count == 0 &&
            _guidancePreferences?.GetGuidedLearningEnabled() == true)
            StartVisualLearning();
    }

    private void StartVisualLearning()
    {
        StopLearningChapters();
        Home.IsHomeVisible = false;
        WorkspaceSwitch.Current = Workspace.Layout;
        Tutorial.Start();
    }

    private void StopLearningChapters()
    {
        _resettingGuidance = true;
        try
        {
            Tutorial.Skip();
            FirstGateTour.Skip();
            WatchTour.Skip();
            RunProgramTour.Skip();
            ConnectChipletsTour.Skip();
            StoreNumberTour.Skip();
            ManufacturingTour?.Skip();
            LearningPath?.Pause();
            _learningSuccessTimeout?.Dispose();
            _learningSuccessTimeout = null;
            ShowLearningSuccess = false;
        }
        finally { _resettingGuidance = false; }
    }
}
