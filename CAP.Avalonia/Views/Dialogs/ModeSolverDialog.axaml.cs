using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Dialog window for "Tools → Compute Modes for Waveguide…".
/// DataContext must be set to <see cref="CAP.Avalonia.ViewModels.Solvers.ModeSolverViewModel"/>.
/// </summary>
public partial class ModeSolverDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public ModeSolverDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
