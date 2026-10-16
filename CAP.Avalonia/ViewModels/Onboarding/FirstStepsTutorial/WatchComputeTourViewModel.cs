using System.ComponentModel;
using CAP.Avalonia.ViewModels.Workspaces;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>
/// Step engine for the "Watch it compute" guided tour (issue #1143, slice 2 of
/// #769): the Counter example is loaded by the Home entry point, then an ordered
/// list of steps observes the live Logic panel — network built, clock stepped,
/// Run started and stopped — and advances automatically as each task is
/// completed. Plain observable state with no view dependency, so the whole tour
/// can be driven headlessly from unit tests against a real
/// <see cref="LogicPanelViewModel"/>.
/// </summary>
public partial class WatchComputeTourViewModel : ObservableObject
{
    /// <summary>File name of the shipped example the tour opens.</summary>
    public const string CounterExampleFileName = "Logic Gate Counter 2-bit.lun";

    private WorkspaceViewModel? _workspace;

    public void AttachWorkspace(WorkspaceViewModel workspace)
    {
        Detach();
        _workspace = workspace;
        if (IsActive) { Observe(); RefreshContext(); }
    }

    public bool NeedsControlsRecovery => IsActive && !IsClosing &&
        (_workspace is { IsLogic: false } || !_dock.IsVisible || _dock.SelectedTabIndex != 7);
    public bool IsClosing => CurrentStepIndex == Steps.Count - 1;
    public string CurrentCue => LocalizationService.Instance.Translate(NeedsControlsRecovery
        ? "WatchTour.ReturnToControls" : CurrentStepIndex switch
        {
            0 => "LogicPanel.Build", 1 => "LogicPanel.StepClock", 2 => "LogicPanel.Run",
            3 => "LogicPanel.RunStop", _ => "WatchTour.Step5Title"
        });

    [RelayCommand]
    private void ShowControls()
    {
        if (!IsActive) return;
        if (_workspace != null) _workspace.Current = Workspace.Logic;
        _dock.OpenLogic();
        RefreshContext();
    }

