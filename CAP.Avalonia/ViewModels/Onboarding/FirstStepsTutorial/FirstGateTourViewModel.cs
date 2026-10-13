using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>
/// Step engine for the "Your first gate" tour: the gate-kit example (two input
/// waveguides, a 2x2 MMI combiner, a 135° phase-shifter bias, a recombining MMI and
/// the output, pre-placed and pre-wired but not grouped) is loaded by the Home entry
/// point; the steps then observe the canvas, the Make-gate steps and the Logic panel —
/// group, Make gate, simulate, pick NAND (automatic threshold), save, build in Logic —
/// and advance as each task is done. Plain observable state, so the whole tour runs
/// headlessly in unit tests.
/// </summary>
public partial class FirstGateTourViewModel : ObservableObject
{
    /// <summary>File name of the shipped gate-kit example the tour opens.</summary>
    public const string GateKitExampleFileName = "Logic Gate - Build your first gate.lun";

    private readonly DesignCanvasViewModel _canvas;
    private readonly MakeGateViewModel _makeGate;
    private readonly LogicPanelViewModel _logic;

    /// <summary>True while the tour card is shown and the engine observes the app.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Zero-based index of the step the user is asked to perform.</summary>
    [ObservableProperty]
    private int _currentStepIndex;

    /// <summary>True once the final step was completed.</summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>Builds the seven steps against the canvas, the Make-gate steps and the Logic panel.</summary>
    public FirstGateTourViewModel(DesignCanvasViewModel canvas, MakeGateViewModel makeGate, LogicPanelViewModel logic)
    {
        _canvas = canvas;
        _makeGate = makeGate;
        _logic = logic;
        Steps = new List<TutorialStep>
        {
            new("FirstGateTour.Step1Title", "FirstGateTour.Step1Body", () => false),
            new("FirstGateTour.Step2Title", "FirstGateTour.Step2Body", HasGroup),
            new("FirstGateTour.Step3Title", "FirstGateTour.Step3Body", () => _makeGate.IsActive, "MakeGateButton"),
            new("FirstGateTour.Step4Title", "FirstGateTour.Step4Body", () => _makeGate.HasResult, "MakeGateSimulateButton"),
            new("FirstGateTour.Step5Title", "FirstGateTour.Step5Body", IsNandWithCleanThreshold, "MakeGateFunctionPicker"),
            new("FirstGateTour.Step6Title", "FirstGateTour.Step6Body", () => _makeGate.IsSaved, "MakeGateSaveButton"),
            new("FirstGateTour.Step7Title", "FirstGateTour.Step7Body", () => _logic.HasNetwork, "MakeGateOpenLogicButton"),
        };
    }

    /// <summary>The ordered steps of the tour.</summary>
    public IReadOnlyList<TutorialStep> Steps { get; }

    /// <summary>The step the user is currently asked to perform.</summary>
    public TutorialStep CurrentStep => Steps[CurrentStepIndex];

    /// <summary>Localized title of the current step.</summary>
    public string CurrentTitle => LocalizationService.Instance.Translate(CurrentStep.TitleKey);

    /// <summary>Localized body of the current step.</summary>
    public string CurrentBody => LocalizationService.Instance.Translate(CurrentStep.BodyKey);

    /// <summary><c>x:Name</c> of the control the current step spotlights, or null for a floating card.</summary>
    public string? CurrentTargetName => CurrentStep.TargetName;

    /// <summary>Localized position label, e.g. "Step 2/7".</summary>
    public string ProgressText => string.Format(
        CultureInfo.InvariantCulture, "{0} {1}/{2}",
        LocalizationService.Instance.Translate("Tutorial.Step"),
        CurrentStepIndex + 1, Steps.Count);

    /// <summary>Starts the tour at the first step and begins observing.</summary>
    public void Start()
    {
        Detach();
        CurrentStepIndex = 0;
        IsCompleted = false;
        IsActive = true;
        _canvas.Components.CollectionChanged += OnCanvasChanged;
        _makeGate.PropertyChanged += OnObservedPropertyChanged;
        _logic.PropertyChanged += OnObservedPropertyChanged;
        EvaluateCurrentStep();
    }

    /// <summary>Advances manually; on the last step the tour completes.</summary>
    [RelayCommand]
    public void Next()
    {
        if (!IsActive)
            return;
        Advance();
        EvaluateCurrentStep();
    }

    /// <summary>Exits the tour without completing it.</summary>
    [RelayCommand]
    public void Skip()
    {
        IsActive = false;
        Detach();
    }

    private bool HasGroup() => _canvas.Components.Any(c => c.Component is ComponentGroup);

    private bool IsNandWithCleanThreshold() =>
        _makeGate.SelectedFunction.Function == GateFunction.Nand && _makeGate.MatchesFunction;

    private void OnCanvasChanged(object? sender, NotifyCollectionChangedEventArgs e) => EvaluateCurrentStep();

    private void OnObservedPropertyChanged(object? sender, PropertyChangedEventArgs e) => EvaluateCurrentStep();

    /// <summary>Advances past every already-satisfied step, so a state reached early cannot stall the tour.</summary>
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
        _canvas.Components.CollectionChanged -= OnCanvasChanged;
        _makeGate.PropertyChanged -= OnObservedPropertyChanged;
        _logic.PropertyChanged -= OnObservedPropertyChanged;
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(ProgressText));
    }
}
