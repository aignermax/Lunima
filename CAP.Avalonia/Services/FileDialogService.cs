using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CAP.Avalonia.Services;

/// <summary>
/// File dialog service implementation for Avalonia.
/// </summary>
public class FileDialogService : IFileDialogService
{
    private readonly Window _window;

    public FileDialogService(Window window)
    {
        _window = window;
    }

    public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultExtension, string filters)
    {
        var storageProvider = _window.StorageProvider;

        var fileTypes = ParseFilters(filters, defaultExtension);

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = fileTypes
        });

        return result?.Path.LocalPath;
    }

    public async Task<string?> ShowOpenFileDialogAsync(string title, string filters)
    {
        var storageProvider = _window.StorageProvider;

        var fileTypes = ParseFilters(filters, null);

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes
        });

        return results.Count > 0 ? results[0].Path.LocalPath : null;
    }

    private List<FilePickerFileType> ParseFilters(string filters, string? defaultExtension)
    {
        var result = new List<FilePickerFileType>();

        // Parse filter string like "Python Files|*.py|All Files|*.*"
        var parts = filters.Split('|');
        for (int i = 0; i < parts.Length - 1; i += 2)
        {
            var name = parts[i];
            var patterns = parts[i + 1].Split(';').ToList();

            result.Add(new FilePickerFileType(name)
            {
                Patterns = patterns
            });
        }

        if (result.Count == 0)
        {
            result.Add(new FilePickerFileType("All Files")
            {
                Patterns = new[] { "*.*" }
            });
        }

        return result;
    }
}
