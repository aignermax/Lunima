using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels.Solvers;

namespace CAP.Avalonia.Views.Dialogs;

/// <summary>
/// Dialog window for "Tools → Compute Modes for Waveguide…".
/// DataContext must be set to <see cref="CAP.Avalonia.ViewModels.Solvers.ModeSolverViewModel"/>.
/// </summary>
public partial class ModeSolverDialog : Window
{
    private bool _closeApproved;
    private Task? _retirement;

    /// <summary>Composition owns the dialog scope and supplies its complete retirement.</summary>
    internal Func<Task>? RetireFeatureAsync { get; init; }

    /// <summary>Initializes the dialog.</summary>
    public ModeSolverDialog()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeApproved) return;
        args.Cancel = true;
        if (_retirement != null) return;
        IsEnabled = false;
        try
        {
            _retirement = RetireFeatureAsync?.Invoke()
                ?? (DataContext as ModeSolverViewModel)?.StopAsync() ?? Task.CompletedTask;
            await _retirement;
        }
        catch (Exception error)
        {
            // The scope owner still releases its resources in finally; a cleanup failure
            // must not escape the async window event and terminate the application.
            System.Diagnostics.Trace.TraceError($"Mode solver retirement failed: {error}");
        }
        finally
        {
            _closeApproved = true;
            Close();
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
