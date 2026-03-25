using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Export;
using System.Collections.ObjectModel;

namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// ViewModel for GDS export functionality.
/// Allows users to export designs to GDS format with automatic Python/Nazca execution.
/// </summary>
public partial class GdsExportViewModel : ObservableObject
{
    private readonly GdsExportService _exportService;
    private readonly PythonDiscoveryService _discoveryService;

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

    [ObservableProperty]
    private string _customPythonPath = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<PythonDiscoveryService.PythonInstallation> _availablePythons = new();

    [ObservableProperty]
    private string _pythonPathSource = string.Empty;

    /// <summary>
    /// True if both Python and Nazca are available and ready.
    /// </summary>
    public bool IsEnvironmentReady => PythonAvailable && NazcaAvailable;

    /// <summary>
    /// Callback to save Python path to preferences when changed.
    /// </summary>
    public Action<string?>? OnPythonPathChanged { get; set; }

    public GdsExportViewModel(GdsExportService exportService)
    {
        _exportService = exportService;
        _discoveryService = new PythonDiscoveryService();
    }

    /// <summary>
    /// Initializes the ViewModel with saved preferences.
    /// </summary>
    /// <param name="savedPythonPath">Previously saved Python path from preferences.</param>
    public void Initialize(string? savedPythonPath)
    {
        if (!string.IsNullOrEmpty(savedPythonPath))
        {
            CustomPythonPath = savedPythonPath;
            _exportService.SetCustomPythonPath(savedPythonPath);
            PythonPathSource = "Custom";
        }
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

    /// <summary>
    /// Searches for Python installations with Nazca and updates the available list.
    /// </summary>
    [RelayCommand]
    public async Task SearchForPythonAsync()
    {
        IsSearching = true;
        AvailablePythons.Clear();

        try
        {
            var found = await _discoveryService.DiscoverPythonWithNazcaAsync();

            foreach (var installation in found)
            {
                AvailablePythons.Add(installation);
            }

            if (found.Count > 0)
            {
                // Auto-select first one if no path is set
                if (string.IsNullOrEmpty(CustomPythonPath))
                {
                    await SelectPython(found[0]);
                }
            }
            else
            {
                PythonStatus = "✗ No Python with Nazca found";
                NazcaStatus = "Install Nazca from nazca-design.org";
            }
        }
        catch (Exception ex)
        {
            PythonStatus = $"✗ Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Manually sets the Python path and updates status.
    /// </summary>
    /// <param name="pythonPath">Path to Python executable.</param>
    public async Task SetPythonPathAsync(string pythonPath)
    {
        CustomPythonPath = pythonPath;
        PythonPathSource = "Custom";
        _exportService.SetCustomPythonPath(pythonPath);
        OnPythonPathChanged?.Invoke(pythonPath);
        await CheckEnvironmentAsync();
    }

    /// <summary>
    /// Selects a discovered Python installation.
    /// </summary>
    /// <param name="installation">The Python installation to use.</param>
    [RelayCommand]
    public async Task SelectPython(PythonDiscoveryService.PythonInstallation installation)
    {
        CustomPythonPath = installation.Path;
        PythonPathSource = installation.Source;
        _exportService.SetCustomPythonPath(installation.Path);
        OnPythonPathChanged?.Invoke(installation.Path);
        await CheckEnvironmentAsync();
    }

}
