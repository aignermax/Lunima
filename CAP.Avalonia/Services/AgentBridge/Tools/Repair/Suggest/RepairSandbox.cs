using System.Diagnostics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.Snapshots;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>Runs each edit on a fresh private graph; the user's model and history are never trial state.</summary>
public sealed class RepairSandbox
{
    private readonly RoutingInputSnapshot _input;
    private readonly ExampleDesignsService _examples;
    private readonly RepairCrossingTemplate? _crossing;
    private readonly RepairTrialPermission _permission;
    private readonly CanvasRoutingFactory? _routingFactory;
    private readonly RepairGraphBuilder? _graphBuilder;
    private readonly Guid _target;
    private readonly Guid? _scope;
    private readonly (double, double, double, double) _bounds;

    internal RepairSandbox(AgentDesignSession session, WaveguideConnection target,
        RepairTrialPermission permission, CanvasRoutingFactory? trialRoutingFactory, RepairGraphBuilder? graphBuilder = null)
    {
        _permission = permission;
        _examples = session.Examples;
        _routingFactory = trialRoutingFactory;
        _graphBuilder = graphBuilder;
        _target = target.Id;
        var canvas = session.Canvas;
        _scope = canvas.CurrentEditGroup?.Id;
        _input = RoutingInputSnapshot.Capture(canvas.Components.Select(view => view.Component).ToArray(), canvas.Router, canvas.ConnectionManager);
        _crossing = RepairCrossingTemplate.Capture(session);
        _bounds = (canvas.ChipMinX, canvas.ChipMinY, canvas.ChipMaxX, canvas.ChipMaxY);
    }

    /// <summary>Retained response field: private trials cannot drift the live design.</summary>
    public string? Drift => null;

    public async Task<TrialOutcome> TryAsync(RepairCandidate candidate, RoutingMetrics baseline)
    {
        _permission.EnsureCurrent();
        var clock = Stopwatch.StartNew();
        await using var workbench = await RepairWorkbench.CreateAsync(_examples, _input, _crossing, _permission, _routingFactory, _bounds, _scope, _graphBuilder);
        var session = workbench.Session;
        var target = session.FindConnection(_target.ToString())!.Connection;
        var (plan, error) = candidate.Build(session);
        if (plan == null) return NotApplicable(candidate, error, clock);
        if (!await workbench.ExecuteAsync(plan))
            return NotApplicable(candidate, "the edit did not take effect here", clock);
        _permission.EnsureCurrent();
        var after = RoutingMetrics.Measure(session);
        var stillBlocked = session.Canvas.Connections.Any(view => view.Connection.IsBlockedFallback
            && (Touches(view.Connection, target.StartPin.PinId) || Touches(view.Connection, target.EndPin.PinId)));
        return new(candidate, true, null, after.BlockedCount,
            after.CrossingCount - baseline.CrossingCount, !stillBlocked, clock.Elapsed.TotalSeconds);
    }

    private static bool Touches(WaveguideConnection wire, Guid pin) => wire.StartPin.PinId == pin || wire.EndPin.PinId == pin;
    private static TrialOutcome NotApplicable(RepairCandidate candidate, string? reason, Stopwatch clock) =>
        new(candidate, false, reason, -1, 0, false, clock.Elapsed.TotalSeconds);
}
