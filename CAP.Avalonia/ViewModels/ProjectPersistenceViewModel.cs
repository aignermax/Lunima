using CAP_Core;
using CAP.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// ViewModel for project persistence operations (save/load projects with ComponentGroups).
/// Separate from Panels.FileOperationsViewModel which handles general file I/O.
/// </summary>
public partial class ProjectPersistenceViewModel : ObservableObject
{
    private readonly ProjectPersistenceService _persistenceService;
    private readonly IFileDialogService? _fileDialogService;
    private readonly ErrorConsoleService? _errorConsole;

    [ObservableProperty]
    private string _currentFilePath = "";

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>Initializes a new instance of <see cref="ProjectPersistenceViewModel"/>.</summary>
    public ProjectPersistenceViewModel(
        ProjectPersistenceService persistenceService,
        IFileDialogService? fileDialogService = null,
        ErrorConsoleService? errorConsole = null)
    {
        _persistenceService = persistenceService;
        _fileDialogService = fileDialogService;
        _errorConsole = errorConsole;
    }

    /// <summary>
    /// Shows a save dialog and saves the current project.
    /// </summary>
    [RelayCommand]
    private async Task SaveProjectAsAsync()
    {
        if (_fileDialogService == null)
        {
            StatusMessage = "File dialog service not available.";
            return;
        }

        var filePath = await _fileDialogService.ShowSaveFileDialogAsync(
            "Save Project",
            "cappro",
            "Lunima Project|*.cappro");

        if (string.IsNullOrEmpty(filePath))
        {
            StatusMessage = "Save cancelled.";
            return;
        }

        await SaveToFileAsync(filePath);
    }

    /// <summary>
    /// Saves the current project to the current file path (or shows dialog if not set).
    /// </summary>
    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            await SaveProjectAsAsync();
            return;
        }

        await SaveToFileAsync(CurrentFilePath);
    }

    /// <summary>
    /// Shows an open dialog and loads a project.
    /// </summary>
    [RelayCommand]
    private async Task LoadProjectAsync()
    {
        if (_fileDialogService == null)
        {
            StatusMessage = "File dialog service not available.";
            return;
        }

        var filePath = await _fileDialogService.ShowOpenFileDialogAsync(
            "Open Project",
            "Lunima Project|*.cappro|All Files|*.*");

        if (string.IsNullOrEmpty(filePath))
        {
            StatusMessage = "Load cancelled.";
            return;
        }

        await LoadFromFileAsync(filePath);
    }

    /// <summary>
    /// Saves the project to the specified file path.
    /// </summary>
    private async Task SaveToFileAsync(string filePath)
    {
        try
        {
            StatusMessage = "Saving project...";
            var success = await _persistenceService.SaveProjectAsync(filePath);

            if (success)
            {
                CurrentFilePath = filePath;
                StatusMessage = $"Project saved to {Path.GetFileName(filePath)}";
            }
            else
            {
                StatusMessage = "Failed to save project.";
            }
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Failed to save project: {ex.Message}", ex);
            StatusMessage = $"Error saving project: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads a project from the specified file path.
    /// </summary>
    private async Task LoadFromFileAsync(string filePath)
    {
        try
        {
            StatusMessage = "Loading project...";
            await _persistenceService.LoadProjectAsync(filePath);

            CurrentFilePath = filePath;
            StatusMessage = $"Project loaded from {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Failed to load project: {ex.Message}", ex);
            StatusMessage = $"Error loading project: {ex.Message}";
        }
    }
}
