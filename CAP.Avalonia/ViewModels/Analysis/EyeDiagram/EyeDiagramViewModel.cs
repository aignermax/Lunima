using System.Globalization;
using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Canvas;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;

namespace CAP.Avalonia.ViewModels.Analysis.EyeDiagram;

/// <summary>
/// ViewModel for the eye-diagram / BER panel (#535). Drives a PRBS-modulated
/// transient simulation (#527 pipeline), folds the output-coupler trace (the
/// coupler with its laser off, #690; strongest trace as legacy fallback) into
/// an eye histogram, and reports Q-factor / BER with a receiver noise model.
/// </summary>
public partial class EyeDiagramViewModel : ObservableObject, IAsyncDisposable
{
    private const double GigabitsToBits = 1e9;
    private const double SecondsToPicoseconds = 1e12;

    /// <summary>Selectable PRBS pattern orders.</summary>
    public IReadOnlyList<PrbsOrder> PrbsOrders { get; } =
        new[] { PrbsOrder.Prbs7, PrbsOrder.Prbs11, PrbsOrder.Prbs23 };

    [ObservableProperty]
    private double _bitRateGbps = 25;

    [ObservableProperty]
    private PrbsOrder _selectedPrbsOrder = PrbsOrder.Prbs7;

    /// <summary>Decision threshold as a fraction of the trace amplitude range (0…1).</summary>
    [ObservableProperty]
    private double _thresholdRelative = 0.5;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Formatted eye metrics (Q, BER, height, width, jitter) shown beside the plot.</summary>
    [ObservableProperty]
    private string _metricsText = "";

    /// <summary>OxyPlot heat-map model of the eye persistence display.</summary>
    [ObservableProperty]
    private PlotModel _plotModel = EyeDiagramPlotBuilder.CreateEmptyPlotModel();

    /// <summary>True once a completed eye analysis is available to plot/export.</summary>
    public bool HasResult => _lastHistogram != null;

    /// <summary>The histogram of the last completed run (null before the first run).</summary>
    public EyeHistogram? LastHistogram => _lastHistogram;

    /// <summary>File dialog service for CSV export. Set by MainViewModel.</summary>
    public Services.IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// Callback that activates the canvas analysis-output picker (#754). Invoked when a
    /// run is ambiguous (several possible outputs, none designated) or the designation
    /// became invalid, so the user can pick instead of facing a modal dialog. Wired by
    /// <c>MainViewModel</c>.
    /// </summary>
    public Action? RequestOutputPicker { get; set; }

    private readonly CAP_Core.ErrorConsoleService? _errorConsole;
    private readonly TransientPanelLifetime _lifetime = new();
    private readonly IEyeAnalysisRunner _runner;
    private DesignCanvasViewModel? _canvas => _lifetime.Canvas;
    private Task? _retirement;
    public Task StopAsync() => _retirement ??= RetireAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private async Task RetireAsync()
    {
        try { await _lifetime.StopAsync(); }
        finally
        {
            _lastHistogram = null;
            FileDialogService = null;
            RequestOutputPicker = null;
        }
    }
    private EyeHistogram? _lastHistogram;

