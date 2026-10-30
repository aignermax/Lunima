using System.Collections.ObjectModel;
using System.ComponentModel;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>
/// ViewModel behind the Truth Table panel: when exactly one <see cref="ComponentGroup"/>
/// is selected on the canvas, its external pins can be assigned as logic inputs (at most
/// <see cref="TruthTableExtractor.MaxLogicInputs"/>), outputs, or bias pins (constantly
/// "on" in every row — the ingredient inversion gates need), and the panel extracts
/// the group's truth table via <see cref="TruthTableExtractor"/> — every output bit shown
/// together with the raw simulated power behind it. A pin is exactly one of input, output,
/// or bias: checking it in one list revokes it in the other two.
/// </summary>
public partial class TruthTableViewModel : ObservableObject, IAsyncDisposable
{
    private const double DefaultThreshold = 0.5;

    private readonly TruthTableExtractor _extractor;

    private readonly TruthTableWorkSession _work = new();
    private readonly ObservablePublicationQueue _publication = new();
    private Task? _retirement;

    public TruthTableViewModel(TruthTableExtractor? extractor = null,
        ApplicationWorkCoordinator? applicationWork = null)
    {
        _extractor = extractor ?? new();
        _work.RegisterWith(applicationWork, StopAsync);
    }
    private ComponentGroup? _group;
    private ComponentGroup? _pinGroup;
    private DesignCanvasViewModel? _canvas;
    private bool _revertingPinCheck;

    /// <summary>True while exactly one group is selected and the panel is active.</summary>
    [ObservableProperty]
    private bool _isGroupSelected;

    /// <summary>True while an extraction runs (spinner + Cancel button).</summary>
    [ObservableProperty]
    private bool _isProcessing;

    /// <summary>Normalized power threshold in the open interval (0, 1); an output is logic 1 at or above it.</summary>
    [ObservableProperty]
    private double _threshold = DefaultThreshold;

    /// <summary>Status, hint, or validation message shown under the Extract button.</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>True when a truth table result is available for display.</summary>
    [ObservableProperty]
    private bool _hasResult;

    /// <summary>Bias-pin assignment of the extracted table, shown above the result (empty when none).</summary>
    [ObservableProperty]
    private string _biasSummaryText = "";

    /// <summary>Display text for the wavelength the table will be extracted at.</summary>
    [ObservableProperty]
    private string _wavelengthText = "";

    /// <summary>
    /// True when the selected group carries a persisted pin assignment (after an
    /// extraction or a load) — only then do the input rows offer the editable
    /// signal-name field (issue #1033), because the edits write into that assignment.
    /// </summary>
    [ObservableProperty]
    private bool _signalNamesVisible;

    /// <summary>External pins of the selected group offered as logic inputs (checkboxes).</summary>
    public ObservableCollection<PinSelectionViewModel> InputPins { get; } = new();

    /// <summary>External pins of the selected group offered as logic outputs (checkboxes).</summary>
    public ObservableCollection<PinSelectionViewModel> OutputPins { get; } = new();

    /// <summary>External pins of the selected group offered as bias pins (checkboxes) — constantly "on" in every row.</summary>
    public ObservableCollection<PinSelectionViewModel> BiasPins { get; } = new();

    /// <summary>Input pin names of the extracted table, in bit order (table header).</summary>
    public ObservableCollection<string> InputHeaders { get; } = new();

    /// <summary>Output pin names of the extracted table (table header).</summary>
    public ObservableCollection<string> OutputHeaders { get; } = new();

    /// <summary>The extracted truth table rows, in binary counting order.</summary>
    public ObservableCollection<TruthTableRowViewModel> Rows { get; } = new();

    /// <summary>
    /// Activates the panel for the current selection: exactly one selected
    /// <see cref="ComponentGroup"/> exposes its external pins; anything else shows a hint.
    /// Called from the selection-changed callback in MainViewModel.
    /// </summary>
    public void ConfigureForSelection(ComponentViewModel? component, DesignCanvasViewModel? canvas)
    {
        if (_work.IsStopping) return;
        _work.Configure(canvas?.Document, () => component?.Component is ComponentGroup
            && (canvas == null || canvas.Components.Contains(component)));
        _canvas = canvas;
        _group = component?.Component as ComponentGroup;
        var version = _work.Version;
        var singleSelection = canvas == null || canvas.Selection.SelectedComponents.Count <= 1;
        _publication.Publish(() => ConfigureDisplay(version, singleSelection));
    }

