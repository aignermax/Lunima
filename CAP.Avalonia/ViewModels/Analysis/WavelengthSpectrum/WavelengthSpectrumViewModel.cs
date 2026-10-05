using System.Collections.ObjectModel;
using System.Globalization;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.LightCalculation;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;

namespace CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;

/// <summary>
/// ViewModel for the Spectrum tab (#816): sweeps the circuit across a wavelength
/// range and plots linear transmission |S|² per output pin — the standard
/// photonics spectrum view. Once a sweep has run, changing any sweep parameter
/// re-runs it automatically (debounced), so the plot stays live without a reload.
/// </summary>
public partial class WavelengthSpectrumViewModel : ObservableObject
{
    /// <summary>Debounce applied before a parameter change triggers an automatic re-sweep.</summary>
    internal static readonly TimeSpan DefaultAutoRefreshDelay = TimeSpan.FromMilliseconds(600);

    [ObservableProperty] private int _startNm = 1500;
    [ObservableProperty] private int _endNm = 1600;
    [ObservableProperty] private int _stepCount = 100;
    [ObservableProperty] private bool _isSweeping;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private PlotModel _plotModel = WavelengthSpectrumPlotBuilder.CreateEmptyPlotModel();

    /// <summary>
    /// Coherent interference mode (#1333): routed waveguides carry their propagation
    /// phase exp(-i·2π·n_eff(λ)·L/λ), so arm-length differences show as fringes.
    /// Mirrors <see cref="CAP_Core.Components.Connections.WaveguideConnectionManager.EnableCoherentPropagationPhase"/>
    /// on the configured canvas.
    /// </summary>
    [ObservableProperty] private bool _isCoherentInterference;

    /// <summary>True once a completed sweep is available (enables the plot and auto-refresh).</summary>
    [ObservableProperty] private bool _hasResult;

    /// <summary>Measured-spectrum overlay (#1335): CSV load + FSR/n_g extraction drawn on the same plot.</summary>
    public MeasuredSpectrumOverlayViewModel Overlay { get; }

    /// <summary>
    /// FSR readout lines under the plot (#1382) — one per simulated curve with
    /// at least two fringes, so a ring's FSR can be checked against
    /// FSR = λ²/(n_g·L) without reading it off the axis by eye.
    /// </summary>
    public ObservableCollection<FsrReadoutLine> FsrReadouts { get; } = new();

    /// <summary>True when at least one curve produced an FSR readout line.</summary>
    [ObservableProperty] private bool _hasFsrReadouts;

    /// <summary>Debounce used by the auto-refresh; tests set this to zero.</summary>
    internal TimeSpan AutoRefreshDelay { get; set; } = DefaultAutoRefreshDelay;

    /// <summary>The pending debounced auto-refresh, awaited by tests; null when idle.</summary>
    internal Task? PendingAutoRefresh { get; private set; }

    private readonly CAP_Core.ErrorConsoleService? _errorConsole;
    private readonly SemaphoreSlim _sweepGate = new(1, 1);
    private DesignCanvasViewModel? _canvas;
    private bool _suppressToggleRefresh;
    private CancellationTokenSource? _sweepCts;
    private CancellationTokenSource? _debounceCts;

    // Last rendered sweep, kept so the measured-spectrum overlay can be drawn
    // on top without re-running the simulation.
    private IReadOnlyList<TransmissionCurve>? _lastCurves;
    private IReadOnlyDictionary<Guid, string>? _lastPinNames;
    private string? _lastInputLabel;
    private double _lastDesignWavelengthNm;

    /// <summary>Initializes a new instance of <see cref="WavelengthSpectrumViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public WavelengthSpectrumViewModel(CAP_Core.ErrorConsoleService? errorConsole = null)
    {
        _errorConsole = errorConsole;
        Overlay = new MeasuredSpectrumOverlayViewModel(errorConsole);
        Overlay.OverlayChanged += (_, _) => RedrawPlotWithOverlay();
    }

