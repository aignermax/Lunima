using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls.Canvas.CutTool;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>
/// One cut as an undoable command: the Cut tool's own <see cref="CutAtPoint"/> /
/// <see cref="InsertManualCrossingCommand"/> (same checks and behaviour as a click in Cut
/// mode, including cutting crossing pieces) plus, when a through wire is given,
/// <see cref="RouteThroughCrossingCommand"/> docking that wire onto the crossing's free ports —
/// recorded as one undo step. This is the only place the agent bridge builds a cut.
/// </summary>
public sealed class CrossingCut
{
    private CrossingCut(IUndoableCommand command, InsertManualCrossingCommand insert, RouteThroughCrossingCommand? throughStep)
    {
        Command = command;
        Insert = insert;
        ThroughStep = throughStep;
    }

    /// <summary>The command to run on the shared undo history.</summary>
    public IUndoableCommand Command { get; }

    /// <summary>The crossing insertion step.</summary>
    public InsertManualCrossingCommand Insert { get; }

    /// <summary>The re-dock step, or null for a plain cut.</summary>
    public RouteThroughCrossingCommand? ThroughStep { get; }

    /// <summary>The crossing component the command inserts.</summary>
    public Component Crossing => Insert.Crossing;

    /// <summary>
    /// Builds the cut of <paramref name="cut"/> at (<paramref name="x"/>, <paramref name="y"/>),
    /// optionally routing <paramref name="through"/> through the new crossing; null with an
    /// <paramref name="error"/> when the Cut tool would refuse that point.
    /// </summary>
    public static CrossingCut? Create(AgentDesignSession session, WaveguideConnection cut, double x, double y,
        WaveguideConnection? through, out string? error)
    {
        var insert = CutAtPoint.CreateCommand(session.Canvas, session.Templates, cut, x, y, out error)
                     ?? CutIgnoringThroughWire(session, cut, x, y, through);
        if (insert == null)
            return null;
        error = null;
        if (through == null || through == cut)
            return new CrossingCut(insert, insert, null);

        var routeThrough = new RouteThroughCrossingCommand(session.Canvas, through, insert.Crossing);
        var composite = new CompositeCommand(
            $"Cut {cut.StartPin.Name}–{cut.EndPin.Name} and route {through.StartPin.Name}–{through.EndPin.Name} through",
            new IUndoableCommand[] { insert, routeThrough });
        return new CrossingCut(composite, insert, routeThrough);
    }

    /// <summary>
    /// The Cut tool refuses a footprint that overlaps any wire but the cut one. When the only
    /// other wire there is the through wire — which the composite re-docks onto the crossing,
    /// so its old path goes away — the cut is built from the same candidate finder with that
    /// wire exempted.
    /// </summary>
    private static InsertManualCrossingCommand? CutIgnoringThroughWire(
        AgentDesignSession session, WaveguideConnection cut, double x, double y, WaveguideConnection? through)
    {
        var supply = CrossingSupply.Resolve(session);
        if (through == null || supply == null
            || !CutToolCandidateComputer.CollectEligibleConnections(session.Canvas).Contains(cut))
            return null;
        var candidate = new ManualCrossingCandidateFinder().FindNearestFreeCandidate(
            new[] { cut }, (x, y), CutAtPoint.DefaultSnapDistanceMicrometers, supply.RequiredRunMicrometers);
        if (candidate == null || !supply.IsFootprintClear(candidate.IntersectionPoint, cut, through))
            return null;
        var instance = CrossingComponentInstance.CreateFromTemplates(session.Templates);
        return instance == null ? null : new InsertManualCrossingCommand(session.Canvas, candidate, instance);
    }
}
