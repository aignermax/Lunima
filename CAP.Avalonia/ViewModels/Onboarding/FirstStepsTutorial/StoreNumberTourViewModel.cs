using System.ComponentModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>
/// Step engine for the "Store a number in light" guided tour (issue #1422, slice
/// 5 of #769): the RAM 2x4 example is loaded by the Home entry point, then an
/// ordered list of steps observes the live Logic panel — build the network, pick
/// address and data, commit the word on a clock edge, switch the address away
/// and back to prove the cell held its state — and advances automatically as
/// each task is completed. Plain observable state with no view dependency, so
/// the whole tour can be driven headlessly from unit tests against a real
/// <see cref="LogicPanelViewModel"/>. Steps bind only to the example's signal
/// names (A, LOAD, D0–D3, Q0–Q3), never to gate ids.
/// </summary>
public partial class StoreNumberTourViewModel : ObservableObject
{
    /// <summary>File name of the shipped example the tour opens.</summary>
    public const string RamExampleFileName = "Logic Gate RAM 2x4.lun";

    private const string AddressPin = "A";
    private const string LoadPin = "LOAD";
    private const string DataPinPrefix = "D";
    private const string OutputPinPrefix = "Q";
    private const int WordBitCount = 4;
    private const int ReadBackStepIndex = 3;

    private readonly LogicPanelViewModel _logic;
    private readonly AnalysisDockViewModel _dock;

    /// <summary>The word the user committed, latched when the read-back step begins.</summary>
    private int _storedWord;

    /// <summary>The address the word was stored under, latched together with <see cref="_storedWord"/>.</summary>
    private bool _storedAddress;

    /// <summary>True once the address was switched away from <see cref="_storedAddress"/> during read-back.</summary>
    private bool _addressLeft;

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
    /// pick a word and a number, store it on a clock edge, read it back after
    /// switching the address away, closing words. The Logic panel lives in the
    /// analysis dock (#1183), so starting the tour opens the dock on the Logic
    /// tab to keep the panel the steps act on visible.
    /// </summary>
    public StoreNumberTourViewModel(LogicPanelViewModel logic, AnalysisDockViewModel dock)
    {
        _logic = logic;
        _dock = dock;
        LocalizationWeakSubscription.Subscribe(this, static (self, _) => self.NotifyVisualState());
        Steps = new List<TutorialStep>
        {
            new("StoreNumberTour.Step1Title", "StoreNumberTour.Step1Body",
                () => _logic.HasNetwork, "LogicBuildButton"),
            new("StoreNumberTour.Step2Title", "StoreNumberTour.Step2Body",
                HasAnyDataBitOn, "LogicInputRows"),
            new("StoreNumberTour.Step3Title", "StoreNumberTour.Step3Body",
                () => false,
                "LogicStepClockButton"),
            new("StoreNumberTour.Step4Title", "StoreNumberTour.Step4Body",
                () => !ReadInput(LoadPin) && _addressLeft
                    && ReadInput(AddressPin) == _storedAddress && ReadOutputWord() == _storedWord,
                "LogicOutputRows"),
            new("StoreNumberTour.Step5Title", "StoreNumberTour.Step5Body",
                () => false, "LogicCellInstanceHelpButton"),
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
    public string? CurrentTargetName => NeedsControlsRecovery ? null :
        CurrentTargetItem != null ? "LogicInputRows" : CurrentStepIndex == 4 ? "LogicOutputRows" : CurrentStep.TargetName;

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
        _storedWord = 0;
        _storedAddress = false;
        _addressLeft = false;
        IsActive = true;
        ShowControls();
        Observe();
        EvaluateCurrentStep();
    }

    /// <summary>Acknowledges the completed read-back; action stages cannot be skipped.</summary>
    [RelayCommand]
    public void Next()
    {
        if (IsActive && IsClosing) Advance();
    }

    /// <summary>Exits the tour without completing it.</summary>
    [RelayCommand]
    public void Skip()
    {
        IsActive = false;
        Detach();
    }

    private void OnClockStepped(object? sender, EventArgs e)
    {
        if (!IsActive || NeedsControlsRecovery || !_logic.HasNetwork) return;
        if (CurrentStepIndex == 2 && ReadInput(LoadPin) && HasAnyDataBitOn()
            && ReadOutputWord() == ReadDataWord())
        {
            LatchStoredWord();
            CurrentStepIndex = ReadBackStepIndex;
        }
        else EvaluateCurrentStep();
    }

    private void EvaluateCurrentStep()
    {
        if (!IsActive || IsClosing) return;
        if (!_logic.HasNetwork)
        {
            _storedWord = 0;
            _addressLeft = false;
            CurrentStepIndex = 0;
        }
        else if (CurrentStepIndex == 2 && !HasAnyDataBitOn()) CurrentStepIndex = 1;
        else if (CurrentStepIndex == ReadBackStepIndex && ReadInput(AddressPin) == _storedAddress
            && ReadOutputWord() != _storedWord)
        {
            _addressLeft = false;
            CurrentStepIndex = HasAnyDataBitOn() ? 2 : 1;
        }
        if (!NeedsControlsRecovery)
            while (IsActive && CurrentStep.IsCompleted()) Advance();
        NotifyVisualState();
    }

    private void Advance()
    {
        if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
            if (CurrentStepIndex == ReadBackStepIndex)
                LatchStoredWord();
            return;
        }

        IsCompleted = true;
        IsActive = false;
        Detach();
    }

    private void LatchStoredWord()
    {
        _storedWord = ReadOutputWord();
        _storedAddress = ReadInput(AddressPin);
        _addressLeft = false;
    }

    private bool ReadInput(string pinName) =>
        _logic.Inputs.FirstOrDefault(input => input.PinName == pinName)?.IsOn ?? false;

    private int ReadOutputWord()
    {
        var word = 0;
        for (var bit = 0; bit < WordBitCount; bit++)
            if (_logic.Outputs.FirstOrDefault(output => output.PinName == OutputPinPrefix + bit)?.IsOne == true)
                word |= 1 << bit;
        return word;
    }

    private int ReadDataWord() => Enumerable.Range(0, WordBitCount)
        .Where(bit => ReadInput(DataPinPrefix + bit)).Sum(bit => 1 << bit);

    private bool HasAnyDataBitOn() =>
        Enumerable.Range(0, WordBitCount).Any(bit => ReadInput(DataPinPrefix + bit));

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(ProgressText));
        NotifyVisualState();
    }
}
