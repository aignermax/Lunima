namespace CAP.Avalonia.Services;

/// <summary>
/// Service for showing input dialogs to prompt user for text input.
/// </summary>
public interface IInputDialogService
{
    /// <summary>
    /// Shows a text input dialog with optional default value.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="prompt">Prompt message</param>
    /// <param name="defaultValue">Default input value</param>
    /// <returns>User input, or null if cancelled</returns>
    Task<string?> ShowInputDialogAsync(string title, string prompt, string? defaultValue = null);

    /// <summary>
    /// Shows a dialog with multiple input fields.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="fields">Field definitions (label, default value)</param>
    /// <returns>Dictionary of field values, or null if cancelled</returns>
    Task<Dictionary<string, string>?> ShowMultiInputDialogAsync(
        string title,
        params (string label, string defaultValue)[] fields);
}
