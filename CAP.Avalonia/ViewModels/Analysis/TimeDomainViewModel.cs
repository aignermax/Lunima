using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// ViewModel for the time-domain (transient) simulation panel.
/// Sweeps S-parameters over a wavelength grid, performs IFFT to obtain impulse
/// responses, convolves with a user-selected input pulse, and reports output traces.
/// </summary>
public partial class TimeDomainViewModel : ObservableObject
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

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// True = Time Domain (Transient) mode active; False = Frequency Domain (CW) mode active.
    /// Controls which simulation mode is visible in the panel.
    /// </summary>
    [ObservableProperty]
    private bool _isTimeDomainMode = true;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _resultText = "";

    private readonly CAP_Core.ErrorConsoleService? _errorConsole;
    private DesignCanvasViewModel? _canvas;
    private TimeDomainResult? _lastResult;

    /// <summary>Initializes a new instance of <see cref="TimeDomainViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public TimeDomainViewModel(CAP_Core.ErrorConsoleService? errorConsole = null)
    {
        _errorConsole = errorConsole;
    }

    /// <summary>File dialog service for CSV export. Set by MainViewModel.</summary>
    public Services.IFileDialogService? FileDialogService { get; set; }

    /// <summary>Configures the panel with the current canvas context.</summary>
    public void Configure(DesignCanvasViewModel? canvas)
    {
        _canvas = canvas;
        ResultText = "";
        StatusText = "";
        _lastResult = null;
    }

    /// <summary>Runs the time-domain simulation pipeline.</summary>
    [RelayCommand]
    private async Task RunTransient()
    {
        if (_canvas == null || _canvas.Components.Count == 0)
        {
            StatusText = "No circuit loaded.";
            return;
        }

        if (IsRunning) return;
        IsRunning = true;
        StatusText = "Building impulse responses…";
        ResultText = "";
        _lastResult = null;

        try
        {
            var result = await Task.Run(() => RunSimulationCore());
            _lastResult = result;
            ResultText = TimeDomainResultFormatter.FormatResult(result);
            StatusText = $"Done — {result.PinTraces.Count} output pin(s)";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = $"Cannot run: {ex.Message}";
            _errorConsole?.LogError($"Time-domain simulation blocked: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _errorConsole?.LogError($"Time-domain simulation failed: {ex.Message}", ex);
            StatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Exports the last simulation result to a CSV file.</summary>
    [RelayCommand]
    private async Task ExportCsv()
    {
        if (_lastResult == null) return;

        try
        {
            string? path = null;
            if (FileDialogService != null)
            {
                path = await FileDialogService.ShowSaveFileDialogAsync(
                    "Export Time-Domain Traces",
                    "csv",
                    "CSV Files|*.csv|All Files|*.*");
            }

            if (path == null)
            {
                StatusText = "Export cancelled";
                return;
            }

            var csv = TimeDomainResultFormatter.BuildCsvContent(_lastResult);
            await File.WriteAllTextAsync(path, csv);
            StatusText = $"Exported to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"CSV export failed: {ex.Message}", ex);
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    private TimeDomainResult RunSimulationCore()
    {
        var tileManager = new ComponentListTileManager();
        foreach (var compVm in _canvas!.Components)
            tileManager.AddComponent(compVm.Component);

        var portManager = new PhysicalExternalPortManager();
        ConfigureLightSources(portManager);

        var gridManager = GridManager.CreateForSimulation(
            tileManager, _canvas.ConnectionManager, portManager);

        var builder = new SystemMatrixBuilder(gridManager);
        var simulator = new TimeDomainSimulator(builder);

        var timeDef = TimeSignalDefinition.FromWavelengthSweep(
            CenterWavelengthNm, SpanNm, FreqPoints);

        var inputSignals = BuildInputSignals(portManager, timeDef);
        return simulator.Run(inputSignals, timeDef, CenterWavelengthNm, SpanNm, FreqPoints);
    }

    private void ConfigureLightSources(PhysicalExternalPortManager portManager)
    {
        foreach (var compVm in _canvas!.Components)
        {
            if (compVm.TemplateName == null) continue;
            if (!compVm.TemplateName.Contains("Coupler", StringComparison.OrdinalIgnoreCase)) continue;
            if (compVm.TemplateName.Contains("Directional", StringComparison.OrdinalIgnoreCase)) continue;

            var laserConfig = compVm.LaserConfig;
            double power = laserConfig?.InputPower ?? 1.0;
            var laserType = laserConfig?.WavelengthNm == StandardWaveLengths.GreenNM
                ? LaserType.Green
                : laserConfig?.WavelengthNm == StandardWaveLengths.BlueNM
                    ? LaserType.Blue
                    : LaserType.Red;

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

    private Dictionary<Guid, double[]> BuildInputSignals(
        PhysicalExternalPortManager portManager, TimeSignalDefinition timeDef)
    {
        double centerSeconds = timeDef.DurationSeconds * 0.3;
        double sigmaSeconds = PulseSigmaPs * 1e-12;
        double centerInput = PulseCenterPs * 1e-12;
        double pulseCenter = Math.Max(centerInput, 3 * sigmaSeconds);

        var signals = new Dictionary<Guid, double[]>();
        foreach (var usedInput in portManager.GetUsedExternalInputs())
        {
            double amplitude = Math.Sqrt(usedInput.Input.InFlowPower.Magnitude);
            var pulse = timeDef.CreateGaussianPulse(pulseCenter, sigmaSeconds, amplitude);
            signals[usedInput.AttachedComponentPinId] = pulse;
        }
        return signals;
    }

}
