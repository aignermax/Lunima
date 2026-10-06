using System.ComponentModel;
using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>
/// Step engine for the "Connect two chiplets" guided tour (issue #1288, slice 4
/// of #769): the Two-Chiplets edge-coupler example is loaded by the Home entry
/// point, then an ordered list of steps observes the live canvas and Design
/// Checks — simulation run, receiver chiplet moved off its facet, the gap
/// warning raised, the one-click "Align chiplet" fix applied — and advances
/// automatically as each task is completed. The move step offers a "do it for
/// me" button that shifts the receiver 5 µm along the facet axis through the
/// same undoable group move a drag-drop records. Plain observable state with no
/// view dependency, so the whole tour can be driven headlessly from unit tests
/// against a real <see cref="DesignCanvasViewModel"/> and
/// <see cref="DesignValidationViewModel"/>.
/// </summary>
public partial class ConnectChipletsTourViewModel : ObservableObject
{
    /// <summary>File name of the shipped example the tour opens.</summary>
    public const string ExampleFileName = "Two Chiplets - Edge-Coupler Link.lun";

    /// <summary>How far the "do it for me" button shifts the receiver chiplet along the facet axis.</summary>
    public const double MoveDistanceMicrometers = 5.0;

    /// <summary>Displacement below which the move step keeps waiting (filters out accidental nudges).</summary>
    internal const double MoveDetectionThresholdMicrometers = 1.0;

    /// <summary>Index of the step that asks the user to move the receiver chiplet.</summary>
    internal const int MoveStepIndex = 2;

    private readonly DesignCanvasViewModel _canvas;
    private readonly DesignValidationViewModel _validation;
    private readonly AnalysisDockViewModel _dock;
    private readonly ConnectChipletsReceiverTracker _receiverTracker;

    private bool _sawChipletIssue;

    /// <summary>True while the tour overlay is shown and the engine is observing canvas and Design Checks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMoveForMeButton))]
    private bool _isActive;

    /// <summary>Zero-based index of the step the user is currently asked to perform.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMoveForMeButton))]
    private int _currentStepIndex;

    /// <summary>True once the final step was completed (or Next was pressed on it).</summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>
    /// Builds the tour steps against the given canvas and Design Checks panel:
    /// meet the two chiplets, run the simulation, move the receiver chiplet off
    /// its facet, watch Design Checks flag the gap, click "Align chiplet",
    /// closing words. Steps anchor in the main window only — the canvas, the
    /// toolbar Run button and the Design Checks tab of the analysis dock.
    /// </summary>
    public ConnectChipletsTourViewModel(
        DesignCanvasViewModel canvas,
        DesignValidationViewModel validation,
        AnalysisDockViewModel dock,
        CommandManager commandManager)
    {
        _canvas = canvas;
        _validation = validation;
        _dock = dock;
        _receiverTracker = new ConnectChipletsReceiverTracker(canvas, commandManager);
        Steps = new List<TutorialStep>
        {
            new("ConnectChipletsTour.Step1Title", "ConnectChipletsTour.Step1Body", () => false, "DesignCanvasControl"),
            new("ConnectChipletsTour.Step2Title", "ConnectChipletsTour.Step2Body", () => _canvas.ShowPowerFlow, "RunSimulationButton"),
            new("ConnectChipletsTour.Step3Title", "ConnectChipletsTour.Step3Body", ReceiverMoved, "DesignCanvasControl"),
            new("ConnectChipletsTour.Step4Title", "ConnectChipletsTour.Step4Body", HasChipletIssue, "DesignChecksRunButton"),
            new("ConnectChipletsTour.Step5Title", "ConnectChipletsTour.Step5Body", () => _sawChipletIssue && !HasChipletIssue(), "AlignChipletButton"),
            new("ConnectChipletsTour.Step6Title", "ConnectChipletsTour.Step6Body", () => false),
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

    /// <summary>True while the move step is current — the card offers the "do it for me" button.</summary>
    public bool ShowMoveForMeButton => IsActive && CurrentStepIndex == MoveStepIndex;

    /// <summary>Localized position label, e.g. "Step 2/6".</summary>
    public string ProgressText => string.Format(
        CultureInfo.InvariantCulture, "{0} {1}/{2}",
        LocalizationService.Instance.Translate("Tutorial.Step"),
        CurrentStepIndex + 1, Steps.Count);

    /// <summary>Starts the tour at the first step and begins observing the canvas and Design Checks.</summary>
    public void Start()
    {
        Detach();
        CurrentStepIndex = 0;
        IsCompleted = false;
        _sawChipletIssue = false;
        IsActive = true;
        _canvas.PropertyChanged += OnCanvasPropertyChanged;
        _validation.PropertyChanged += OnValidationPropertyChanged;
        _receiverTracker.ReceiverPositionChanged += OnReceiverPositionChanged;
        _receiverTracker.ResetBaseline();
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

    /// <summary>
    /// "Do it for me" on the move step: shifts the receiver chiplet
    /// <see cref="MoveDistanceMicrometers"/> along the facet axis (away from its
    /// partner) through the same undoable group move a drag-drop records, so
    /// Ctrl+Z restores the aligned position exactly.
    /// </summary>
    [RelayCommand]
    public void MoveReceiverForMe()
    {
        if (!ShowMoveForMeButton)
            return;
        _receiverTracker.MoveAlongFacetAxis(MoveDistanceMicrometers);
        EvaluateCurrentStep();
    }

    private bool ReceiverMoved() =>
        _receiverTracker.DisplacementFromStart() > MoveDetectionThresholdMicrometers;

    private bool HasChipletIssue() =>
        _validation.Issues.Any(i => i.Type is DesignIssueType.ChipletInterfaceNotFacing
            or DesignIssueType.ChipletInterfaceLateralOffset
            or DesignIssueType.ChipletInterfaceOffEdge
            or DesignIssueType.ChipletInterfaceGapLoss);

    private void OnCanvasPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesignCanvasViewModel.ShowPowerFlow)
            or nameof(DesignCanvasViewModel.Connections))
        {
            EvaluateCurrentStep();
        }
    }

    private void OnValidationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesignValidationViewModel.HasIssues)
            or nameof(DesignValidationViewModel.StatusText))
        {
            EvaluateCurrentStep();
        }
    }

    private void OnReceiverPositionChanged(object? sender, EventArgs e) => EvaluateCurrentStep();

    /// <summary>
    /// Advances past every already-satisfied step, so a canvas state that
    /// already fulfils a condition when the tour starts cannot stall it.
    /// </summary>
    private void EvaluateCurrentStep()
    {
        if (HasChipletIssue())
            _sawChipletIssue = true;
        while (IsActive && CurrentStep.IsCompleted())
            Advance();
    }

    private void Advance()
    {
        if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
            if (Steps[CurrentStepIndex].TargetName == "DesignChecksRunButton")
                _dock.OpenChecks();
            return;
        }

        IsCompleted = true;
        IsActive = false;
        Detach();
    }

    private void Detach()
    {
        _canvas.PropertyChanged -= OnCanvasPropertyChanged;
        _validation.PropertyChanged -= OnValidationPropertyChanged;
        _receiverTracker.ReceiverPositionChanged -= OnReceiverPositionChanged;
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
