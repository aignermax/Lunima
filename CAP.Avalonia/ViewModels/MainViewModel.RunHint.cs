using System.ComponentModel;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Workspaces;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Run-shortcut half of <see cref="MainViewModel"/>: the L key runs the same workspace
/// action as the Run button, and the first few times it also names that action in a
/// brief hint under the button — L means "checks" in Layout but "clock step" in Logic.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The hint under the Run button announcing what L just did.</summary>
    public RunShortcutHintViewModel RunHint { get; } = new();

    /// <summary>Keeps the Run label in step with the Logic tab (build vs step); call after <see cref="InitializeWorkspaces"/>.</summary>
    private void InitializeRunHint()
    {
        WorkspaceSwitch.CanStepLogicClock = IsLogicClockable;
        RightPanel.Logic.PropertyChanged += OnLogicPanelChangedForRun;
    }

    private void OnLogicPanelChangedForRun(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogicPanelViewModel.HasNetwork) or nameof(LogicPanelViewModel.HasRegisters))
            WorkspaceSwitch.CanStepLogicClock = IsLogicClockable;
    }

    /// <summary>True when Run in Logic steps the clock (a built network with registers), as <see cref="RunWorkspace"/> decides.</summary>
    private bool IsLogicClockable => RightPanel.Logic.HasNetwork && RightPanel.Logic.StepClockCommand.CanExecute(null);

    /// <summary>
    /// The L key: announces the workspace action (the first few times per action), then
    /// runs it exactly like the Run button.
    /// </summary>
    [RelayCommand]
    private async Task RunWorkspaceFromShortcut()
    {
        RunHint.TryShow(RunActionKey());
        await RunWorkspace();
    }

    /// <summary>String-table key naming what Run does right now in the active workspace.</summary>
    internal string RunActionKey() => WorkspaceSwitch.Current switch
    {
        Workspace.Optics when SimulationMode == Analysis.SimulationMode.Transient => "Workspace.Action.Transient",
        Workspace.Optics or Workspace.System when Canvas.ShowPowerFlow => "Workspace.Action.SimulationOff",
        Workspace.Optics => "Workspace.Action.SimulationOn",
        Workspace.Logic when IsLogicClockable => "Workspace.Action.LogicStep",
        Workspace.Logic => "Workspace.Action.BuildLogic",
        Workspace.System => "Workspace.Action.RunSystem",
        _ => "Workspace.Action.Checks",
    };
}
