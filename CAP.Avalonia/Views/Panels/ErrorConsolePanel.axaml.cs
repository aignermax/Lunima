using Avalonia.Controls;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Collapsible error console panel displayed at the bottom of the window.
/// Handles auto-scroll to newest entry and clipboard wiring.
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class ErrorConsolePanel : UserControl
{
    /// <summary>Initializes the ErrorConsolePanel and wires up auto-scroll and clipboard.</summary>
    public ErrorConsolePanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Auto-scroll to newest entry when new entries are added
        vm.BottomPanel.ErrorConsole.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(vm.BottomPanel.ErrorConsole.EntryCount)
                && ErrorConsoleListBox != null)
            {
                var items = ErrorConsoleListBox.ItemsSource;
                if (items is System.Collections.IList list && list.Count > 0)
                {
                    ErrorConsoleListBox.ScrollIntoView(list[list.Count - 1]);
                }
            }
        };

        // Wire clipboard for copy-all
        vm.BottomPanel.ErrorConsole.CopyToClipboard = async (text) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        };
    }
}