    private void Observe()
    {
        _logic.PropertyChanged += OnLogicPropertyChanged;
        _dock.PropertyChanged += OnContextChanged;
        if (_workspace != null) _workspace.PropertyChanged += OnContextChanged;
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Current)
            or nameof(AnalysisDockViewModel.IsVisible) or nameof(AnalysisDockViewModel.SelectedTabIndex))
            RefreshContext();
    }

    private void RefreshContext()
    {
        // Leaving the dock can stop its clock as part of view teardown. That is
        // not the requested Stop action; ask for Run again after returning.
        if (NeedsControlsRecovery && CurrentStepIndex == 3)
        {
            _sawRunStarted = false;
            CurrentStepIndex = 2;
        }
        OnPropertyChanged(nameof(NeedsControlsRecovery));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(CurrentCue));
        if (!NeedsControlsRecovery)
        {
            _sawRunStarted |= _logic.IsRunning;
            EvaluateCurrentStep();
        }
    }

    private readonly LogicPanelViewModel _logic;
    private readonly AnalysisDockViewModel _dock;

    /// <summary>True once Run was pressed — the stop step completes on the falling edge of IsRunning, not its level.</summary>
    private bool _sawRunStarted;

    /// <summary>True while the tour overlay is shown and the engine is observing the Logic panel.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Zero-based index of the step the user is currently asked to perform.</summary>
    [ObservableProperty]
    private int _currentStepIndex;

    /// <summary>True once the final step was completed (or Next was pressed on it).</summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>
    /// Builds the tour steps against the given Logic panel: build the network,
    /// step the clock once, run the auto-clock, stop it again, closing words.
    /// The Logic panel lives in the analysis dock (#1183), so starting the tour
    /// opens the dock on the Logic tab to keep the panel the steps act on visible.
    /// </summary>
    public WatchComputeTourViewModel(LogicPanelViewModel logic, AnalysisDockViewModel dock)
    {
        _logic = logic;
        _dock = dock;
        LocalizationWeakSubscription.Subscribe(this, static (self, _) => self.OnCurrentStepIndexChanged(self.CurrentStepIndex));
        Steps = new List<TutorialStep>
        {
            new("WatchTour.Step1Title", "WatchTour.Step1Body", () => _logic.HasNetwork, "LogicBuildButton"),
            new("WatchTour.Step2Title", "WatchTour.Step2Body", () => _logic.ClockStepCount > 0, "LogicStepClockButton"),
            new("WatchTour.Step3Title", "WatchTour.Step3Body", () => _logic.IsRunning, "LogicRunStopButton"),
            new("WatchTour.Step4Title", "WatchTour.Step4Body", () => _sawRunStarted && !_logic.IsRunning, "LogicRunStopButton"),
            new("WatchTour.Step5Title", "WatchTour.Step5Body", () => false),
        };
    }

    /// <summary>The ordered steps of this tour chapter.</summary>
    public IReadOnlyList<TutorialStep> Steps { get; }

    /// <summary>The step the user is currently asked to perform.</summary>
    public TutorialStep CurrentStep => Steps[CurrentStepIndex];

    /// <summary>Localized title of the current step.</summary>
    public string CurrentTitle => LocalizationService.Instance.Translate(CurrentStep.TitleKey);

    /// <summary>Localized body text of the current step.</summary>
    public string CurrentBody => LocalizationService.Instance.Translate(CurrentStep.BodyKey);

    /// <summary><c>x:Name</c> of the control the current step spotlights, or null for a floating card.</summary>
    public string? CurrentTargetName => NeedsControlsRecovery ? null : CurrentStep.TargetName;

    /// <summary>Localized position label, e.g. "Step 2/5".</summary>
    public string ProgressText => string.Format(
        CultureInfo.InvariantCulture, "{0} {1}/{2}",
        LocalizationService.Instance.Translate("Tutorial.Step"),
        CurrentStepIndex + 1, Steps.Count);

    /// <summary>Starts the tour at the first step, opens the Logic tab and begins observing the Logic panel.</summary>
    public void Start()
    {
        Detach();
        CurrentStepIndex = 0;
        IsCompleted = false;
        _sawRunStarted = _logic.IsRunning;
        IsActive = true;
        ShowControls();
        Observe();
        EvaluateCurrentStep();
    }

    /// <summary>Acknowledges the closing summary; action stages cannot be skipped.</summary>
    [RelayCommand]
    public void Next()
    {
        if (IsActive && IsClosing)
            Advance();
    }

    /// <summary>Exits the tour without completing it.</summary>
    [RelayCommand]
    public void Skip()
    {
        IsActive = false;
        Detach();
    }

    private void OnLogicPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogicPanelViewModel.IsRunning) && _logic.IsRunning)
            _sawRunStarted = true;
        if (e.PropertyName is nameof(LogicPanelViewModel.HasNetwork)
            or nameof(LogicPanelViewModel.ClockStepCount)
            or nameof(LogicPanelViewModel.IsRunning))
        {
            EvaluateCurrentStep();
        }
    }

    /// <summary>
    /// Advances past every already-satisfied step, so a panel state that already
    /// fulfils a condition when the tour starts cannot stall it.
    /// </summary>
    private void EvaluateCurrentStep()
    {
        if (!IsActive) return;
        if (!_logic.HasNetwork)
        {
            _sawRunStarted = false;
            CurrentStepIndex = 0;
            return;
        }
        if (NeedsControlsRecovery) return;
        if (_logic.ClockStepCount == 0 && CurrentStepIndex > 1)
        {
            _sawRunStarted = _logic.IsRunning;
            CurrentStepIndex = 1;
        }
        while (IsActive && CurrentStep.IsCompleted())
            Advance();
    }

    private void Advance()
    {
        if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
            return;
        }

        IsCompleted = true;
        IsActive = false;
        Detach();
    }

    private void Detach()
    {
        _logic.PropertyChanged -= OnLogicPropertyChanged;
        _dock.PropertyChanged -= OnContextChanged;
        if (_workspace != null) _workspace.PropertyChanged -= OnContextChanged;
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsClosing));
        OnPropertyChanged(nameof(NeedsControlsRecovery));
        OnPropertyChanged(nameof(CurrentCue));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(ProgressText));
    }
}
