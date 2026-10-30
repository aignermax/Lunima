using CAP_Core.Grid;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.TimeTrace;
using CAP.Avalonia.ViewModels.Canvas;
using OxyPlot;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// ViewModel for the time-domain (transient) simulation panel.
/// Sweeps S-parameters over a wavelength grid, performs IFFT to obtain impulse
/// responses, convolves with a user-selected input pulse, and reports output traces.
/// </summary>
public partial class TimeDomainViewModel : ObservableObject, IAsyncDisposable
{
    [ObservableProperty]
    private double _centerWavelengthNm = 1550;

    [ObservableProperty]
    private double _spanNm = 100;

    [ObservableProperty]
    private int _freqPoints = 256;

    [ObservableProperty]
    private double _pulseCenterPs = 2.0;

    [ObservableProperty]
    private double _pulseSigmaPs = 0.5;

    /// <summary>
    /// Signal-source selection + parameters (issue #600): Gaussian pulse
    /// (default, back-compat), CW, or PRBS-NRZ on a signal-driven time grid.
    /// </summary>
    public TransientSourceSettingsViewModel Source { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _resultText = "";

    /// <summary>
    /// OxyPlot model for the transient waveform plot (power vs time, one series
    /// per output pin). Bound to the panel's <c>PlotView</c>. Reuses the ONA
    /// charting approach (#526) — zoom/pan come from OxyPlot's default axes.
    /// </summary>
    [ObservableProperty]
    private PlotModel _plotModel = TimeTracePlotBuilder.CreateEmptyPlotModel();

    /// <summary>
    /// Legend / per-pin visibility toggles. Each entry maps to one output-pin
    /// trace; toggling <see cref="TimeTraceSeriesViewModel.IsVisible"/> updates
    /// the existing plot without replacing its data or viewport.
    /// </summary>
    public ObservableCollection<TimeTraceSeriesViewModel> Series { get; } = new();

    /// <summary>
    /// One-line description of which components' curves are plotted, driven by the canvas
    /// selection (all components when nothing is selected).
    /// </summary>
    [ObservableProperty]
    private string _selectionHint = "";

    /// <summary>True once a completed transient result is available to plot/export.</summary>
    public bool HasResult => _lastResult != null;

    private readonly CAP_Core.ErrorConsoleService? _errorConsole;
    private readonly TransientPanelLifetime _lifetime = new();
    private readonly ObservablePublicationQueue _publication = new();
    private readonly ITransientSimulationRunner _runner;
    private DesignCanvasViewModel? _canvas => _lifetime.Canvas;
    private Task? _retirement;
    public Task StopAsync() => _retirement ??= RetireAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private async Task RetireAsync()
    {
        try { await _lifetime.StopAsync(); }
        finally
        {
            DetachSeries();
            ObserveSelection(null);
            _lastResult = null;
            FileDialogService = null;
            RequestOutputPicker = null;
        }
    }
    private TimeDomainResult? _lastResult;
    private Dictionary<Guid, string> _pinNameMap = new();
    private Dictionary<Guid, CAP_Core.Components.Core.Component> _pinOwnerMap = new();
    private CAP.Avalonia.Selection.SelectionManager? _observedSelection;

    /// <summary>Initializes a new instance of <see cref="TimeDomainViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public TimeDomainViewModel(CAP_Core.ErrorConsoleService? errorConsole = null,
        ITransientSimulationRunner? runner = null, Services.Lifetime.ApplicationWorkCoordinator? applicationWork = null)
    {
        _errorConsole = errorConsole;
        _runner = runner ?? new TransientSimulationRunner();
        _lifetime.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>File dialog service for CSV export. Set by MainViewModel.</summary>
    public Services.IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// Callback that activates the canvas analysis-output picker (#754). Invoked when a
    /// run is ambiguous (several possible outputs, none designated) or the designation
    /// became invalid, so the user can pick instead of facing a modal dialog. Wired by
    /// <c>MainViewModel</c>.
    /// </summary>
    public Action? RequestOutputPicker { get; set; }

    /// <summary>Configures the panel with the current canvas context.</summary>
    public void Configure(DesignCanvasViewModel? canvas)
    {
        if (_lifetime.IsStopping) return;
        var version = _lifetime.Configure(canvas);
        ObserveSelection(canvas);
        _lastResult = null;
        _publication.Publish(() =>
        {
            if (!_lifetime.IsConfiguration(version)) return;
            ResultText = "";
            if (!_lifetime.IsConfiguration(version)) return;
            StatusText = "";
            if (!_lifetime.IsConfiguration(version)) return;
            ClearPlot(() => _lifetime.IsConfiguration(version));
        });
    }

    /// <summary>Runs the time-domain simulation pipeline.</summary>
    [RelayCommand]
    private Task RunTransient() => _lifetime.RunAsync(RunTransientCoreAsync);

    private async Task RunTransientCoreAsync(CancellationToken stop)
    {
        var canvas = _canvas;
        var version = _lifetime.Version;
        if (canvas == null || canvas.Components.Count == 0)
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Common.NoCircuit");
            return;
        }
        if (canvas.Components.Where(c => c.IsLightSource).All(c => c.IsLaserOff)
            && canvas.Components.Any(c => c.IsLightSource))
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Common.NoLaserOn");
            return;
        }
        if (IsRunning) return;
        AnalysisSweepOperation? operation = null;
        try
        {
            var resolution = AnalysisOutputResolver.Resolve(canvas);
            if (!_lifetime.IsConfiguration(version)) return;
            operation = _lifetime.Begin(stop);
            operation.ThrowIfObsolete();
            if (ReportInvalidDesignation(resolution, operation)) return;
            var output = CapturedAnalysisOutput.Capture(resolution);
            var names = BuildPinNameMap(out var owners);
            var (circuit, excitation) = TransientCircuitFactory.Capture(canvas);
            var waveform = new TransientWaveformSettings(Source.SourceType switch
            {
                TransientSourceType.ContinuousWave => TransientWaveform.ContinuousWave,
                TransientSourceType.PrbsNrz => TransientWaveform.PrbsNrz,
                _ => TransientWaveform.GaussianPulse
            }, Source.BitrateGbps * 1e9, Source.PrbsOrder, Source.SamplesPerSymbol, Source.SymbolCount,
                Source.ExtinctionRatioDb, Source.Seed, PulseCenterPs * 1e-12, PulseSigmaPs * 1e-12);
            var request = new TransientSimulationRequest(circuit, excitation, waveform,
                CenterWavelengthNm, SpanNm, FreqPoints);
            operation.ThrowIfObsolete();
            IsRunning = true;
            operation.ThrowIfObsolete();
            StatusText = LocalizationService.Instance.Translate("Analysis.TimeDomain.BuildingImpulseResponses");
            operation.ThrowIfObsolete();
            _lastResult = null;
            ResultText = "";
            operation.ThrowIfObsolete();
            await _publication.PublishAsync(() =>
            {
                if (operation.CanPublish) ClearPlot(() => operation.CanPublish);
            });
            operation.ThrowIfObsolete();
            var outcome = await _runner.RunAsync(request, operation.Token);
            operation.ThrowIfObsolete();
            foreach (var warning in outcome.Warnings)
            {
                _errorConsole?.LogWarning(warning.ToMessage());
                operation.ThrowIfObsolete();
            }
            var (displayed, statusOverride) = ApplyOutputSelection(outcome.Result, output);
            if (displayed == null) { StatusText = statusOverride!; return; }
            _pinNameMap = names;
            _pinOwnerMap = owners;
            _lastResult = displayed;
            var peaks = TimeTracePeak.Build(displayed);
            ResultText = TimeDomainResultFormatter.FormatResult(displayed, peaks);
            operation.ThrowIfObsolete();
            await _publication.PublishAsync(() =>
            {
                if (operation.CanPublish) BuildPlot(displayed, peaks, operation);
            });
            operation.ThrowIfObsolete();
            OnPropertyChanged(nameof(HasResult));
            operation.ThrowIfObsolete();
            StatusText = statusOverride ?? string.Format(
                LocalizationService.Instance.Translate("Analysis.TimeDomain.DonePins"), displayed.PinTraces.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError(ex is InvalidOperationException
                ? $"Time-domain simulation blocked: {ex.Message}" : $"Time-domain simulation failed: {ex.Message}", ex);
            if (!operation.CanPublish) return;
            StatusText = ex switch
            {
                CAP_Core.LightCalculation.NonConvergentCircuitException physics => NonConvergentCircuitMessageFormatter.Format(physics),
                InvalidOperationException => string.Format(LocalizationService.Instance.Translate("Analysis.TimeDomain.CannotRun"), ex.Message),
                _ => string.Format(LocalizationService.Instance.Translate("Analysis.Common.Failed"), ex.Message)
            };
        }
        finally
        {
            try { IsRunning = false; }
            finally { await _lifetime.EndAsync(operation); }
        }
    }

