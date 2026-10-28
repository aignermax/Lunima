using System.Globalization;
using CAP_Core.Analysis.MonteCarloAnalysis;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.LightCalculation;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Canvas;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;

namespace CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;

/// <summary>
/// ViewModel for the Monte-Carlo fabrication-variance tab: samples correlated
/// wafer-level Δwidth/Δthickness deviations from the active process tolerances,
/// perturbs every component's S-matrix accordingly, re-simulates N times, and
/// shows either the spectral envelope over the nominal curve or the
/// eye-openness distribution — a statistical yield view of the design.
/// </summary>
public partial class MonteCarloViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Selectable per-run metrics.</summary>
    public IReadOnlyList<MonteCarloMetricOption> Metrics { get; } = new[]
    {
        new MonteCarloMetricOption(
            MonteCarloMetric.SpectrumEnvelope,
            LocalizationService.Instance.Translate("MonteCarlo.MetricSpectrum")),
        new MonteCarloMetricOption(
            MonteCarloMetric.EyeOpenness,
            LocalizationService.Instance.Translate("MonteCarlo.MetricEye")),
    };

    [ObservableProperty] private MonteCarloMetricOption _selectedMetric;
    [ObservableProperty] private int _runCount = MonteCarloConfiguration.DefaultRunCount;
    [ObservableProperty] private double _widthSigmaNm = ProcessTolerances.DefaultWidthSigmaNm;
    [ObservableProperty] private double _thicknessSigmaNm = ProcessTolerances.DefaultThicknessSigmaNm;
    [ObservableProperty] private int _seed = MonteCarloConfiguration.DefaultSeed;
    [ObservableProperty] private int _startNm = 1500;
    [ObservableProperty] private int _endNm = 1600;
    [ObservableProperty] private int _stepCount = 21;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private PlotModel _plotModel;

    /// <summary>True once a completed Monte-Carlo result is shown.</summary>
    public bool HasResult => !string.IsNullOrEmpty(SummaryText);

    /// <summary>True when the spectrum metric is selected (shows the wavelength-range inputs).</summary>
    public bool IsSpectrumMetric => SelectedMetric.Metric == MonteCarloMetric.SpectrumEnvelope;

    /// <summary>
    /// Supplies the active PDK process so its declared fabrication tolerances
    /// pre-fill the sigma inputs. Wired by the main view model.
    /// </summary>
    public Func<ActiveProcessSelection?>? ActiveProcessProvider { get; set; }

    private readonly CAP_Core.ErrorConsoleService? _errorConsole;
    private readonly IMonteCarloSpectrumRunner _spectrumRunner;
    private readonly IMonteCarloEyeRunner _eyeRunner;
    private readonly MonteCarloRunLifetime _lifetime = new();
    private DesignCanvasViewModel? _canvas => _lifetime.Canvas;
    private Task? _retirement;

    /// <summary>Initializes a new instance of <see cref="MonteCarloViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public MonteCarloViewModel(CAP_Core.ErrorConsoleService? errorConsole = null,
        IMonteCarloSpectrumRunner? spectrumRunner = null, ApplicationWorkCoordinator? application = null,
        IMonteCarloEyeRunner? eyeRunner = null)
    {
        _errorConsole = errorConsole;
        _spectrumRunner = spectrumRunner ?? new MonteCarloSpectrumRunner();
        _eyeRunner = eyeRunner ?? new MonteCarloEyeRunner();
        _selectedMetric = Metrics[0];
        _plotModel = MonteCarloPlotBuilder.CreateEmptyModel("Monte Carlo");
        _lifetime.Register(application, StopAsync);
    }

    /// <summary>Provides the canvas reference needed to build the simulation circuit.</summary>
    public void Configure(DesignCanvasViewModel canvas) => _lifetime.Configure(canvas);

    public Task StopAsync() => _retirement ??= RetireAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private async Task RetireAsync()
    {
        try { await _lifetime.StopAsync(); }
        finally { ActiveProcessProvider = null; _lifetime.Release(); }
    }

    /// <summary>
    /// Reloads the sigma inputs from the active process tolerances. Call when the
    /// panel is shown so the fields track the process selected in the meantime.
    /// </summary>
    public void RefreshTolerancesFromProcess()
    {
        if (_lifetime.IsStopping) return;
        var tolerances = ActiveProcessProvider?.Invoke()?.Fingerprint?.Tolerances;
        if (tolerances == null || _lifetime.IsStopping) return;
        WidthSigmaNm = tolerances.WidthSigmaNm;
        if (_lifetime.IsStopping) return;
        ThicknessSigmaNm = tolerances.ThicknessSigmaNm;
    }

    partial void OnSelectedMetricChanged(MonteCarloMetricOption value)
        => OnPropertyChanged(nameof(IsSpectrumMetric));

    partial void OnSummaryTextChanged(string value)
        => OnPropertyChanged(nameof(HasResult));

    /// <summary>Runs the Monte-Carlo fabrication-variance analysis.</summary>
    [RelayCommand]
    private Task RunAnalysis() => _lifetime.RunAsync(RunAnalysisCoreAsync);

    private async Task RunAnalysisCoreAsync(CancellationToken stop)
    {
        if (IsRunning) return;
        AnalysisSweepOperation? operation = null;
        try
        {
            operation = _lifetime.Begin(stop);
            if (operation == null) return;
            operation.ThrowIfObsolete();
            if (_canvas!.Components.Count == 0)
            {
                StatusText = LocalizationService.Instance.Translate("Analysis.Common.NoCircuit");
                return;
            }
            var components = CollectPhysicalComponents();
            if (components.Count == 0)
            {
                StatusText = LocalizationService.Instance.Translate("Analysis.Mc.NoPhysicalComponents");
                return;
            }
            var config = new MonteCarloConfiguration(RunCount, Seed);
            var tolerances = new ProcessTolerances(WidthSigmaNm, ThicknessSigmaNm);
            var spectrum = IsSpectrumMetric;
            var sweep = spectrum ? new WavelengthSweepConfiguration(StartNm, EndNm, StepCount) : null;
            IsRunning = true;
            operation.ThrowIfObsolete();
            ProgressPercent = 0;
            operation.ThrowIfObsolete();
            SummaryText = "";
            operation.ThrowIfObsolete();
            var progress = new Progress<MonteCarloProgress>(p =>
            {
                if (!operation.CanPublish) return;
                ProgressPercent = 100.0 * p.CompletedRuns / p.TotalRuns;
                if (!operation.CanPublish) return;
                StatusText = string.Format(
                    LocalizationService.Instance.Translate("Analysis.Mc.Running"),
                    p.CompletedRuns, p.TotalRuns);
            });

            if (spectrum)
                await RunSpectrumAnalysis(config, sweep!, components, tolerances, progress, operation);
            else
                await RunEyeAnalysis(config, components, tolerances, progress, operation);
        }
        catch (OperationCanceledException)
        {
            // User cancellation publishes immediate feedback; retirement and obsolete
            // document completions cannot write into the current panel.
        }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError(ex is NonConvergentCircuitException
                ? $"Monte-Carlo analysis blocked: {ex.Message}" : $"Monte-Carlo analysis failed: {ex.Message}", ex);
            if (!operation.CanPublish) return;
            StatusText = ex is NonConvergentCircuitException physics
                ? NonConvergentCircuitMessageFormatter.Format(physics)
                : string.Format(LocalizationService.Instance.Translate("Analysis.Common.Failed"), ex.Message);
        }
        finally
        {
            try { if (operation != null) IsRunning = false; }
            finally { await _lifetime.EndAsync(operation); }
        }
    }

    /// <summary>Cancels the running analysis after the current run finishes.</summary>
    [RelayCommand]
    private void CancelAnalysis()
    {
        if (_lifetime.Cancel()) StatusText = LocalizationService.Instance.Translate("Analysis.Mc.Cancelled");
    }

    private async Task RunSpectrumAnalysis(
        MonteCarloConfiguration config, WavelengthSweepConfiguration sweepConfig,
        IReadOnlyList<Component> components, ProcessTolerances tolerances,
        IProgress<MonteCarloProgress> progress, AnalysisSweepOperation operation)
    {
        operation.ThrowIfObsolete();
        var (sampler, error) = MonteCarloSpectrumSampler.Create(_canvas!, sweepConfig, config,
            components, tolerances);
        if (sampler == null)
        {
            StatusText = error ?? "";
            return;
        }

        operation.ThrowIfObsolete();
        var completed = await _spectrumRunner.RunAsync(sampler.Snapshot, progress, operation.Token);
        operation.ThrowIfObsolete();
        var result = completed.Samples;

        PlotModel = MonteCarloPlotBuilder.BuildEnvelopePlot(
            sampler.Wavelengths, result, sampler.PinName(completed.SelectedOutputPinId));
        operation.ThrowIfObsolete();
        SummaryText = FormatSpectrumSummary(result, components.Count, tolerances);
        operation.ThrowIfObsolete();
        StatusText = string.Format(
            LocalizationService.Instance.Translate("Analysis.Mc.Complete"), config.RunCount);
    }

    private async Task RunEyeAnalysis(
        MonteCarloConfiguration config, IReadOnlyList<Component> components, ProcessTolerances tolerances,
        IProgress<MonteCarloProgress> progress, AnalysisSweepOperation operation)
    {
        operation.ThrowIfObsolete();
        var resolution = AnalysisOutputResolver.Resolve(_canvas!);
        if (ReportInvalidDesignation(resolution)) return;

        var captured = new MonteCarloEyeSampler(_canvas!, resolution).Capture(config, components, tolerances);
        operation.ThrowIfObsolete();
        var result = await _eyeRunner.RunAsync(captured, progress, operation.Token);
        operation.ThrowIfObsolete();

        var samples = result.GetSamplesAtIndex(0);
        var histogram = DistributionHistogram.Create(samples);
        PlotModel = MonteCarloPlotBuilder.BuildHistogramPlot(histogram, result.NominalCurve[0]);
        operation.ThrowIfObsolete();
        SummaryText = FormatEyeSummary(result, samples, components.Count, tolerances);
        operation.ThrowIfObsolete();
        StatusText = string.Format(
            LocalizationService.Instance.Translate("Analysis.Mc.Complete"), config.RunCount);
    }

    private bool ReportInvalidDesignation(AnalysisOutputResolution resolution)
    {
        if (resolution.State == AnalysisOutputState.DesignatedMissing)
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Output.DesignatedMissing");
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

    /// <summary>Every fabricated component participates; virtual analysis tools do not.</summary>
    private List<Component> CollectPhysicalComponents()
        => SimulationService.GetAllComponentsRecursively(_canvas!.Components)
            .Where(component => !component.IsAnalysisTool)
            .ToList();

    private static string FormatSpectrumSummary(MonteCarloResult result, int componentCount, ProcessTolerances tolerances)
    {
        var inv = CultureInfo.InvariantCulture;
        double worstDip = result.GetMinCurve().Min();
        double nominalMin = result.NominalCurve.Min();
        return string.Join(Environment.NewLine,
            string.Format(inv, "Varied components:    {0}", componentCount),
            string.Format(inv, "Wafer sigma:          Δw {0:F1} nm / Δt {1:F1} nm", tolerances.WidthSigmaNm, tolerances.ThicknessSigmaNm),
            string.Format(inv, "Nominal worst IL:     {0:F2} dB", nominalMin),
            string.Format(inv, "Monte-Carlo worst IL: {0:F2} dB", worstDip));
    }

    private static string FormatEyeSummary(MonteCarloResult result, double[] samples, int componentCount, ProcessTolerances tolerances)
    {
        var inv = CultureInfo.InvariantCulture;
        double openFraction = samples.Count(s => s > 0) / (double)samples.Length;
        return string.Join(Environment.NewLine,
            string.Format(inv, "Varied components:   {0}", componentCount),
            string.Format(inv, "Wafer sigma:         Δw {0:F1} nm / Δt {1:F1} nm", tolerances.WidthSigmaNm, tolerances.ThicknessSigmaNm),
            string.Format(inv, "Nominal eye height:  {0:E3}", result.NominalCurve[0]),
            string.Format(inv, "p5 / p50 / p95:      {0:E3} / {1:E3} / {2:E3}",
                result.GetPercentileCurve(5)[0],
                result.GetPercentileCurve(50)[0],
                result.GetPercentileCurve(95)[0]),
            string.Format(inv, "Open-eye yield:      {0:P1}", openFraction));
    }
}