    private void ConfigureDisplay(long version, bool singleSelection)
    {
        if (!_work.IsVersionCurrent(version)) return;
        _revertingPinCheck = true;
        try
        {
            IsGroupSelected = _group != null && singleSelection;
            if (!_work.IsVersionCurrent(version)) return;
            HasResult = false;
            if (!_work.IsVersionCurrent(version)) return;
            Rows.Clear();
            if (!_work.IsVersionCurrent(version)) return;
            InputHeaders.Clear();
            if (!_work.IsVersionCurrent(version)) return;
            OutputHeaders.Clear();
            if (!_work.IsVersionCurrent(version)) return;
            StatusText = "";
            if (!_work.IsVersionCurrent(version)) return;
            BiasSummaryText = "";
            if (!_work.IsVersionCurrent(version)) return;
            ClearThresholdSuggestion(() => _work.IsVersionCurrent(version));
            if (!_work.IsVersionCurrent(version)) return;
            IsRegister = _group?.TruthTablePinAssignment?.IsRegister ?? false;
            if (!_work.IsVersionCurrent(version)) return;
            RebuildPinLists(version);
            if (!_work.IsVersionCurrent(version)) return;
            PrefillFromPersistedAssignment(version);
            if (!_work.IsVersionCurrent(version)) return;
            SignalNamesVisible = IsGroupSelected && _group?.TruthTablePinAssignment != null;
            if (!_work.IsVersionCurrent(version)) return;
            RefreshCollisionHints();
            if (!_work.IsVersionCurrent(version)) return;
            WavelengthText = IsGroupSelected
                ? string.Format(Translate("TruthTable.Wavelength"), ResolveWavelengthNm()) : "";
        }
        finally { _revertingPinCheck = false; }
    }

    public Task StopAsync()
    {
        if (_retirement != null) return _retirement;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retirement = completion.Task;
        _ = RetireAsync(completion);
        return completion.Task;
    }