    /// <summary>Configures the panel with the current canvas context.</summary>
    /// <param name="canvas">Canvas providing components and connections.</param>
    public void Configure(DesignCanvasViewModel? canvas)
    {
        _canvas = canvas;
        StatusText = "";
        HasResult = false;
        _lastCurves = null;
        _lastPinNames = null;
        _lastInputLabel = null;
        FsrReadouts.Clear();
        HasFsrReadouts = false;
        PlotModel = WavelengthSpectrumPlotBuilder.CreateEmptyPlotModel();
        // Sync the toggle from the canvas (e.g. a .lun just loaded with the flag on)
        // without triggering a refresh: HasResult is already false here.
        IsCoherentInterference = canvas?.ConnectionManager.EnableCoherentPropagationPhase ?? false;
    }

    /// <summary>Runs the wavelength sweep and updates the transmission plot.</summary>
    [RelayCommand]
    private Task RunSweep() => RunSweepInternalAsync();

    /// <summary>Cancels a running sweep.</summary>
    [RelayCommand]
    private void CancelSweep() => _sweepCts?.Cancel();

    partial void OnStartNmChanged(int value) => ScheduleAutoRefresh();
    partial void OnEndNmChanged(int value) => ScheduleAutoRefresh();
    partial void OnStepCountChanged(int value) => ScheduleAutoRefresh();

    partial void OnIsCoherentInterferenceChanged(bool value)
    {
        if (_canvas != null)
            _canvas.ConnectionManager.EnableCoherentPropagationPhase = value;
        if (!_suppressToggleRefresh)
            ScheduleAutoRefresh();
    }

    /// <summary>
    /// Re-syncs the toggle from the canvas flag after a .lun load restored it
    /// (the canvas instance survives loads, so Configure is not re-run). Never
    /// schedules a sweep — loading a design must not kick off a simulation.
    /// </summary>
    public void SyncCoherentToggleFromCanvas()
    {
        if (_canvas == null) return;
        _suppressToggleRefresh = true;
        try
        {
            IsCoherentInterference = _canvas.ConnectionManager.EnableCoherentPropagationPhase;
        }
        finally
        {
            _suppressToggleRefresh = false;
        }
    }

    /// <summary>
    /// Re-runs the sweep automatically after a parameter change — but only once
    /// the user has run a first sweep, so typing values before the first run
    /// doesn't kick off surprise simulations.
    /// </summary>
    private void ScheduleAutoRefresh()
    {
        if (!HasResult) return;
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        PendingAutoRefresh = AutoRefreshAsync(_debounceCts.Token);
    }

