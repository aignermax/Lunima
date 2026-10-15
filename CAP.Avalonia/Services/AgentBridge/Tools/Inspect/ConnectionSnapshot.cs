namespace CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

/// <summary>
/// Reads the current connection state of the open design — the canvas connection list
/// annotated with crossing membership — for the inspection and edit tools.
/// </summary>
public static class ConnectionSnapshot
{
    /// <summary>Upper bound on blocked connections listed inline in an edit tool's result.</summary>
    public const int MaxBlockedListed = 50;

    /// <summary>Describes every connection on the canvas, each with the group that owns it.</summary>
    public static IReadOnlyList<ConnectionInfo> Describe(AgentDesignSession session)
    {
        var crossing = session.Canvas.ConnectionManager.CrossingInsertion;
        return session.Canvas.Connections
            .Select(vm => DesignDescriber.Describe(
                vm.Connection, crossing?.IsCrossingConnection(vm.Connection) == true))
            .ToList();
    }

    /// <summary>
    /// Describes the wires stored inside groups (frozen geometry with both pins, at any depth):
    /// a wire between two parts of a group belongs to that group, wherever it was drawn.
    /// </summary>
    public static IReadOnlyList<ConnectionInfo> DescribeGroupWires(AgentDesignSession session) =>
        CAP_Core.Analysis.Hierarchy.DesignHierarchy.From(session.Canvas.Components.Select(c => c.Component))
            .FrozenPaths
            .Where(p => p.Frozen.StartPin != null && p.Frozen.EndPin != null)
            .Select(p => DesignDescriber.Describe(p.Frozen, p.Owner))
            .ToList();

    /// <summary>
    /// Compact post-edit routing report: total and blocked counts plus the first
    /// <see cref="MaxBlockedListed"/> blocked connections.
    /// </summary>
    public static RoutingReport Report(AgentDesignSession session)
    {
        var all = Describe(session);
        var blocked = all.Where(c => c.Blocked).ToList();
        return new RoutingReport(all.Count, blocked.Count, blocked.Take(MaxBlockedListed).ToList());
    }
}

/// <summary>Connection totals and the blocked connections after a routing pass.</summary>
public sealed record RoutingReport(int ConnectionCount, int BlockedCount, IReadOnlyList<ConnectionInfo> Blocked);
