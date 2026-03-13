using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Export;

namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// ViewModel for GDS export functionality.
/// Allows users to export designs to GDS format with automatic Python/Nazca execution.
/// </summary>
public partial class GdsExportViewModel : ObservableObject
{
    private readonly GdsExportService _exportService;

    [ObservableProperty]
    private bool _pythonAvailable;

    [ObservableProperty]
    private string _pythonStatus = "Checking...";

    [ObservableProperty]
    private bool _nazcaAvailable;

    [ObservableProperty]
    private string _nazcaStatus = "Checking...";

    [ObservableProperty]
    private bool _generateGdsEnabled = true;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string _lastExportStatus = string.Empty;

    /// <summary>
    /// True if both Python and Nazca are available and ready.
    /// </summary>
    public bool IsEnvironmentReady => PythonAvailable && NazcaAvailable;

    public GdsExportViewModel(GdsExportService exportService)
    {
        _exportService = exportService;
    }

    /// <summary>
    /// Checks the Python/Nazca environment and updates status.
    /// </summary>
    [RelayCommand]
    public async Task CheckEnvironmentAsync()
    {
        IsChecking = true;
        PythonStatus = "Checking...";
        NazcaStatus = "Checking...";

        try
        {
            var envInfo = await _exportService.CheckPythonEnvironmentAsync();

            PythonAvailable = envInfo.PythonAvailable;
            NazcaAvailable = envInfo.NazcaAvailable;

            if (envInfo.PythonAvailable)
            {
                PythonStatus = $"✓ Found (v{envInfo.PythonVersion})";
            }
            else
            {
                PythonStatus = "✗ Not found";
            }

            if (envInfo.NazcaAvailable)
            {
                NazcaStatus = $"✓ Found (v{envInfo.NazcaVersion})";
            }
            else if (envInfo.PythonAvailable)
            {
                NazcaStatus = "✗ Not installed";
            }
            else
            {
                NazcaStatus = "N/A (Python not found)";
            }

            OnPropertyChanged(nameof(IsEnvironmentReady));
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Exports a Python script to GDS (if enabled and environment is ready).
    /// </summary>
    /// <param name="scriptPath">Path to the exported Python script.</param>
    /// <returns>Export result with status information.</returns>
    public async Task<GdsExportService.ExportResult> ExportScriptToGdsAsync(string scriptPath)
    {
        var result = await _exportService.ExportToGdsAsync(scriptPath, GenerateGdsEnabled);
        LastExportStatus = result.Status;
        return result;
    }
}
