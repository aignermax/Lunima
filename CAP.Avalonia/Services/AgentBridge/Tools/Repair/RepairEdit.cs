using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair;

/// <summary>Blocked wires and crossings of the design at one moment.</summary>
/// <param name="BlockedCount">Number of blocked (red) connections.</param>
/// <param name="CrossingCount">Number of crossing components on the canvas.</param>
/// <param name="BlockedIds">Ids of the blocked connections.</param>
public sealed record RoutingMetrics(int BlockedCount, int CrossingCount, IReadOnlySet<Guid> BlockedIds)
{
    /// <summary>Measures the open design.</summary>
    public static RoutingMetrics Measure(AgentDesignSession session)
    {
        var blocked = session.Canvas.Connections
            .Where(c => c.Connection.IsBlockedFallback)
            .Select(c => c.Connection.Id)
            .ToHashSet();
        var crossings = session.Canvas.Components.Count(c => CrossingComponentCatalog.IsCrossing(c.Component));
        return new RoutingMetrics(blocked.Count, crossings, blocked);
    }
}

/// <summary>
/// Runs a repair edit the way every agent-bridge edit runs: one step on the shared undo
/// history (Ctrl+Z reverts it, and the crossing undo journal attributes the re-route's crossing
/// changes to it), then a full routing pass awaited without blocking the UI thread.
/// </summary>
public static class RepairEdit
{
    /// <summary>Executes <paramref name="command"/> and waits for routing. Returns whether routing finished in time.</summary>
    public static async Task<bool> ExecuteAndRouteAsync(AgentDesignSession session, IUndoableCommand command)
    {
        session.Commands.ExecuteCommand(command);
        var routed = await RoutingAwaiter.RerouteAndWaitAsync(session.Canvas);
        session.Canvas.InvalidateSimulation();
        return routed;
    }

    /// <summary>Undoes the most recent step and waits for routing.</summary>
    public static async Task<bool> UndoAndRouteAsync(AgentDesignSession session)
    {
        if (!session.Commands.Undo())
            return true;
        var routed = await RoutingAwaiter.RerouteAndWaitAsync(session.Canvas);
        session.Canvas.InvalidateSimulation();
        return routed;
    }
}
