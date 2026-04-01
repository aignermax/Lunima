using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Status bar and error console panel (docked to bottom of MainWindow).
/// DataContext is inherited from MainWindow (MainViewModel).
/// </summary>
public partial class StatusBarPanel : UserControl
{
    /// <summary>Initializes the StatusBarPanel and wires up clipboard and auto-scroll.</summary>
    public StatusBarPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            vm.BottomPanel.ErrorConsole.CopyToClipboard = async (text) =>
                await clipboard.SetTextAsync(text);
        }

        vm.BottomPanel.ErrorConsole.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(vm.BottomPanel.ErrorConsole.EntryCount))
                ScrollToLatestEntry();
        };
    }

    private void ScrollToLatestEntry()
    {
        var items = ErrorConsoleListBox?.ItemsSource;
        if (items is System.Collections.IList list && list.Count > 0)
            ErrorConsoleListBox!.ScrollIntoView(list[list.Count - 1]);
    }
}
