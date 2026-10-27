using System.Numerics;
using System.Text;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// ViewModel for the parameter sweep UI panel.
/// Runs a sweep by varying a component slider across a range,
/// running simulation at each step, and collecting output power data.
/// </summary>
public partial class ParameterSweepViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Services.Lifetime.ShellWorkScope _work = new();
    private readonly IParameterSweepRunner _runner;
    private AnalysisSweepOperation? _activeSweep;
    private long _configurationVersion;
    private CAP_Core.Documents.DocumentIdentity? _configuredDocument;
    private Task? _retirement;

    public Task StopAsync() => _retirement ??= RetireAsync();
    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RetireAsync()
    {
        try { await _work.StopAsync(); }
        finally
        {
            _canvas = null;
            _targetComponent = null;
            _configuredDocument = null;
            _lastResult = null;
            FileDialogService = null;
        }
    }

    [ObservableProperty]
    private double _startValue;

    [ObservableProperty]
    private double _endValue = 360;

    [ObservableProperty]
    private int _stepCount = 20;

    [ObservableProperty]
    private bool _isSweeping;

    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private string _statusText = "";

    private readonly ErrorConsoleService? _errorConsole;
    private DesignCanvasViewModel? _canvas;
    private ComponentViewModel? _targetComponent;
    private SweepResult? _lastResult;

    /// <summary>Initializes a new instance of <see cref="ParameterSweepViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public ParameterSweepViewModel(ErrorConsoleService? errorConsole = null,
        IParameterSweepRunner? runner = null, Services.Lifetime.ApplicationWorkCoordinator? applicationWork = null)
    {
        _errorConsole = errorConsole;
        _runner = runner ?? new ParameterSweepRunner();
        _work.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>
    /// File dialog service for CSV export. Set by MainViewModel.
    /// </summary>
    public Services.IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// Configures the sweep panel for the given component.
    /// Pre-fills range from the component's slider min/max.
    /// </summary>
    public void ConfigureForComponent(ComponentViewModel? component, DesignCanvasViewModel? canvas)
    {
        if (_work.IsStopping) return;
        var version = ++_configurationVersion;
        _activeSweep?.Cancel();
        _canvas = canvas;
        _targetComponent = component;
        _configuredDocument = canvas?.Document.CaptureIdentity();
        _lastResult = null;
        ResultText = "";
        if (_work.IsStopping || version != _configurationVersion) return;
        StatusText = "";
        if (_work.IsStopping || version != _configurationVersion) return;

        if (component != null && component.HasSliders)
        {
            StartValue = component.SliderMin;
            if (_work.IsStopping || version != _configurationVersion) return;
            EndValue = component.SliderMax;
        }
    }

    [RelayCommand]
    private Task RunSweep() => _work.RunAsync(RunSweepCoreAsync);

    private async Task RunSweepCoreAsync(CancellationToken stop)
    {
        if (_canvas == null || _targetComponent == null || !_targetComponent.HasSliders)
            return;

        if (IsSweeping) return;
        var canvas = _canvas;
        var target = _targetComponent;
        var version = _configurationVersion;
        AnalysisSweepOperation? operation = null;

        try
        {
            operation = new AnalysisSweepOperation(_work, canvas.Document,
                () => version == _configurationVersion && ReferenceEquals(_targetComponent, target)
                    && canvas.Components.Contains(target), stop, stop, _configuredDocument);
            _activeSweep = operation;
            operation.ThrowIfObsolete();
            var config = new SweepConfiguration(new SweepParameter(target.Component, 0, target.SliderLabel),
                StartValue, EndValue, StepCount, 1550);
            var pinNameMap = BuildPinNameMap();
            IsSweeping = true;
            operation.ThrowIfObsolete();
            StatusText = LocalizationService.Instance.Translate("Analysis.Sweep.Running");
            operation.ThrowIfObsolete();
            ResultText = "";
            operation.ThrowIfObsolete();
            _lastResult = null;
            var points = new List<SweepDataPoint>();
            if (canvas.Components.Count > 0 && canvas.Connections.Count > 0)
            {
                var (grid, ports) = BuildSimulationGrid(canvas);
                operation.ThrowIfObsolete();
                var progress = new Progress<int>(step =>
                {
                    if (!ReferenceEquals(_activeSweep, operation) || !operation.CanPublish) return;
                    StatusText = string.Format(
                        LocalizationService.Instance.Translate("Analysis.Sweep.Step"), step, config.StepCount);
                });
                points = await _runner.RunAsync(config, grid, ports, operation.Token, progress);
            }
            operation.ThrowIfObsolete();
            _activeSweep = null; // Posted progress must not overwrite final feedback.
            operation.TryPublish(() =>
            {
                _lastResult = new SweepResult(config, points,
                    points.Count > 0 ? points[0].OutputPowers.Keys.ToList() : []);
                ResultText = FormatResultsTable(_lastResult, pinNameMap);
                if (operation.CanPublish)
                    StatusText = string.Format(LocalizationService.Instance.Translate("Analysis.Sweep.Complete"), points.Count);
            });
        }
        catch (OperationCanceledException)
        {
            // A replacement selection owns its feedback; retirement cannot publish into it.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError($"Parameter sweep failed: {ex.Message}", ex);
            if (!operation.CanPublish) return;
            StatusText = string.Format(
                LocalizationService.Instance.Translate("Analysis.Sweep.Failed"), ex.Message);
        }
        finally
        {
            try { IsSweeping = false; }
            finally
            {
                if (ReferenceEquals(_activeSweep, operation)) _activeSweep = null;
                if (operation != null) await operation.DisposeAsync();
            }
        }
    }

    [RelayCommand]
    private Task ExportCsv() => _work.RunAsync(ExportCsvCoreAsync);

    private async Task ExportCsvCoreAsync(CancellationToken stop)
    {
        if (_lastResult == null || _canvas == null) return;
        var result = _lastResult;
        var version = _configurationVersion;
        AnalysisSweepOperation? operation = null;

        try
        {
            operation = new AnalysisSweepOperation(_work, _canvas.Document,
                () => version == _configurationVersion && ReferenceEquals(result, _lastResult),
                stop, stop, _configuredDocument);
            operation.ThrowIfObsolete();
            var csv = SweepCsvFormatter.GenerateCsvContent(result);

            string? path = null;
            if (FileDialogService != null)
            {
                path = await FileDialogService.ShowSaveFileDialogAsync(
                    "Export Sweep Results",
                    "csv",
                    $"CSV Files|*.csv|All Files|*.*");
            }

            operation.ThrowIfObsolete();
            if (path == null)
            {
                StatusText = LocalizationService.Instance.Translate("Analysis.Common.ExportCancelled");
                return;
            }

            await File.WriteAllTextAsync(path, csv, operation.Token);
            operation.ThrowIfObsolete();
            StatusText = string.Format(
                LocalizationService.Instance.Translate("Analysis.Common.ExportedTo"), Path.GetFileName(path));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError($"Failed to export sweep results: {ex.Message}", ex);
            if (!operation.CanPublish) return;
            StatusText = string.Format(
                LocalizationService.Instance.Translate("Analysis.Common.ExportFailed"), ex.Message);
        }
        finally { if (operation != null) await operation.DisposeAsync(); }
    }

    /// <summary>
    /// Assembles caller-owned simulation inputs for immediate detached capture.
    /// </summary>
    private (GridManager Grid, PhysicalExternalPortManager Ports) BuildSimulationGrid(DesignCanvasViewModel canvas)
    {
        var tileManager = new ComponentListTileManager();
        foreach (var compVm in canvas.Components)
            tileManager.AddComponent(compVm.Component);

        var portManager = new PhysicalExternalPortManager();
        ConfigureLightSources(canvas, portManager);

        return (GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager), portManager);
    }

    private static void ConfigureLightSources(DesignCanvasViewModel canvas, PhysicalExternalPortManager portManager)
    {
        foreach (var compVm in canvas.Components)
        {
            if (!compVm.IsLightSource) continue;

            // Laser off = output coupler (listen-only, #690) — inject no light.
            if (compVm.IsLaserOff) continue;

            var laserConfig = compVm.LaserConfig;
            double power = laserConfig?.InputPower ?? 1.0;
            var laserType = SimulationService.GetLaserTypeForWavelength(
                laserConfig?.WavelengthNm ?? StandardWaveLengths.RedNM);

            foreach (var pin in compVm.Component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light) continue;
                var input = new ExternalInput(
                    $"src_{compVm.Component.Identifier}_{pin.Name}",
                    laserType, 0, new Complex(power, 0));
                portManager.AddLightSource(input, pin.LogicalPin.IDInFlow);
            }
        }
    }

    private Dictionary<Guid, string> BuildPinNameMap()
    {
        var map = new Dictionary<Guid, string>();
        if (_canvas == null) return map;

        foreach (var compVm in _canvas.Components)
        {
            foreach (var pin in compVm.Component.PhysicalPins)
            {
                if (pin.LogicalPin == null) continue;
                var shortName = $"{compVm.Component.Identifier}.{pin.Name}";
                // Truncate for table display
                if (shortName.Length > 16) shortName = shortName[..16];
                map[pin.LogicalPin.IDInFlow] = shortName + ".in";
                map[pin.LogicalPin.IDOutFlow] = shortName + ".out";
            }
        }
        return map;
    }

    private string FormatResultsTable(SweepResult result, Dictionary<Guid, string> pinNameMap)
    {
        if (result.DataPoints.Count == 0)
            return LocalizationService.Instance.Translate("Analysis.Sweep.NoDataPoints");

        // Only show pins that have non-zero power in at least one step
        var activePins = result.MonitoredPinIds
            .Where(pid => result.DataPoints.Any(dp =>
                dp.OutputPowers.TryGetValue(pid, out double p) && p > 1e-10))
            .Take(6) // Limit columns for readability
            .ToList();

        if (activePins.Count == 0)
            return LocalizationService.Instance.Translate("Analysis.Sweep.NoSignal");

        var sb = new StringBuilder();

        // Header
        sb.Append("Value".PadRight(8));
        foreach (var pid in activePins)
        {
            var name = pinNameMap.GetValueOrDefault(pid, pid.ToString()[..6]);
            if (name.Length > 12) name = name[..12];
            sb.Append(name.PadRight(14));
        }
        sb.AppendLine();

        // Separator
        sb.AppendLine(new string('-', 8 + activePins.Count * 14));

        // Data rows
        foreach (var dp in result.DataPoints)
        {
            sb.Append($"{dp.ParameterValue,7:F1} ");
            foreach (var pid in activePins)
            {
                dp.OutputPowers.TryGetValue(pid, out double power);
                sb.Append($"{power,13:F6} ");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
