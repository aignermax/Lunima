using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

public partial class StoreNumberTourViewModel
{
    private WorkspaceViewModel? _workspace;

    public void AttachWorkspace(WorkspaceViewModel workspace)
    {
        Detach();
        _workspace = workspace;
        if (IsActive) Observe();
        NotifyVisualState();
    }

    public bool IsClosing => CurrentStepIndex == Steps.Count - 1;
    public bool NeedsControlsRecovery => IsActive && !IsClosing &&
        (_workspace is { IsLogic: false } || !_dock.IsVisible || _dock.SelectedTabIndex != (int)AnalysisDockTab.Logic);

    public LogicNetworkInputViewModel? CurrentTargetItem => !IsActive || NeedsControlsRecovery ? null :
        _logic.Inputs.FirstOrDefault(i => i.PinName == (CurrentStepIndex switch
        {
            1 => "D0",
            2 when !ReadInput(LoadPin) => LoadPin,
            3 when ReadInput(LoadPin) => LoadPin,
            3 => AddressPin,
            _ => null
        }));

    public string CurrentCue => LocalizationService.Instance.Translate(NeedsControlsRecovery
        ? "StoreNumberTour.Controls" : CurrentStepIndex switch
        {
            0 => "LogicPanel.Build",
            1 => "StoreNumberTour.DataCue",
            2 when !ReadInput(LoadPin) => "StoreNumberTour.LoadOnCue",
            2 => "LogicPanel.StepClock",
            3 when ReadInput(LoadPin) => "StoreNumberTour.LoadOffCue",
            3 when !_addressLeft => "StoreNumberTour.AddressAwayCue",
            3 => "StoreNumberTour.AddressBackCue",
            _ => "StoreNumberTour.ReadBackCue"
        });

    [RelayCommand]
    private void ShowControls()
    {
        if (!IsActive) return;
        if (_workspace != null) _workspace.Current = Workspace.Logic;
        _dock.OpenLogic();
        EvaluateCurrentStep();
    }

    private void NotifyVisualState()
    {
        OnPropertyChanged(nameof(IsClosing));
        OnPropertyChanged(nameof(NeedsControlsRecovery));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(CurrentTargetItem));
        OnPropertyChanged(nameof(CurrentCue));
    }
}
