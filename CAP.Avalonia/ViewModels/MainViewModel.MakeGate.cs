using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Make-gate wiring of <see cref="MainViewModel"/>: connects the right panel's
/// Make-gate steps and plain truth table to the undo history, the library and the
/// Logic tab, lets the Logic tab's empty state jump back to "Make gate…", and starts
/// the "Your first gate" tour.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Hooks the Make-gate steps, the truth table and the Logic panel together.</summary>
    private void WireMakeGate()
    {
        var makeGate = RightPanel.MakeGate;
        makeGate.ExecuteCommand = CommandManager.ExecuteCommand;
        makeGate.SaveToLibraryRequested = SaveGateToLibrary;
        makeGate.OpenLogicRequested = OpenLogicTab;
        makeGate.GateSaved += (_, _) => RightPanel.Logic.RefreshGateCandidates();
        RightPanel.TruthTable.OpenLogicRequested = OpenLogicTab;
        RightPanel.Logic.MakeGateRequested = SelectGroupAndOpenMakeGate;
    }

    /// <summary>
    /// Context menu "Make gate…": groups a multi-selection first (Ctrl+G), then opens
    /// the Make-gate steps for the selected group.
    /// </summary>
    [RelayCommand]
    private void MakeGate()
    {
        var selection = Canvas.Selection.SelectedComponents;
        var isSingleGroup = selection.Count <= 1 && Canvas.SelectedComponent?.Component is ComponentGroup;
        if (!isSingleGroup && selection.Count >= 2)
            CanvasInteraction.CreateGroupCommand.Execute(null);

        if (Canvas.SelectedComponent?.Component is ComponentGroup group)
            SelectGroupAndOpenMakeGate(group);
        else
            UpdateStatusText(Services.Localization.LocalizationService.Instance.Translate("MakeGate.SelectPartsFirst"));
    }

    /// <summary>Selects <paramref name="group"/> on the canvas and opens its Make-gate steps.</summary>
    private void SelectGroupAndOpenMakeGate(ComponentGroup group)
    {
        var groupVm = Canvas.Components.FirstOrDefault(c => ReferenceEquals(c.Component, group));
        if (groupVm == null)
            return;
        EnsureGateToolsVisible();
        Canvas.Selection.SelectSingle(groupVm);
        Canvas.SelectedComponent = groupVm;
        RightPanel.TruthTable.ConfigureForSelection(groupVm, Canvas);
        RightPanel.MakeGate.ConfigureForSelection(groupVm, Canvas);
        RightPanel.MakeGate.OpenCommand.Execute(null);
    }

    /// <summary>Switches to the Logic workspace, refreshes the Logic tab's gate list and opens the dock on it.</summary>
    private void OpenLogicTab()
    {
        WorkspaceSwitch.Current = ViewModels.Workspaces.Workspace.Logic;
        RightPanel.Logic.RefreshGateCandidates();
        BottomPanel.Analysis.OpenLogic();
    }

    /// <summary>Saves the gate as a prefab under "Saved Groups" (undoable).</summary>
    private bool SaveGateToLibrary(ComponentGroup group, string name)
    {
        var library = LeftPanel.ComponentLibrary;
        if (library == null)
            return false;
        CommandManager.ExecuteCommand(new SaveGroupAsPrefabCommand(
            library, new GroupPreviewGenerator(), group, name, group.Description));
        return true;
    }

    /// <summary>
    /// Starts the "Your first gate" tour on the shipped gate-kit example. The example
    /// opens as an untitled copy; when the user cancels the unsaved-changes prompt (or
    /// the example is not installed) the tour does not start.
    /// </summary>
    private async Task StartFirstGateTourAsync()
    {
        var kitPath = Home.Examples
            .FirstOrDefault(example => System.IO.Path.GetFileName(example.FilePath)
                == Onboarding.FirstStepsTutorial.FirstGateTourViewModel.GateKitExampleFileName)
            ?.FilePath;
        if (kitPath == null || !await FileOperations.OpenDesignAsCopyAsync(kitPath))
            return;

        FirstGateTour.Start();
    }
}
