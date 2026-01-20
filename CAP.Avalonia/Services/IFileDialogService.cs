namespace CAP.Avalonia.Services;

/// <summary>
/// Service for showing file dialogs.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Shows a save file dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="defaultExtension">Default file extension</param>
    /// <param name="filters">File type filters (e.g., "Python Files|*.py")</param>
    /// <returns>Selected file path, or null if cancelled</returns>
    Task<string?> ShowSaveFileDialogAsync(string title, string defaultExtension, string filters);

    /// <summary>
    /// Shows an open file dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="filters">File type filters</param>
    /// <returns>Selected file path, or null if cancelled</returns>
    Task<string?> ShowOpenFileDialogAsync(string title, string filters);
}
