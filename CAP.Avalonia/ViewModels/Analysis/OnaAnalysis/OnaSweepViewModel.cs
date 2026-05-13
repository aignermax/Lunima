using System.Numerics;
using CAP_Core;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;

/// <summary>
/// ViewModel for the ONA (Optical Network Analyzer) wavelength-sweep panel.
/// Sweeps the simulation across a wavelength range and plots insertion loss vs wavelength.
/// </summary>
public partial class OnaSweepViewModel : ObservableObject
{
    [ObservableProperty] private int _startNm = 1500;
    [ObservableProperty] private int _endNm = 1600;
    [ObservableProperty] private int _stepCount = 21;
    [ObservableProperty] private bool _isSweeping;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _warningText = "";
    [ObservableProperty] private PlotModel _plotModel = CreateEmptyPlotModel();

    /// <summary>True when a completed sweep result is available to export.</summary>
    public bool HasResult => _lastResult != null;

    private readonly ErrorConsoleService? _errorConsole;
    private DesignCanvasViewModel? _canvas;
    private WavelengthSweepResult? _lastResult;

    /// <summary>File dialog service for CSV export. Set by MainViewModel.</summary>
    public Services.IFileDialogService? FileDialogService { get; set; }

    /// <summary>Initializes a new instance of <see cref="OnaSweepViewModel"/>.</summary>
    public OnaSweepViewModel(ErrorConsoleService? errorConsole = null)
    {
        _errorConsole = errorConsole;
    }

    /// <summary>Provides the canvas reference needed to build the simulation grid.</summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
    }

    /// <summary>Runs a wavelength sweep and updates the insertion-loss plot.</summary>
    [RelayCommand]
    private async Task RunSweep()
    {
        if (_canvas == null || IsSweeping) return;

        IsSweeping = true;
        StatusText = "Preparing ONA sweep...";
        WarningText = "";
        _lastResult = null;
        OnPropertyChanged(nameof(HasResult));

        try
        {
            var config = new WavelengthSweepConfiguration(StartNm, EndNm, StepCount);
            var (gridManager, portManager) = BuildSimulationGrid();
            if (gridManager == null)
            {
                StatusText = "No components on canvas.";
                return;
            }

            var builder = new SystemMatrixBuilder(gridManager);
            var sweeper = new WavelengthSweeper(builder, portManager);
            var cts = new CancellationTokenSource();

            StatusText = $"Running ONA sweep ({StepCount} steps)...";
            _lastResult = await sweeper.RunSweepAsync(config, gridManager, cts.Token);
            OnPropertyChanged(nameof(HasResult));

            if (_lastResult.Warnings.Count > 0)
                WarningText = string.Join("\n", _lastResult.Warnings);

            UpdatePlotModel(_lastResult);
            StatusText = $"ONA sweep complete: {_lastResult.DataPoints.Count} points";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Sweep cancelled.";
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"ONA sweep failed: {ex.Message}", ex);
            StatusText = $"Sweep failed: {ex.Message}";
        }
        finally
        {
            IsSweeping = false;
        }
    }

    /// <summary>Exports the last sweep result as CSV.</summary>
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
                    "Export ONA Results", "csv", "CSV Files|*.csv|All Files|*.*");
            }

            if (path == null) { StatusText = "Export cancelled"; return; }

            await File.WriteAllTextAsync(path, _lastResult.GenerateCsvContent());
            StatusText = $"Exported to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"ONA export failed: {ex.Message}", ex);
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    private (GridManager? gridManager, PhysicalExternalPortManager portManager) BuildSimulationGrid()
    {
        if (_canvas == null || _canvas.Components.Count == 0) return (null, new PhysicalExternalPortManager());

        var tileManager = new ComponentListTileManager();
        foreach (var compVm in _canvas.Components)
            tileManager.AddComponent(compVm.Component);

        var portManager = new PhysicalExternalPortManager();
        ConfigureLightSources(portManager);

        var gridManager = GridManager.CreateForSimulation(
            tileManager, _canvas.ConnectionManager, portManager);
        return (gridManager, portManager);
    }

    private void ConfigureLightSources(PhysicalExternalPortManager portManager)
    {
        if (_canvas == null) return;
        foreach (var compVm in _canvas.Components)
        {
            if (compVm.TemplateName == null) continue;
            if (!compVm.TemplateName.Contains("Coupler", StringComparison.OrdinalIgnoreCase)) continue;
            if (compVm.TemplateName.Contains("Directional", StringComparison.OrdinalIgnoreCase)) continue;

            var laserConfig = compVm.LaserConfig;
            double power = laserConfig?.InputPower ?? 1.0;

            foreach (var pin in compVm.Component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light) continue;
                var input = new ExternalInput(
                    $"ona_{compVm.Component.Identifier}_{pin.Name}",
                    LaserType.Red, 0, new Complex(power, 0));
                portManager.AddLightSource(input, pin.LogicalPin.IDInFlow);
            }
        }
    }

    private void UpdatePlotModel(WavelengthSweepResult result)
    {
        var model = CreateEmptyPlotModel();
        var wavelengths = result.GetWavelengthValues();

        int seriesCount = 0;
        foreach (var pinId in result.MonitoredPinIds)
        {
            var losses = result.GetInsertionLossSeriesForPin(pinId);
            if (losses.All(v => v <= WavelengthDataPoint.MinInsertionLossDb + 1)) continue;

            var series = new LineSeries
            {
                Title = $"Pin {pinId.ToString("N")[..6]}",
                StrokeThickness = 1.5,
            };

            for (int i = 0; i < wavelengths.Length; i++)
                series.Points.Add(new DataPoint(wavelengths[i], losses[i]));

            model.Series.Add(series);
            if (++seriesCount >= 8) break; // cap series to prevent plot overload
        }

        model.InvalidatePlot(true);
        PlotModel = model;
    }

    private static PlotModel CreateEmptyPlotModel()
    {
        var model = new PlotModel { Title = "ONA — Insertion Loss", Background = OxyColors.Transparent };
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Wavelength (nm)",
            MajorGridlineStyle = LineStyle.Dot,
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Insertion Loss (dB)",
            MajorGridlineStyle = LineStyle.Dot,
        });
        return model;
    }
}
