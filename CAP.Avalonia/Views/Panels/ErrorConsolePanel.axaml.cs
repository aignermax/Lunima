using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Diagnostics;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Code-behind for the ErrorConsole panel.
/// Handles auto-scrolling to the newest entry when entries are added.
/// </summary>
public partial class ErrorConsolePanel : UserControl
{
    /// <summary>Initializes a new instance of <see cref="ErrorConsolePanel"/>.</summary>
    public ErrorConsolePanel()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ErrorConsoleViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ErrorConsoleViewModel.EntryCount)) return;
        if (ErrorConsoleListBox?.ItemsSource is not System.Collections.IList list || list.Count == 0) return;

        ErrorConsoleListBox.ScrollIntoView(list[list.Count - 1]);
    }
}
