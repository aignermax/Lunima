using System.ComponentModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>
/// Step engine for the "Run a program on your chip" guided tour (issue #1267,
/// slice 3 of #769): the 4-bit adder example is loaded by the Home entry point,
/// then an ordered list of steps observes the live Logic panel and the ISA
/// playground — network built, playground window opened, multiply sample picked,
/// photonic-ADD toggle on, a few photonic steps taken — and advances
/// automatically as each task is completed. Plain observable state with no view
/// dependency, so the whole tour can be driven headlessly from unit tests
/// against a real <see cref="LogicPanelViewModel"/> and
/// <see cref="IsaPlaygroundViewModel"/>.
/// </summary>
public partial class RunProgramTourViewModel : ObservableObject
{
    /// <summary>File name of the shipped example the tour opens.</summary>
    public const string AdderExampleFileName = "Logic Gate 4-Bit Adder.lun";

    /// <summary>File name of the ISA sample the tour asks the user to pick.</summary>
    public const string MultiplySampleFileName = "multiply-3x4.asm";

    /// <summary>How many photonic ADDs the user must step through before the tour moves on.</summary>
    internal const int PhotonicStepsToWatch = 3;

    /// <summary>First step whose card lives in the ISA playground window; earlier steps anchor in the main window.</summary>
    private const int FirstPlaygroundStepIndex = 2;

    private readonly LogicPanelViewModel _logic;
    private readonly AnalysisDockViewModel _dock;
    private readonly IsaPlaygroundViewModel _playground;

    private int _photonicAddCount;

    /// <summary>True while the tour overlay is shown and the engine is observing panel and playground.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCardInMainWindow))]
    [NotifyPropertyChangedFor(nameof(ShowCardInPlayground))]
    private bool _isActive;

    /// <summary>Zero-based index of the step the user is currently asked to perform.</summary>
    [ObservableProperty]
    private int _currentStepIndex;

    /// <summary>True once the final step was completed (or Next was pressed on it).</summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>True while the ISA playground window is open (reported by the window itself).</summary>
    [ObservableProperty]
    private bool _isPlaygroundOpen;

    /// <summary>
    /// Builds the tour steps against the given Logic panel and ISA playground:
    /// build the adder network, open the playground, pick the multiply sample,
    /// switch ADDs onto the photonic chip, step through them, closing words.
    /// Steps up to <see cref="FirstPlaygroundStepIndex"/> anchor in the main
    /// window (Logic tab, Tools menu), the rest inside the playground window,
    /// so each window hosts its own overlay over the same engine.
    /// </summary>
    public RunProgramTourViewModel(
        LogicPanelViewModel logic,
        AnalysisDockViewModel dock,
        IsaPlaygroundViewModel playground)
    {
        _logic = logic;
        _dock = dock;
        _playground = playground;
        Steps = new List<TutorialStep>
        {
            new("RunProgramTour.Step1Title", "RunProgramTour.Step1Body", () => _logic.HasNetwork, "LogicBuildButton"),
            new("RunProgramTour.Step2Title", "RunProgramTour.Step2Body", () => IsPlaygroundOpen, "ToolsMenuButton"),
            new("RunProgramTour.Step3Title", "RunProgramTour.Step3Body",
                () => _playground.SelectedSample?.FileName == MultiplySampleFileName, "IsaSamplePicker"),
            new("RunProgramTour.Step4Title", "RunProgramTour.Step4Body", () => _playground.UsePhotonicAdder, "IsaPhotonicToggle"),
            new("RunProgramTour.Step5Title", "RunProgramTour.Step5Body", () => _photonicAddCount >= PhotonicStepsToWatch, "IsaStepButton"),
            new("RunProgramTour.Step6Title", "RunProgramTour.Step6Body", () => false),
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
    public string? CurrentTargetName => CurrentStep.TargetName;

    /// <summary>True when the tour card belongs to the main window's overlay (steps before the playground steps).</summary>
    public bool ShowCardInMainWindow => IsActive && CurrentStepIndex < FirstPlaygroundStepIndex;

    /// <summary>True when the tour card belongs to the ISA playground window's overlay.</summary>
    public bool ShowCardInPlayground => IsActive && CurrentStepIndex >= FirstPlaygroundStepIndex;

    /// <summary>Localized position label, e.g. "Step 2/6".</summary>
    public string ProgressText => string.Format(
        CultureInfo.InvariantCulture, "{0} {1}/{2}",
        LocalizationService.Instance.Translate("Tutorial.Step"),
        CurrentStepIndex + 1, Steps.Count);

    /// <summary>Starts the tour at the first step, opens the Logic tab and begins observing panel and playground.</summary>
    public void Start()
    {
        Detach();
        CurrentStepIndex = 0;
        IsCompleted = false;
        _photonicAddCount = 0;
        IsActive = true;
        _dock.OpenLogic();
        _logic.PropertyChanged += OnLogicPropertyChanged;
        _playground.PropertyChanged += OnPlaygroundPropertyChanged;
        EvaluateCurrentStep();
    }

    /// <summary>Advances to the next step manually; on the last step the tour completes.</summary>
    [RelayCommand]
    public void Next()
    {
        if (!IsActive)
            return;
        Advance();
    }

    /// <summary>Exits the tour without completing it.</summary>
    [RelayCommand]
    public void Skip()
    {
        IsActive = false;
        Detach();
    }

    /// <summary>Called by the playground window when it opens; completes the open-playground step.</summary>
    public void NotifyPlaygroundOpened()
    {
        IsPlaygroundOpen = true;
        EvaluateCurrentStep();
    }

    /// <summary>Called by the playground window when it closes.</summary>
    public void NotifyPlaygroundClosed() => IsPlaygroundOpen = false;

    private void OnLogicPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogicPanelViewModel.HasNetwork))
            EvaluateCurrentStep();
    }

    private void OnPlaygroundPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsaPlaygroundViewModel.PhotonicStatusText)
            && !string.IsNullOrEmpty(_playground.PhotonicStatusText))
        {
            _photonicAddCount++;
        }

        if (e.PropertyName is nameof(IsaPlaygroundViewModel.SelectedSample)
            or nameof(IsaPlaygroundViewModel.UsePhotonicAdder)
            or nameof(IsaPlaygroundViewModel.PhotonicStatusText))
        {
            EvaluateCurrentStep();
        }
    }

    /// <summary>
    /// Advances past every already-satisfied step, so a state that already
    /// fulfils a condition when the tour starts cannot stall it.
    /// </summary>
    private void EvaluateCurrentStep()
    {
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
        _playground.PropertyChanged -= OnPlaygroundPropertyChanged;
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ShowCardInMainWindow));
        OnPropertyChanged(nameof(ShowCardInPlayground));
    }
}
