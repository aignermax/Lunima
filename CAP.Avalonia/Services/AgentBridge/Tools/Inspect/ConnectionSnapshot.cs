namespace CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

/// <summary>
/// Reads the current connection state of the open design — the canvas connection list
/// annotated with crossing membership — for the inspection and edit tools.
/// </summary>
public static class ConnectionSnapshot
{
    /// <summary>Upper bound on blocked connections listed inline in an edit tool's result.</summary>
    public const int MaxBlockedListed = 50;

    /// <summary>Describes every connection on the canvas.</summary>
    public static IReadOnlyList<ConnectionInfo> Describe(AgentDesignSession session)
    {
        var crossing = session.Canvas.ConnectionManager.CrossingInsertion;
        return session.Canvas.Connections
            .Select(vm => DesignDescriber.Describe(
                vm.Connection, crossing?.IsCrossingConnection(vm.Connection) == true))
            .ToList();
    }

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
