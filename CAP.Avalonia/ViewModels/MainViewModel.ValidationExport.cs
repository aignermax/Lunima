using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.LightCalculation.Validation;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Partial class for validation report export functionality.
/// </summary>
public partial class MainViewModel
{
    private readonly ValidationReportExporter _validationExporter = new();

    /// <summary>
    /// The most recent S-Matrix validation result from a simulation run.
    /// </summary>
    [ObservableProperty]
    private SMatrixValidationResult? _lastValidationResult;

    /// <summary>
    /// The wavelength in nm from the most recent simulation run.
    /// </summary>
    [ObservableProperty]
    private int? _lastSimulationWavelengthNm;

    /// <summary>
    /// When true, validation reports are auto-exported after each simulation.
    /// </summary>
    [ObservableProperty]
    private bool _autoExportValidation;

    /// <summary>
    /// Exports the last validation result to a JSON file via save dialog.
    /// </summary>
    [RelayCommand]
    private async Task ExportValidationReport()
    {
        if (FileDialogService == null)
        {
            StatusText = "Export not available";
            return;
        }

        if (LastValidationResult == null)
        {
            StatusText = "No validation result available — run a simulation first";
            return;
        }

        var filePath = await FileDialogService.ShowSaveFileDialogAsync(
            "Export Validation Report",
            "validation.json",
            "Validation JSON|*.validation.json|JSON Files|*.json|All Files|*.*");

        if (filePath != null)
        {
            await ExportValidationToFile(filePath);
        }
    }

    /// <summary>
    /// Called after simulation completes. Stores the result and optionally auto-exports.
    /// </summary>
    /// <param name="result">The validation result from the simulation.</param>
    /// <param name="wavelengthNm">The wavelength used in the simulation.</param>
    public async Task OnSimulationValidationCompleted(
        SMatrixValidationResult result,
        int? wavelengthNm = null)
    {
        LastValidationResult = result;
        LastSimulationWavelengthNm = wavelengthNm;

        if (AutoExportValidation && _currentFilePath != null)
        {
            var exportPath = ValidationReportExporter
                .GetAutoExportPath(_currentFilePath);
            await ExportValidationToFile(exportPath);
        }
    }

    private async Task ExportValidationToFile(string filePath)
    {
        try
        {
            await _validationExporter.ExportAsync(
                LastValidationResult!,
                filePath,
                LastSimulationWavelengthNm);
            StatusText = $"Validation report exported to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Validation export failed: {ex.Message}";
        }
    }
}