    /// <summary>Exports the last simulation result to a CSV file.</summary>
    [RelayCommand]
    private Task ExportCsv() => _lifetime.RunAsync(ExportCsvCoreAsync);

    private async Task ExportCsvCoreAsync(CancellationToken stop)
    {
        var result = _lastResult;
        if (result == null || _canvas == null) return;
        AnalysisSweepOperation? operation = null;
        try
        {
            operation = _lifetime.Begin(stop);
            operation.ThrowIfObsolete();
            var dialog = FileDialogService;
            var path = dialog == null ? null : await dialog.ShowSaveFileDialogAsync(
                "Export Time-Domain Traces", "csv", "CSV Files|*.csv|All Files|*.*");
            operation.ThrowIfObsolete();
            if (!ReferenceEquals(result, _lastResult)) return;
            if (path == null)
            {
                StatusText = LocalizationService.Instance.Translate("Analysis.Common.ExportCancelled");
                return;
            }
            var csv = TimeDomainResultFormatter.BuildCsvContent(result);
            operation.ThrowIfObsolete();
            await File.WriteAllTextAsync(path, csv, operation.Token);
            operation.ThrowIfObsolete();
            if (!ReferenceEquals(result, _lastResult)) return;
            StatusText = string.Format(LocalizationService.Instance.Translate("Analysis.Common.ExportedTo"), Path.GetFileName(path));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError($"CSV export failed: {ex.Message}", ex);
            if (operation.CanPublish)
                StatusText = string.Format(LocalizationService.Instance.Translate("Analysis.Common.ExportFailed"), ex.Message);
        }
        finally { await _lifetime.EndAsync(operation); }
    }

