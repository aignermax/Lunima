using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// One change a routing pass made to the router-placed crossings — a chain taken out, a
/// chain or crossing placed, a chain put back — with its exact undo. The host attributes
/// such changes to the edit whose re-route made them, so undoing the edit restores the
/// crossings as they were before it.
/// </summary>
/// <param name="Added">Crossings the change placed (gone again after <see cref="Undo"/>).</param>
/// <param name="Removed">Crossings the change took out (back after <see cref="Undo"/>).</param>
/// <param name="AddedConnections">
/// Connections the change put into the design. The undo is only exact while all of them are
/// still there; a later change that replaced one must be undone first.
/// </param>
/// <param name="UndoAction">Reverts the change exactly.</param>
public sealed record CrossingChange(
    IReadOnlyList<Component> Added,
    IReadOnlyList<Component> Removed,
    IReadOnlyCollection<WaveguideConnection> AddedConnections,
    Action UndoAction)
{
    /// <summary>True while the design still holds what the change put in.</summary>
    public bool CanUndo(WaveguideConnectionManager manager)
    {
        lock (manager.SyncRoot)
            return AddedConnections.All(manager.Connections.Contains);
    }

    /// <summary>Reverts the change when <see cref="CanUndo"/>; returns whether it did.</summary>
    public bool Undo(WaveguideConnectionManager manager)
    {
        if (!CanUndo(manager)) return false;
        UndoAction();
        return true;
    }
}
