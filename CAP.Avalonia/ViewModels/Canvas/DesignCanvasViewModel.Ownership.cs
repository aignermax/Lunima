using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Wire-ownership half of <see cref="DesignCanvasViewModel"/>: every wire sits with the group
/// that owns it (the lowest common ancestor of its parts, see <see cref="ConnectionOwnership"/>).
/// </summary>
public partial class DesignCanvasViewModel
{
    /// <summary>
    /// Brings a loaded design in line with the ownership rule (see <see cref="WireHomeMigration"/>):
    /// root wires between parts of one group move into that group, frozen paths stored too high
    /// move down, and wires stored inside a die come onto the assembly canvas (a die is edited
    /// in place). Idempotent; a design whose wires already sit with their owner is untouched.
    /// Not undoable — it runs as part of loading.
    /// </summary>
    public WireHomePlan ApplyWireOwnership()
    {
        var plan = WireHomeMigration.Plan(Components.Select(c => c.Component), ConnectionManager.Connections.ToList());
        if (plan.IsEmpty)
            return plan;

        foreach (var move in plan.RootWiresToGroups)
        {
            var frozen = GroupWireHoming.Freeze(move.Connection, move.Owner);
            ConnectionManager.RemoveConnectionDeferred(move.Connection);
            if (Connections.FirstOrDefault(c => ReferenceEquals(c.Connection, move.Connection)) is { } vm)
                Connections.Remove(vm);
            GroupWireHoming.Home(move.Owner, frozen);
        }
        WireHomeMigration.ApplyModelMoves(plan);
        foreach (var wire in plan.DieWiresToSurface)
            SurfaceDieWire(wire.Path);

        foreach (var vm in Components.Where(c => c.Component is ComponentGroup { IsDie: false }).ToList())
            ResyncGroupPinViewModels(vm, (ComponentGroup)vm.Component);
        RefreshDieInterfaces();
        InvalidateSimulation();
        return plan;
    }

    /// <summary>Turns a frozen die path into a live wire on the assembly canvas, geometry and settings kept.</summary>
    private void SurfaceDieWire(FrozenWaveguidePath path)
    {
        var start = path.StartPin!;
        var end = path.EndPin!;
        var connectionVm = path.Path is { Segments.Count: > 0 } routed
            ? ConnectPinsWithCachedRoute(start, end, routed.DeepCopy())
            : ConnectPins(start, end);
        if (connectionVm == null)
            return;
        path.ApplySettingsTo(connectionVm.Connection);
        connectionVm.Connection.UpdateLossFromPath();
    }
}
