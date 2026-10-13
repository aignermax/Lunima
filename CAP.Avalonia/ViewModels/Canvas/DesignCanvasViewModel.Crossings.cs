using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Canvas-side handling of the crossings routing placed: commands take their chains out
/// before deleting or replacing a wire through them (so an undo restores whole wires), and
/// the user can remove all of them at once with "Clean up crossings".
/// </summary>
public partial class DesignCanvasViewModel
{
    /// <summary>
    /// Undo stack canvas-level commands record into. Set by the composition root; without
    /// one, such commands still run but cannot be undone.
    /// </summary>
    public CommandManager? CommandManager { get; set; }

    /// <summary>
    /// Removes every crossing routing placed: the wires through them merge and route again
    /// (re-inserting crossings only where adaptive insertion is on and one is needed).
    /// Crossings the user placed stay. Undoable.
    /// </summary>
    [RelayCommand]
    private void CleanUpCrossings()
    {
        var command = new CleanUpCrossingsCommand(this);
        if (CommandManager != null)
            CommandManager.ExecuteCommand(command);
        else
            command.Execute();
    }

    /// <summary>
    /// Takes out the chains of routing-placed crossings the selected wires dock onto and
    /// updates the view-models, before a command deletes or replaces those wires.
    /// </summary>
    public void DissolveInsertedCrossingChains(Func<WaveguideConnection, bool> touches) =>
        ApplyCrossingRemoval(ConnectionManager.DissolveInsertedChainsTouching(touches));

    /// <summary>
    /// Takes out the chain of one piece of a routing-placed crossing and returns its wire
    /// merged pin to pin (route dropped), or null when the piece docks onto no such crossing.
    /// </summary>
    public WaveguideConnection? DissolveInsertedCrossingChainOf(WaveguideConnection piece)
    {
        var merged = ConnectionManager.DissolveInsertedChainOf(piece, out var removed);
        ApplyCrossingRemoval(removed);
        return merged;
    }

    /// <summary>Drops the view-models of removed crossings and mirrors the manager's connections.</summary>
    internal void ApplyCrossingRemoval(IReadOnlyCollection<Component> removed)
    {
        if (removed.Count == 0) return;
        foreach (var crossing in removed)
            CrossingCanvasSync.RemoveCrossing(this, crossing);
        SyncConnectionViewModels();
    }
}
