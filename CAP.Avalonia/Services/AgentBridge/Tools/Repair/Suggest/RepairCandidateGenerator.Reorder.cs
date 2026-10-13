using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>Swapping the routing order of the blocked wire and a pin neighbour.</summary>
public sealed partial class RepairCandidateGenerator
{
    /// <summary>
    /// For each pin-neighbour wire (a pin within 12 µm of the blocked wire's pins — the other
    /// input of an MMI pair, or the other output of a splitter): take it out, let the blocked
    /// wire route, then put it back so it routes around — the manual version of the router's
    /// pin-pair swap. Two undo steps. Crossing pieces are skipped (their chain would go too).
    /// </summary>
    private IEnumerable<RepairCandidate> NeighbourReorders() =>
        _session.Canvas.Connections
            .Where(vm => vm.Connection != _blocked && !vm.Connection.IsBlockedFallback && !vm.Connection.IsElectrical)
            .Where(vm => !CrossingComponentCatalog.IsCrossing(vm.Connection.StartPin.ParentComponent)
                         && !CrossingComponentCatalog.IsCrossing(vm.Connection.EndPin.ParentComponent))
            .Where(vm => IsPinNeighbour(vm.Connection.StartPin) || IsPinNeighbour(vm.Connection.EndPin))
            .Select(ReorderCandidate);

    private RepairCandidate ReorderCandidate(WaveguideConnectionViewModel neighbour)
    {
        var connection = neighbour.Connection;
        var description = $"Route the blocked wire before its pin neighbour {Name(connection)}: remove the neighbour, " +
                          "route, then reconnect it so it routes around";
        return new RepairCandidate("reorder_neighbour", description, 0,
            new
            {
                Tool = "delete_connection, reroute_connections, create_connection",
                ConnectionId = connection.Id.ToString(),
                Reconnect = $"{connection.StartPin.ParentComponent.Identifier}.{connection.StartPin.Name} → {connection.EndPin.ParentComponent.Identifier}.{connection.EndPin.Name}",
            },
            () =>
            {
                var vm = _session.Canvas.Connections.FirstOrDefault(c => c.Connection == connection);
                if (vm == null)
                    return (null, "The neighbour wire is gone.");
                var delete = new DeleteConnectionCommand(_session.Canvas, vm);
                var reconnect = new CreateConnectionCommand(_session.Canvas, connection.StartPin, connection.EndPin);
                return (new RepairEditPlan(delete,
                    () => _session.Canvas.Connections.All(c => c.Connection != connection),
                    new IUndoableCommand[] { reconnect }), null);
            });
    }
}
