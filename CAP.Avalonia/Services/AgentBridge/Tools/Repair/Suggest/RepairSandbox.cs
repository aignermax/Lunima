using System.Diagnostics;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>
/// Tries repair candidates on the live design and takes each back: execute through the shared
/// undo history → full re-route → measure → undo → restore every wire's original route →
/// re-route (which keeps them). Undo restores components and the crossings the re-route changed
/// (crossing undo journal), the route snapshot the wires, so the design ends where it started;
/// that is checked after every trial, and trials stop if it ever does not hold. Running on the live
/// design (not a copy) is deliberate: only the real joint routing pass says whether a wire
/// routes. The user's redo stack is cleared by the trials.
/// </summary>
public sealed class RepairSandbox
{
    private readonly AgentDesignSession _session;
    private readonly WaveguideConnection _target;

    /// <summary>Creates the sandbox for repairing <paramref name="target"/>.</summary>
    public RepairSandbox(AgentDesignSession session, WaveguideConnection target)
    {
        _session = session;
        _target = target;
    }

    /// <summary>Set when a trial's undo did not restore the starting state; no further trials run.</summary>
    public string? Drift { get; private set; }

    /// <summary>Tries one candidate against <paramref name="baseline"/> and restores the design.</summary>
    public async Task<TrialOutcome> TryAsync(RepairCandidate candidate, RoutingMetrics baseline)
    {
        var clock = Stopwatch.StartNew();
        var (plan, error) = candidate.Build();
        if (plan == null)
            return NotApplicable(candidate, error, clock);

        var routes = RouteSnapshot.Capture(_session);
        var undoFrom = _session.Commands.UndoCount;
        if (!await plan.ExecuteAndRouteAsync(_session))
        {
            await RestoreAsync(routes, baseline, undoFrom);
            return NotApplicable(candidate, "the edit did not take effect here", clock);
        }

        var after = RoutingMetrics.Measure(_session);
        var fixedTarget = !TargetStillBlocked();

        await RestoreAsync(routes, baseline, undoFrom);

        return new TrialOutcome(candidate, true, null, after.BlockedCount,
            after.CrossingCount - baseline.CrossingCount, fixedTarget, clock.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Undo, let the routing pass the undo started finish, put every wire's original route back
    /// (<see cref="RouteSnapshot"/>) and run a pass that keeps them, then verify the start state.
    /// </summary>
    private async Task RestoreAsync(RouteSnapshot routes, RoutingMetrics baseline, int undoFrom)
    {
        while (_session.Commands.UndoCount > undoFrom && _session.Commands.Undo())
            await RoutingAwaiter.WaitForIdleAsync(_session.Canvas);
        await RoutingAwaiter.WaitForIdleAsync(_session.Canvas);
        routes.Restore(_session);
        await RoutingAwaiter.RerouteAndWaitAsync(_session.Canvas);
        _session.Canvas.InvalidateSimulation();
        CheckRestored(baseline);
    }

    /// <summary>A blocked wire still ends at one of the target's pins (the target may have been split into pieces).</summary>
    private bool TargetStillBlocked() =>
        _session.Canvas.Connections.Any(vm => vm.Connection.IsBlockedFallback
            && (Touches(vm.Connection, _target.StartPin) || Touches(vm.Connection, _target.EndPin)));

    private static bool Touches(WaveguideConnection connection, CAP_Core.Components.Core.PhysicalPin pin) =>
        connection.StartPin == pin || connection.EndPin == pin;

    private void CheckRestored(RoutingMetrics baseline)
    {
        var now = RoutingMetrics.Measure(_session);
        if (now.BlockedCount != baseline.BlockedCount || now.CrossingCount != baseline.CrossingCount)
            Drift = $"after undo the design has {now.BlockedCount} blocked wires and {now.CrossingCount} crossings " +
                    $"(started with {baseline.BlockedCount} and {baseline.CrossingCount})";
    }

    private static TrialOutcome NotApplicable(RepairCandidate candidate, string? reason, Stopwatch clock) =>
        new(candidate, false, reason, -1, 0, false, clock.Elapsed.TotalSeconds);
}