    /// <summary>Initializes a new instance of <see cref="EyeDiagramViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public EyeDiagramViewModel(CAP_Core.ErrorConsoleService? errorConsole = null,
        IEyeAnalysisRunner? runner = null, Services.Lifetime.ApplicationWorkCoordinator? applicationWork = null)
    {
        _errorConsole = errorConsole;
        _runner = runner ?? new EyeAnalysisRunner();
        _lifetime.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>Configures the panel with the current canvas context.</summary>
    /// <param name="canvas">Canvas providing components and connections.</param>
    public void Configure(DesignCanvasViewModel? canvas)
    {
        if (_lifetime.IsStopping) return;
        var version = _lifetime.Configure(canvas);
        _lastHistogram = null;
        StatusText = "";
        if (!_lifetime.IsConfiguration(version)) return;
        MetricsText = "";
        if (!_lifetime.IsConfiguration(version)) return;
        PlotModel = EyeDiagramPlotBuilder.CreateEmptyPlotModel();
        if (_lifetime.IsConfiguration(version)) OnPropertyChanged(nameof(HasResult));
    }

    /// <summary>Runs the PRBS transient simulation and updates the eye plot and metrics.</summary>
    [RelayCommand]
    private Task RunEyeAnalysis() => _lifetime.RunAsync(RunEyeAnalysisCoreAsync);

    private async Task RunEyeAnalysisCoreAsync(CancellationToken stop)
    {
        var canvas = _canvas;
        var version = _lifetime.Version;
        if (canvas == null || canvas.Components.Count == 0)
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Common.NoCircuit");
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
            var (circuit, excitation) = TransientCircuitFactory.Capture(canvas);
            var request = new EyeAnalysisRequest(circuit, excitation, BitRateGbps * GigabitsToBits,
                SelectedPrbsOrder, ThresholdRelative, output.PinIds,
                output.State == AnalysisOutputState.MultipleCandidates,
                new(EyeTraceSelector.NoSignalAtDesignatedOutputError, EyeTraceSelector.BelowNoiseFloorError,
                    EyeTraceSelector.MultipleOutputsWarning));
            operation.ThrowIfObsolete();
            IsRunning = true;
            operation.ThrowIfObsolete();
            StatusText = LocalizationService.Instance.Translate("Analysis.Eye.RunningPrbs");
            operation.ThrowIfObsolete();
            _lastHistogram = null;
            MetricsText = "";
            operation.ThrowIfObsolete();
            var outcome = await _runner.RunAsync(request, operation.Token);
            operation.ThrowIfObsolete();
            foreach (var warning in outcome.PassivityWarnings)
            {
                _errorConsole?.LogWarning(warning.ToMessage());
                operation.ThrowIfObsolete();
            }
            if (outcome.Error != null || outcome.Histogram == null)
            {
                StatusText = outcome.Error ?? "Eye analysis produced no result.";
                return;
            }
            _lastHistogram = outcome.Histogram;
            PlotModel = EyeDiagramPlotBuilder.BuildPlotModel(outcome.Histogram);
            operation.ThrowIfObsolete();
            MetricsText = FormatMetrics(outcome.Metrics!);
            operation.ThrowIfObsolete();
            OnPropertyChanged(nameof(HasResult));
            operation.ThrowIfObsolete();
            StatusText = outcome.Warning ?? "Done";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError(ex is CAP_Core.LightCalculation.NonConvergentCircuitException
                ? $"Eye-diagram analysis blocked: {ex.Message}" : $"Eye-diagram analysis failed: {ex.Message}", ex);
            if (!operation.CanPublish) return;
            StatusText = ex is CAP_Core.LightCalculation.NonConvergentCircuitException physics
                ? NonConvergentCircuitMessageFormatter.Format(physics)
                : string.Format(LocalizationService.Instance.Translate("Analysis.Common.Failed"), ex.Message);
        }
        finally
        {
            try { IsRunning = false; }
            finally { await _lifetime.EndAsync(operation); }
        }
    }

    /// <summary>Exports the eye histogram to a CSV file.</summary>
    [RelayCommand]
    private Task ExportCsv() => _lifetime.RunAsync(ExportCsvCoreAsync);

    private async Task ExportCsvCoreAsync(CancellationToken stop)
    {
        var histogram = _lastHistogram;
        if (histogram == null || _canvas == null) return;
        AnalysisSweepOperation? operation = null;
        try
        {
            operation = _lifetime.Begin(stop);
            operation.ThrowIfObsolete();
            var dialog = FileDialogService;
            var path = dialog == null ? null : await dialog.ShowSaveFileDialogAsync(
                "Export Eye Histogram", "csv", "CSV Files|*.csv|All Files|*.*");
            operation.ThrowIfObsolete();
            if (!ReferenceEquals(histogram, _lastHistogram)) return;
            if (path == null)
            {
                StatusText = LocalizationService.Instance.Translate("Analysis.Common.ExportCancelled");
                return;
            }
            var csv = histogram.ToCsv();
            operation.ThrowIfObsolete();
            await File.WriteAllTextAsync(path, csv, operation.Token);
            operation.ThrowIfObsolete();
            if (!ReferenceEquals(histogram, _lastHistogram)) return;
            StatusText = string.Format(LocalizationService.Instance.Translate("Analysis.Common.ExportedTo"), Path.GetFileName(path));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError($"Eye histogram export failed: {ex.Message}", ex);
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

    private static string FormatMetrics(EyeMetrics metrics)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join(Environment.NewLine,
            string.Format(inv, "Q factor:       {0:F2}", metrics.QFactor),
            string.Format(inv, "BER (est.):     {0:E2}", metrics.BerEstimate),
            string.Format(inv, "Eye height:     {0:E3}", metrics.EyeHeight),
            string.Format(inv, "Eye width:      {0:F2} ps", metrics.EyeWidthSeconds * SecondsToPicoseconds),
            string.Format(inv, "RMS jitter:     {0:F3} ps", metrics.RmsJitterSeconds * SecondsToPicoseconds),
            string.Format(inv, "Optimal sample: {0:F2} ps", metrics.OptimalSampleOffsetSeconds * SecondsToPicoseconds));
    }
}
