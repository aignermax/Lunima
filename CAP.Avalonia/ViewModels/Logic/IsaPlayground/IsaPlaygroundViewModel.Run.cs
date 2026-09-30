using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// Auto-step half of <see cref="IsaPlaygroundViewModel"/> (issue #1204, ISA playground
/// slice 2): the Run button steps the golden model on a fixed didactic cadence
/// (<see cref="RunInterval"/>) until HALT, until the user presses Stop (the same
/// button toggles), or until <see cref="MaxRunSteps"/> trips on an endless loop.
/// While running the editor is read-only and Assemble/Step are disabled; the
/// current-line highlight and the PC/ACC/RAM readout refresh on every tick.
/// The ViewModel stays timer-free so tests advance ticks synchronously: the view
/// wires <see cref="AdvanceRunTick"/> to a DispatcherTimer at <see cref="RunInterval"/>
/// (same pattern as the Logic panel playback, issue #1069).
/// </summary>
public partial class IsaPlaygroundViewModel
{
    /// <summary>Wall-clock cadence of the auto-step run — didactic, not to scale.</summary>
    public static TimeSpan RunInterval { get; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Step cap that stops an endless loop instead of running forever.</summary>
    public const int MaxRunSteps = 1000;

    private int _runSteps;

    /// <summary>True while the machine auto-steps (button shows Stop).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunStopText))]
    [NotifyPropertyChangedFor(nameof(IsEditorReadOnly))]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    [NotifyCanExecuteChangedFor(nameof(StepCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssembleCommand))]
    private bool _isRunning;

    /// <summary>Label of the Run/Stop button — Run at rest, Stop while the machine runs.</summary>
    public string RunStopText =>
        Translate(IsRunning ? "IsaPlayground.StopButton" : "IsaPlayground.RunButton");

    /// <summary>The editor is read-only while the machine runs on the assembled program.</summary>
    public bool IsEditorReadOnly => IsRunning;

    private bool CanStep => IsAssembled && !IsRunning;

    private bool CanAssemble => !IsRunning;

    /// <summary>
    /// Run starts the auto-step from the current machine state; Stop freezes it
    /// mid-program (PC, ACC and RAM stay where they are and Step resumes manually).
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsAssembled))]
    private void ToggleRun()
    {
        if (IsRunning)
        {
            StopRun();
            return;
        }

        if (_emulator is null)
        {
            return;
        }

        _runSteps = 0;
        IsRunning = true;
    }

    /// <summary>
    /// One auto-step: executes the instruction at PC and refreshes the readout.
    /// Stops on HALT ("halted after N steps"), on the step cap, or on a runtime
    /// error. Called by the view's DispatcherTimer; tests call it directly.
    /// </summary>
    public void AdvanceRunTick()
    {
        if (!IsRunning || _emulator is null)
        {
            return;
        }

        if (_emulator.IsHalted)
        {
            FinishRun("IsaPlayground.StatusHaltedAfterSteps");
            return;
        }

        var photonicAdd = NextStepIsPhotonicAdd();
        try
        {
            _emulator.Step();
        }
        catch (InvalidOperationException ex)
        {
            ErrorText = ex.Message;
            StopRun();
            UpdateState();
            return;
        }

        _runSteps++;
        UpdateState();
        if (photonicAdd)
        {
            ReportPhotonicAdd();
            PublishDrivenInputs();
        }

        if (_emulator.IsHalted)
        {
            FinishRun("IsaPlayground.StatusHaltedAfterSteps");
        }
        else if (_runSteps >= MaxRunSteps)
        {
            FinishRun("IsaPlayground.StatusStepCapReached");
        }
    }

    /// <summary>Stops the auto-step; the machine state stays where it is.</summary>
    private void StopRun() => IsRunning = false;

    /// <summary>Ends the run with a final status line that names the step count.</summary>
    private void FinishRun(string statusKey)
    {
        StopRun();
        MachineStatusText = string.Format(CultureInfo.InvariantCulture, Translate(statusKey), _runSteps);
    }
}