    private async Task RetireAsync(TaskCompletionSource completion)
    {
        try
        {
            try { await _work.StopAsync(); }
            finally
            {
                DetachPinHandlers();
                _group = null;
                _pinGroup = null;
                _canvas = null;
                OpenLogicRequested = null;
                await _publication.PublishAsync(() =>
                {
                    HasResult = false;
                    Rows.Clear();
                    IsProcessing = false;
                });
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    /// <summary>
    /// Re-applies the pin-role assignment the group was last successfully extracted
    /// with (issue #981 — persisted in the .lun file): ticks the same input/output/
    /// bias checkboxes and restores the threshold, so a save → load round trip
    /// brings the panel back exactly as the user left it. Silent for legacy files
    /// and for groups never extracted; pin names that no longer exist on the group
    /// are skipped instead of failing.
    /// </summary>
    private void PrefillFromPersistedAssignment(long version)
    {
        var saved = _group?.TruthTablePinAssignment;
        if (saved == null) return;
        CheckPins(InputPins, saved.InputPinNames, version);
        if (!_work.IsVersionCurrent(version)) return;
        CheckPins(OutputPins, saved.OutputPinNames, version);
        if (!_work.IsVersionCurrent(version)) return;
        CheckPins(BiasPins, saved.BiasPinNames, version);
        if (!_work.IsVersionCurrent(version)) return;
        Threshold = saved.Threshold;
        if (!_work.IsVersionCurrent(version)) return;
        PrefillSignalNames(saved);
    }

    private void CheckPins(IEnumerable<PinSelectionViewModel> pins, IEnumerable<string> names, long version)
    {
        foreach (var name in names)
        {
            if (!_work.IsVersionCurrent(version)) return;
            var pin = pins.FirstOrDefault(p => p.PinName == name);
            if (pin != null) pin.IsChecked = true;
        }
    }

    private void RebuildPinLists(long version)
    {
        DetachPinHandlers();
        _pinGroup = null;
        InputPins.Clear();
        if (!_work.IsVersionCurrent(version)) return;
        OutputPins.Clear();
        if (!_work.IsVersionCurrent(version)) return;
        BiasPins.Clear();
        if (!_work.IsVersionCurrent(version) || !IsGroupSelected || _group == null) return;
        _pinGroup = _group;
        foreach (var pin in _group.ExternalPins.ToArray())
        {
            var input = CreatePin(pin.Name);
            input.SignalEditingVisible = SignalNamesVisible;
            InputPins.Add(input);
            if (!_work.IsVersionCurrent(version)) return;
            var output = CreatePin(pin.Name);
            output.SignalEditingVisible = SignalNamesVisible;
            OutputPins.Add(output);
            if (!_work.IsVersionCurrent(version)) return;
            BiasPins.Add(CreatePin(pin.Name));
            if (!_work.IsVersionCurrent(version)) return;
        }
    }

    /// <summary>Builds one checkbox entry and wires the pin-role invariant handler.</summary>
    private PinSelectionViewModel CreatePin(string pinName)
    {
        var pin = new PinSelectionViewModel(pinName);
        pin.PropertyChanged += OnPinPropertyChanged;
        return pin;
    }

    private void DetachPinHandlers()
    {
        foreach (var pin in InputPins.Concat(OutputPins).Concat(BiasPins))
            pin.PropertyChanged -= OnPinPropertyChanged;
    }

    /// <summary>
    /// Enforces the pin-role invariants directly at the checkbox: a pin is at most one
    /// of input, output, or bias (checking it in one list revokes it in the other two),
    /// and at most <see cref="TruthTableExtractor.MaxLogicInputs"/> inputs may be checked.
    /// Signal-name edits write through to the group's persisted assignment, and a pin
    /// losing its input or output role drops its signal identity (issues #1033/#1046).
    /// </summary>
    private void OnPinPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_work.IsStopping || !ReferenceEquals(_pinGroup, _group)
            || sender is not PinSelectionViewModel pin
            || !(InputPins.Contains(pin) || OutputPins.Contains(pin) || BiasPins.Contains(pin)))
            return;
        if (!_revertingPinCheck && (e.PropertyName is nameof(PinSelectionViewModel.SignalName)
            or nameof(PinSelectionViewModel.IsChecked))) _work.Invalidate();
        if (e.PropertyName == nameof(PinSelectionViewModel.SignalName))
        {
            if (!_revertingPinCheck && (InputPins.Contains(pin) || OutputPins.Contains(pin)))
                ApplySignalNameEdit(pin);
            RefreshCollisionHints();
            return;
        }
        if (_revertingPinCheck || e.PropertyName != nameof(PinSelectionViewModel.IsChecked))
            return;
        if (!pin.IsChecked)
        {
            if (InputPins.Contains(pin) || OutputPins.Contains(pin))
                RevokeSignalName(pin);
            RefreshCollisionHints();
            return;
        }

        _revertingPinCheck = true;
        try
        {
            UncheckSamePinInOtherLists(pin);
            EnforceInputLimit(pin);
        }
        finally
        {
            _revertingPinCheck = false;
        }
        RefreshCollisionHints();
    }

    /// <summary>Unchecks the same pin name in the two lists the just-checked pin does not belong to.</summary>
    private void UncheckSamePinInOtherLists(PinSelectionViewModel checkedPin)
    {
        foreach (var list in new[] { InputPins, OutputPins, BiasPins })
        {
            if (list.Contains(checkedPin))
                continue;
            var twin = list.FirstOrDefault(p => p.PinName == checkedPin.PinName);
            if (twin == null)
                continue;
            twin.IsChecked = false;
            // The guard above suppresses the twin's own uncheck handler, so the
            // role loss revokes its signal identity explicitly here.
            if (!ReferenceEquals(list, BiasPins))
                RevokeSignalName(twin);
        }
    }

    /// <summary>Reverts a fresh input check that would exceed the extractor's input limit.</summary>
    private void EnforceInputLimit(PinSelectionViewModel checkedPin)
    {
        if (!InputPins.Contains(checkedPin))
            return;
        if (InputPins.Count(p => p.IsChecked) <= TruthTableExtractor.MaxLogicInputs)
            return;

        checkedPin.IsChecked = false;
        StatusText = string.Format(Translate("Analysis.TruthTable.TooManyInputs"), TruthTableExtractor.MaxLogicInputs);
    }

    /// <summary>The active laser's wavelength, falling back to the standard red wavelength.</summary>
    private int ResolveWavelengthNm() =>
        _canvas?.Components.FirstOrDefault(c => c.IsLightSource)?.LaserConfig?.WavelengthNm
        ?? StandardWaveLengths.RedNM;

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);
}