    /// <summary>
    /// Surfaces an invalid designation (#754) as a status warning and activates the
    /// picker. Returns true when the run must not proceed.
    /// </summary>
    private bool ReportInvalidDesignation(AnalysisOutputResolution resolution, AnalysisSweepOperation operation)
    {
        if (resolution.State == AnalysisOutputState.DesignatedMissing)
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Output.DesignatedMissing");
            operation.ThrowIfObsolete();
            RequestOutputPicker?.Invoke();
            return true;
        }
        if (resolution.State == AnalysisOutputState.DesignatedLaserOn)
        {
            StatusText = string.Format(
                LocalizationService.Instance.Translate("Analysis.Output.DesignatedLaserOn"),
                resolution.Output!.Name);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Applies the output designation to a completed result (#754): with a valid
    /// designation only THAT coupler's light-pin traces are displayed (null result +
    /// error status when no light reaches it); an ambiguous design keeps all traces
    /// but carries an explicit warning instead of a silent "Done".
    /// </summary>
    private static (TimeDomainResult? Displayed, string? StatusOverride) ApplyOutputSelection(
        TimeDomainResult result, CapturedAnalysisOutput resolution)
    {
        if (resolution.State == AnalysisOutputState.MultipleCandidates)
            return (result, LocalizationService.Instance.Translate("Analysis.Output.MultipleCandidatesWarning"));
        if (resolution.State != AnalysisOutputState.DesignatedValid)
            return (result, null);

        var pinIds = resolution.PinIds!;
        var traces = result.PinTraces
            .Where(kv => pinIds.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (traces.Count == 0)
            return (null, string.Format(
                LocalizationService.Instance.Translate("Analysis.Output.NoSignal"), resolution.Name));
        return (new TimeDomainResult(result.TimeAxis, traces), null);
    }

    /// <summary>
    /// Maps each logical pin Guid (both in- and out-flow) to a "Component.Pin" label so the
    /// plot legend shows readable names instead of Guids. Built on the UI thread from the
    /// current canvas before the simulation runs.
    /// </summary>
    private Dictionary<Guid, string> BuildPinNameMap(out Dictionary<Guid, CAP_Core.Components.Core.Component> owners)
    {
        var map = new Dictionary<Guid, string>();
        owners = new Dictionary<Guid, CAP_Core.Components.Core.Component>();
        if (_canvas == null) return map;

        foreach (var compVm in _canvas.Components)
        {
            var component = compVm.Component;
            var componentName = component.HumanReadableName ?? component.Identifier;
            foreach (var pin in component.PhysicalPins)
            {
                if (pin.LogicalPin == null) continue;
                var label = $"{componentName}.{pin.Name}";
                // The result keys traces by the output pin's flow id; map both so the
                // label resolves regardless of which flow direction the trace carries.
                map[pin.LogicalPin.IDInFlow] = label;
                map[pin.LogicalPin.IDOutFlow] = label;
                owners[pin.LogicalPin.IDInFlow] = component;
                owners[pin.LogicalPin.IDOutFlow] = component;
            }
        }
        return map;
    }

    /// <summary>
    /// Builds the legend items and plot model from a completed result, and subscribes to
    /// each series so toggling its visibility updates the existing plot.
    /// </summary>
    private void BuildPlot(TimeDomainResult result, IReadOnlyList<TimeTracePeak> peaks, AnalysisSweepOperation operation)
    {
        DetachSeries();
        Series.Clear();
        operation.ThrowIfObsolete();

        var items = TimeTracePlotBuilder.BuildSeriesItems(
            result, pinId => _pinNameMap.TryGetValue(pinId, out var name) ? name : null, peaks);
        var selected = SelectedComponentSet();
        foreach (var item in items)
        {
            item.IsVisible = IsPinShown(item.PinId, selected);
            item.PropertyChanged += OnSeriesVisibilityChanged;
            Series.Add(item);
            operation.ThrowIfObsolete();
        }

        PlotModel = TimeTracePlotBuilder.BuildPlotModel(result, items);
        UpdateSelectionHint();
    }

    private void ObserveSelection(DesignCanvasViewModel? canvas)
    {
        if (_observedSelection != null)
            _observedSelection.SelectedComponents.CollectionChanged -= OnCanvasSelectionChanged;
        _observedSelection = canvas?.Selection;
        if (_observedSelection != null)
            _observedSelection.SelectedComponents.CollectionChanged += OnCanvasSelectionChanged;
    }

    /// <summary>Re-filters the plotted curves to the selection; never re-runs the simulation.</summary>
    private void OnCanvasSelectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_lastResult == null || Series.Count == 0) return;
        var selected = SelectedComponentSet();
        // Select-all raises one change per component: redraw the plot once, not once per curve.
        _batchingVisibility = true;
        try
        {
            foreach (var item in Series.ToList())
                item.IsVisible = IsPinShown(item.PinId, selected);
        }
        finally
        {
            _batchingVisibility = false;
        }
        PlotModel.InvalidatePlot(false);
        UpdateSelectionHint();
    }

    private bool _batchingVisibility;

    private HashSet<CAP_Core.Components.Core.Component> SelectedComponentSet() =>
        _observedSelection?.SelectedComponents.Select(c => c.Component).ToHashSet() ?? new();

    private bool IsPinShown(Guid pinId, HashSet<CAP_Core.Components.Core.Component> selected) =>
        selected.Count == 0
        || (_pinOwnerMap.TryGetValue(pinId, out var owner) && selected.Contains(owner));

    private void UpdateSelectionHint()
    {
        var selectedCount = SelectedComponentSet().Count;
        var shown = Series.Count(s => s.IsVisible);
        var t = LocalizationService.Instance;
        SelectionHint = selectedCount == 0
            ? t.Translate("Analysis.TimeDomain.SelectionAll")
            : shown == 0
                ? t.Translate("Analysis.TimeDomain.SelectionNoCurves")
                : string.Format(t.Translate("Analysis.TimeDomain.SelectionFiltered"), selectedCount);
        TimeTracePlotBuilder.ApplyLegendPolicy(PlotModel, shown);
        PlotModel.InvalidatePlot(false);
    }

    /// <summary>Resets the plot and legend to the empty state (e.g. on reconfigure or re-run).</summary>
    private void ClearPlot(Func<bool> canContinue)
    {
        DetachSeries();
        Series.Clear();
        SelectionHint = "";
        if (!canContinue()) return;
        PlotModel = TimeTracePlotBuilder.CreateEmptyPlotModel();
        if (canContinue()) OnPropertyChanged(nameof(HasResult));
    }

    private void DetachSeries()
    {
        foreach (var series in Series)
            series.PropertyChanged -= OnSeriesVisibilityChanged;
    }

    private void OnSeriesVisibilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimeTraceSeriesViewModel.IsVisible)
            || sender is not TimeTraceSeriesViewModel item || _lastResult == null)
            return;

        var series = PlotModel.Series.FirstOrDefault(series => Equals(series.Tag, item.PinId));
        if (series == null) return;
        series.IsVisible = item.IsVisible;
        if (!_batchingVisibility)
            PlotModel.InvalidatePlot(false);
    }
}