    private async Task AutoRefreshAsync(CancellationToken token)
    {
        try { await Task.Delay(AutoRefreshDelay, token); }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested) return;
        await RunSweepInternalAsync();
    }

    private async Task RunSweepInternalAsync()
    {
        if (_canvas == null) return;

        // A newer request supersedes a running sweep: cancel it, then take the gate.
        _sweepCts?.Cancel();
        await _sweepGate.WaitAsync();
        try
        {
            IsSweeping = true;
            _sweepCts = new CancellationTokenSource();
            await ExecuteSweepAsync(_sweepCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Spectrum.Cancelled");
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Spectrum sweep failed: {ex.Message}", ex);
            StatusText = string.Format(
                LocalizationService.Instance.Translate("Analysis.Common.Failed"), ex.Message);
        }
        finally
        {
            IsSweeping = false;
            _sweepCts?.Dispose();
            _sweepCts = null;
            _sweepGate.Release();
        }
    }

    private async Task ExecuteSweepAsync(CancellationToken token)
    {
        if (!TryCreateConfiguration(out var config)) return;

        var circuit = SpectrumSweepCircuitFactory.Create(_canvas!);
        if (circuit == null)
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Common.NoCircuit");
            return;
        }
        if (circuit.Ports.GetAllExternalInputs().Count == 0)
        {
            _errorConsole?.LogError(
                "Spectrum sweep aborted: no laser is switched on — turn the laser on at your input coupler.");
            StatusText = LocalizationService.Instance.Translate("Analysis.Spectrum.NoLight");
            return;
        }

        StatusText = string.Format(
            LocalizationService.Instance.Translate("Analysis.Spectrum.Running"), config!.StepCount);

        var sweeper = new WavelengthSweeper(new SystemMatrixBuilder(circuit.GridManager), circuit.Ports);
        var result = await sweeper.RunSweepAsync(config, circuit.GridManager, token);

        foreach (var warning in result.Warnings)
            _errorConsole?.LogWarning(warning);

        var curves = TransmissionSpectrumBuilder.Build(result, circuit.OutputCouplerPinIds);
        _lastCurves = curves;
        _lastPinNames = circuit.PinNames;
        _lastInputLabel = circuit.InputLabel;
        _lastDesignWavelengthNm = circuit.DesignWavelengthNm;
        RedrawPlotWithOverlay();
        UpdateFsrReadouts(curves, circuit.PinNames, circuit.InputLabel);
        HasResult = true;

        StatusText = curves.All(c => c.IsAtNoiseFloor)
            ? LocalizationService.Instance.Translate("Analysis.Spectrum.AllAtFloor")
            : string.Format(
                LocalizationService.Instance.Translate("Analysis.Spectrum.Complete"),
                result.DataPoints.Count);
    }

    /// <summary>
    /// Rebuilds the plot from the last sweep plus the current measured-spectrum
    /// overlay (if any). No-op when no sweep has been rendered yet — the overlay
    /// is only meaningful on top of a simulated curve.
    /// </summary>
    private void RedrawPlotWithOverlay()
    {
        if (_lastCurves == null || _lastPinNames == null) return;
        var pinNames = _lastPinNames;
        var inputLabel = _lastInputLabel;
        PlotModel = WavelengthSpectrumPlotBuilder.BuildPlotModel(
            _lastCurves,
            pinId => pinNames.TryGetValue(pinId, out var name)
                ? SpectrumLegendLabelBuilder.ComposeCurveLabel(inputLabel, name)
                : null,
            _lastDesignWavelengthNm,
            Overlay.Spectrum);
    }

    /// <summary>
    /// Rebuilds the FSR readout lines from a finished sweep (#1382). Labels match
    /// the plot legend so each line is attributable to its curve; curves without
    /// at least two fringes are skipped silently. Internal so tests can drive it
    /// with synthetic curves instead of a full simulation.
    /// </summary>
    internal void UpdateFsrReadouts(
        IReadOnlyList<TransmissionCurve> curves,
        IReadOnlyDictionary<Guid, string> pinNames,
        string? inputLabel)
    {
        FsrReadouts.Clear();
        var i18n = LocalizationService.Instance;
        foreach (var curve in curves)
        {
            var fsr = CurveFsrAnalyzer.Analyze(curve);
            if (fsr == null) continue;

            string outputLabel = pinNames.TryGetValue(curve.PinId, out var name)
                ? name
                : curve.PinId.ToString("N")[..8];
            string label = SpectrumLegendLabelBuilder.ComposeCurveLabel(inputLabel, outputLabel);
            string fsrText = fsr.MeanFsrNm.ToString("0.0", CultureInfo.InvariantCulture);
            string key = fsr.ExtremumKind == SpectrumExtremumKind.Peaks
                ? "Spectrum.Fsr.ReadoutPeaks"
                : "Spectrum.Fsr.ReadoutDips";
            string text = string.Format(
                CultureInfo.InvariantCulture, i18n.Translate(key), label, fsrText, fsr.ExtremumCount);
            FsrReadouts.Add(new FsrReadoutLine(
                label, fsr.MeanFsrNm, fsr.ExtremumCount, fsr.ExtremumKind, text));
        }
        HasFsrReadouts = FsrReadouts.Count > 0;
    }

    private bool TryCreateConfiguration(out WavelengthSweepConfiguration? config)
    {
        try
        {
            config = new WavelengthSweepConfiguration(StartNm, EndNm, StepCount);
            return true;
        }
        catch (ArgumentException ex)
        {
            config = null;
            StatusText = ex.Message;
            return false;
        }
    }
}
