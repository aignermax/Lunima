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
        _resettingGuidance = true;
        try { Tutorial.Skip(); }
        finally { _resettingGuidance = false; }
        if (cameFromHome && Canvas.Components.Count == 0 && _guidancePreferences?.GetGuidedLearningEnabled() == true)
            StartVisualLearning();
    }

    private void StartVisualLearning()
    {
        Home.IsHomeVisible = false;
        WorkspaceSwitch.Current = Workspace.Layout;
        Tutorial.Start();
    }
}
