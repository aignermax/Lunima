using System.Collections.Specialized;
using System.ComponentModel;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

public partial class StoreNumberTourViewModel
{
    private readonly HashSet<LogicNetworkInputViewModel> _observedInputs = new();

    private void Observe()
    {
        _logic.PropertyChanged += OnLogicPropertyChanged;
        _logic.ClockStepped += OnClockStepped;
        _logic.RegistersReset += OnRegistersReset;
        _logic.Inputs.CollectionChanged += OnInputsCollectionChanged;
        _dock.PropertyChanged += OnContextChanged;
        if (_workspace != null) _workspace.PropertyChanged += OnContextChanged;
        HookInputs();
    }

    private void Detach()
    {
        _logic.PropertyChanged -= OnLogicPropertyChanged;
        _logic.ClockStepped -= OnClockStepped;
        _logic.RegistersReset -= OnRegistersReset;
        _logic.Inputs.CollectionChanged -= OnInputsCollectionChanged;
        _dock.PropertyChanged -= OnContextChanged;
        if (_workspace != null) _workspace.PropertyChanged -= OnContextChanged;
        foreach (var input in _observedInputs) input.PropertyChanged -= OnInputPropertyChanged;
        _observedInputs.Clear();
    }

    private void HookInputs()
    {
        foreach (var input in _observedInputs) input.PropertyChanged -= OnInputPropertyChanged;
        _observedInputs.Clear();
        foreach (var input in _logic.Inputs)
            if (_observedInputs.Add(input)) input.PropertyChanged += OnInputPropertyChanged;
    }

    private void OnInputsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookInputs();
        EvaluateCurrentStep();
    }

    private void OnLogicPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogicPanelViewModel.HasNetwork) or nameof(LogicPanelViewModel.ClockStepCount)
            or nameof(LogicPanelViewModel.StatusText)) EvaluateCurrentStep();
    }

    private void OnInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LogicNetworkInputViewModel.IsOn)) return;
        if (CurrentStepIndex == ReadBackStepIndex)
        {
            if (ReadInput(LoadPin)) _addressLeft = false;
            else if (ReadInput(AddressPin) != _storedAddress) _addressLeft = true;
        }
        EvaluateCurrentStep();
    }

    private void OnRegistersReset(object? sender, EventArgs e)
    {
        if (!IsActive || IsClosing) return;
        _storedWord = 0;
        _addressLeft = false;
        CurrentStepIndex = HasAnyDataBitOn() ? 2 : 1;
        EvaluateCurrentStep();
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Current) or nameof(AnalysisDockViewModel.IsVisible)
            or nameof(AnalysisDockViewModel.SelectedTabIndex)) EvaluateCurrentStep();
    }
}
