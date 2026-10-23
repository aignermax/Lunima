using System.ComponentModel;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Home;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Process;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Onboarding.LearningJourneys;

/// <summary>Prepares the optical tutorial's process and owns its guidance preference and success timer.</summary>
internal sealed class LearningGuidanceController : ObservableObject, IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly FileOperationsViewModel _files;
    private readonly ProcessRoutingContext _process;
    private readonly TutorialViewModel _tutorial;
    private readonly HomeViewModel _home;
    private readonly WorkspaceViewModel _workspace;
    private readonly UserPreferencesService _preferences;
    private readonly Action _stopChapters;
    private IDisposable? _timeout;
    private bool _resetting;
    private bool _showSuccess;

    public LearningGuidanceController(DesignCanvasViewModel canvas, FileOperationsViewModel files,
        ProcessRoutingContext process, TutorialViewModel tutorial, HomeViewModel home,
        WorkspaceViewModel workspace, UserPreferencesService preferences, Action stopChapters)
    {
        (_canvas, _files, _process, _tutorial, _home, _workspace, _preferences, _stopChapters) =
            (canvas, files, process, tutorial, home, workspace, preferences, stopChapters);
        tutorial.PropertyChanged += TutorialChanged;
        home.ContinueWithoutProjectRequested = ContinueWithoutProject;
    }

    public bool ShowSuccess { get => _showSuccess; set => SetProperty(ref _showSuccess, value); }
    public bool IsPreparing { get; private set; }
    public bool ChoosingNewProjectProcess { get; set; }
    public bool HasLearningProcess => _files.ActiveProcess is { IsPlayground: false } process
        && process.MemberPdkNames.Contains("Demo PDK")
        && _canvas.Surface.Dies.All(die => die.ProcessBinding?.MemberPdkNames.Contains("Demo PDK") == true);

    private bool PrepareProcess()
    {
        if (HasLearningProcess) return true;
        if (_canvas.Components.Count != 0 || _canvas.Surface.Dies.Count != 0
            || _files.CurrentFilePath != null || _files.HasUnsavedChanges) return false;
        var group = _process.BuildCatalog().FirstOrDefault(item => item.MemberPdkNames.Contains("Demo PDK"));
        if (group == null) return false;
        _files.SetActiveProcess(ActiveProcessSelection.ForGroup(group), markDirty: false);
        return true;
    }

    public async Task StartAsync(Func<bool> isCurrent)
    {
        if (IsPreparing) return;
        IsPreparing = true;
        try
        {
            if (!PrepareProcess())
            {
                if (_files.MessageBoxService is not { } dialogs) return;
                var loc = LocalizationService.Instance;
                var choice = await dialogs.ShowChoicePromptAsync(loc.Translate("Tutorial.Process.NewProjectBody"),
                    loc.Translate("Tutorial.Process.Title"), [loc.Translate("Tutorial.Process.Create"), loc.Translate("Tutorial.Process.Keep")]);
                if (choice != 0 || !isCurrent()) return;
                if (!await _files.TryNewProjectAsync(isCurrent) || !PrepareProcess()) return;
            }
            if (!isCurrent()) return;
            _preferences.SetGuidedLearningEnabled(true);
            Start();
        }
        finally { IsPreparing = false; }
    }

    public void AfterNewProject()
    {
        if (HasLearningProcess && _preferences.GetGuidedLearningEnabled()) Start();
    }

    public void ProjectOpened(bool cameFromHome)
    {
        Reset();
        if (!ChoosingNewProjectProcess && !IsPreparing && cameFromHome && _files.CurrentFilePath == null
            && _canvas.Components.Count == 0 && _preferences.GetGuidedLearningEnabled()) Start();
    }

    private void ContinueWithoutProject()
    {
        if (_files.CurrentFilePath == null && _canvas.Components.Count == 0 && _preferences.GetGuidedLearningEnabled()) Start();
    }

    private void Start()
    {
        if (!PrepareProcess()) return;
        Reset();
        _home.IsHomeVisible = false;
        _workspace.Current = Workspace.Layout;
        _tutorial.Start();
    }

    public void Reset()
    {
        _resetting = true;
        try
        {
            _stopChapters();
            _timeout?.Dispose();
            _timeout = null;
            ShowSuccess = false;
        }
        finally { _resetting = false; }
    }

    private void TutorialChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(TutorialViewModel.IsCompleted))
        {
            _timeout?.Dispose();
            ShowSuccess = _tutorial.IsCompleted;
            if (ShowSuccess) _timeout = DispatcherTimer.RunOnce(() => ShowSuccess = false, TimeSpan.FromSeconds(3));
        }
        if (change.PropertyName == nameof(TutorialViewModel.IsActive) && !_tutorial.IsActive && !_resetting)
            _preferences.SetGuidedLearningEnabled(false);
    }

    public void Dispose()
    {
        _tutorial.PropertyChanged -= TutorialChanged;
        _timeout?.Dispose();
        if (_home.ContinueWithoutProjectRequested == ContinueWithoutProject) _home.ContinueWithoutProjectRequested = null;
    }
}
