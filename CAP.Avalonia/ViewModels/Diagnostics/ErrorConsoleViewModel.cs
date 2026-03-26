using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CAP_Contracts.Logger;
using CAP_Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// Display-ready wrapper for a <see cref="Log"/> entry, mapping level to color and formatted text.
/// </summary>
public class LogDisplayEntry
{
    /// <summary>Formatted one-line display text.</summary>
    public string Text { get; }

    /// <summary>Avalonia color string based on log level.</summary>
    public string Color { get; }

    /// <summary>Original log level (used for count aggregation).</summary>
    public LogLevel Level { get; }

    /// <summary>Initializes a display entry from a raw <see cref="Log"/>.</summary>
    public LogDisplayEntry(Log log)
    {
        Level = log.Level;
        Text = $"[{log.TimeStamp:HH:mm:ss}] [{log.Level.ToString().ToUpper()}] {log.Message}";
        Color = log.Level switch
        {
            LogLevel.Error or LogLevel.Fatal => "#FF6B6B",
            LogLevel.Warn => "#FFD93D",
            _ => "#A0A0A0"
        };
    }
}

/// <summary>
/// ViewModel for the collapsible error console bottom panel.
/// Wraps <see cref="ErrorConsoleService"/> to expose entries as UI-ready display items
/// and provides toggle, clear, and copy commands.
/// </summary>
public partial class ErrorConsoleViewModel : ObservableObject
{
    private readonly ErrorConsoleService _service;
    private readonly ObservableCollection<LogDisplayEntry> _displayEntries = new();

    /// <summary>
    /// UI-ready display entries derived from the underlying log service.
    /// </summary>
    public ReadOnlyObservableCollection<LogDisplayEntry> DisplayEntries { get; }

    /// <summary>
    /// Clipboard copy callback. Injected by the View code-behind because
    /// clipboard access in Avalonia requires a TopLevel reference.
    /// </summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>Whether the console content area is visible (expanded).</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Number of Error/Fatal entries.</summary>
    [ObservableProperty]
    private int _errorCount;

    /// <summary>Number of Warning entries.</summary>
    [ObservableProperty]
    private int _warningCount;

    /// <summary>Whether there are any error entries.</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>Whether there are any warning entries.</summary>
    public bool HasWarnings => WarningCount > 0;

    /// <summary>Total number of displayed entries.</summary>
    public int EntryCount => _displayEntries.Count;

    /// <summary>
    /// Initializes a new <see cref="ErrorConsoleViewModel"/> bound to the given service.
    /// </summary>
    /// <param name="service">The singleton error console service.</param>
    public ErrorConsoleViewModel(ErrorConsoleService service)
    {
        _service = service;
        DisplayEntries = new ReadOnlyObservableCollection<LogDisplayEntry>(_displayEntries);
        _service.EntryAdded += OnEntryAdded;
    }

    private void OnEntryAdded(object? sender, Log entry)
    {
        _displayEntries.Add(new LogDisplayEntry(entry));
        RefreshCounts();

        // Auto-expand for errors to draw user attention
        if (entry.Level >= LogLevel.Error)
            IsVisible = true;
    }

    private void RefreshCounts()
    {
        ErrorCount = _displayEntries.Count(e => e.Level >= LogLevel.Error);
        WarningCount = _displayEntries.Count(e => e.Level == LogLevel.Warn);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(EntryCount));
    }

    /// <summary>
    /// Convenience method to log a message directly via the underlying service.
    /// </summary>
    /// <param name="message">Message to log.</param>
    /// <param name="level">Severity level.</param>
    /// <param name="ex">Optional exception for additional context.</param>
    public void Log(string message, LogLevel level = LogLevel.Error, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message}\n{ex}" : message;
        _service.Log(level, fullMessage);
    }

    /// <summary>Toggles the console content area open or closed.</summary>
    [RelayCommand]
    private void Toggle() => IsVisible = !IsVisible;

    /// <summary>Clears all log entries from the console.</summary>
    [RelayCommand]
    private void Clear()
    {
        _service.Clear();
        _displayEntries.Clear();
        ErrorCount = 0;
        WarningCount = 0;
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(EntryCount));
    }

    /// <summary>Copies all current log entries as plain text to the system clipboard.</summary>
    [RelayCommand]
    private async Task CopyAll()
    {
        if (CopyToClipboard == null || _displayEntries.Count == 0)
            return;

        var sb = new StringBuilder();
        foreach (var entry in _displayEntries)
            sb.AppendLine(entry.Text);

        await CopyToClipboard(sb.ToString());
    }
}
