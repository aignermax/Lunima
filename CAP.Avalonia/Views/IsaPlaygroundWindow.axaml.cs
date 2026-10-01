using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

namespace CAP.Avalonia.Views;

/// <summary>
/// Non-modal ISA playground tool window (issue #1194): write a 4-bit assembly
/// program (or pick a shipped sample), assemble it and step the golden-model
/// machine while the state readout and the current source line stay visible.
/// Opened from the Tools flyout; <see cref="MainWindow"/> deduplicates so a
/// second open activates the existing window. The ViewModel stays timer-free so
/// tests advance run ticks synchronously; this code-behind owns the
/// DispatcherTimer that turns <see cref="IsaPlaygroundViewModel.IsRunning"/>
/// into wall-clock auto-steps (issue #1204, same pattern as the Logic panel).
/// </summary>
public partial class IsaPlaygroundWindow : Window
{
    private IsaPlaygroundViewModel? _playground;
    private DispatcherTimer? _runTimer;
    private RunProgramTourViewModel? _tour;

    /// <summary>Initializes the window.</summary>
    public IsaPlaygroundWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => _runTimer?.Stop();
        Opened += (_, _) => _tour?.NotifyPlaygroundOpened();
        Closed += (_, _) => _tour?.NotifyPlaygroundClosed();
    }

    /// <summary>
    /// The "Run a program on your chip" tour engine (issue #1267) whose card is
    /// hosted by this window's overlay for the playground steps; also told when
    /// the window opens and closes. Assigned by <see cref="MainWindow"/> (or by
    /// tests); null means no tour card is shown here.
    /// </summary>
    public RunProgramTourViewModel? Tour
    {
        get => _tour;
        set
        {
            _tour = value;
            RunProgramTourOverlay.DataContext = value;
            RunProgramTourCard.DataContext = value;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_playground != null)
        {
            _playground.PropertyChanged -= OnPlaygroundPropertyChanged;
        }

        _playground = DataContext as IsaPlaygroundViewModel;
        if (_playground != null)
        {
            _playground.PropertyChanged += OnPlaygroundPropertyChanged;
        }

        SyncRunTimer();
    }

    private void OnPlaygroundPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsaPlaygroundViewModel.IsRunning))
        {
            SyncRunTimer();
        }
    }

    /// <summary>Runs the timer while the VM auto-steps; stops it when the run ends.</summary>
    private void SyncRunTimer()
    {
        if (_playground?.IsRunning == true)
        {
            if (_runTimer == null)
            {
                _runTimer = new DispatcherTimer { Interval = IsaPlaygroundViewModel.RunInterval };
                _runTimer.Tick += (_, _) => _playground?.AdvanceRunTick();
            }

            _runTimer.Start();
            return;
        }

        _runTimer?.Stop();
    }
}
