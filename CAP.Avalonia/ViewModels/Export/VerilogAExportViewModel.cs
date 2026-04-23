using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Export;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// ViewModel for Verilog-A / SPICE export panel.
/// Allows exporting photonic circuits for co-simulation with electronic circuits.
/// </summary>
public partial class VerilogAExportViewModel : ObservableObject
{
    private readonly VerilogAExporter _exporter;
    private readonly VerilogAFileWriter _fileWriter;
    private readonly DesignCanvasViewModel _canvas;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _circuitName = "PhotonicCircuit";

    [ObservableProperty]
    private int _wavelengthNm = 1550;

    [ObservableProperty]
    private bool _includeTestBench = true;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _lastExportSucceeded;

    [ObservableProperty]
    private int _lastFileCount;

    /// <summary>
    /// Available wavelength options for S-parameter extraction.
    /// </summary>
    public IReadOnlyList<int> WavelengthOptions { get; } = new[] { 1310, 1550, 1625 };

    /// <summary>Initializes the ViewModel with required services.</summary>
    public VerilogAExportViewModel(
        VerilogAExporter exporter,
        VerilogAFileWriter fileWriter,
        DesignCanvasViewModel canvas)
    {
        _exporter = exporter;
        _fileWriter = fileWriter;
        _canvas = canvas;
        OutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LunimaExport");
    }

    /// <summary>
    /// Exports the current design to Verilog-A files.
    /// </summary>
    [RelayCommand]
    public async Task ExportAsync()
    {
        if (IsExporting) return;

        IsExporting = true;
        StatusText = "Exporting...";
        LastExportSucceeded = false;

        try
        {
            var components = _canvas.Components
                .Select(vm => vm.Component)
                .Where(c => c != null)
                .ToList();

            var connections = _canvas.Connections
                .Select(vm => vm.Connection)
                .Where(c => c != null)
                .ToList();

            if (components.Count == 0)
            {
                StatusText = "No components to export.";
                return;
            }

            var options = new VerilogAExportOptions
            {
                WavelengthNm = WavelengthNm,
                CircuitName = SanitizeCircuitName(CircuitName),
                IncludeTestBench = IncludeTestBench
            };

            var result = await Task.Run(() => _exporter.Export(components, connections, options));

            if (!result.Success)
            {
                StatusText = $"✗ Export failed: {result.ErrorMessage}";
                return;
            }

            await _fileWriter.WriteAsync(result, OutputDirectory);
            LastFileCount = result.TotalFileCount;
            LastExportSucceeded = true;
            StatusText = $"✓ Exported {result.TotalFileCount} files to {OutputDirectory}";
        }
        catch (InvalidOperationException ex)
        {
            // Design-level issue surfaced by the exporter (orphan pin, unmapped
            // component type, missing ports, …).
            StatusText = $"✗ Export rejected: {ex.Message}";
        }
        catch (IOException ex)
        {
            StatusText = $"✗ Could not write files: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText = $"✗ Access denied: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>
    /// Opens the output directory in the file explorer.
    /// </summary>
    [RelayCommand]
    public void OpenOutputDirectory()
    {
        if (!Directory.Exists(OutputDirectory)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = OutputDirectory,
                UseShellExecute = true
            });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            StatusText = $"Could not open output directory: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = $"Could not open output directory: {ex.Message}";
        }
    }

    private static string SanitizeCircuitName(string name)
    {
        var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        return string.IsNullOrEmpty(sanitized) ? "PhotonicCircuit" : sanitized;
    }
}
