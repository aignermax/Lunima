using CAP.Avalonia.Commands;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>
/// One cut as an undoable command: <see cref="InsertManualCrossingCommand"/> (exactly what the
/// Cut tool's click runs, so fixes to the Cut tool apply here too) plus, when a through wire is
/// given, <see cref="RouteThroughCrossingCommand"/> docking that wire onto the crossing's free
/// ports — recorded as one undo step. This is the only place the agent bridge builds a cut.
/// </summary>
public sealed class CrossingCut
{
    private CrossingCut(IUndoableCommand command, Component crossing, RouteThroughCrossingCommand? throughStep)
    {
        Command = command;
        Crossing = crossing;
        ThroughStep = throughStep;
    }

    /// <summary>The command to run on the shared undo history.</summary>
    public IUndoableCommand Command { get; }

    /// <summary>The crossing component the command inserts.</summary>
    public Component Crossing { get; }

    /// <summary>The re-dock step, or null for a plain cut.</summary>
    public RouteThroughCrossingCommand? ThroughStep { get; }

    /// <summary>Builds the cut, or null when no crossing instance can be created.</summary>
    public static CrossingCut? Create(AgentDesignSession session, CrossingSupply supply,
        ManualCrossingCandidate candidate, WaveguideConnection? through)
    {
        var instance = supply.CreateInstance();
        if (instance == null)
            return null;

        var insert = new InsertManualCrossingCommand(session.Canvas, candidate, instance);
        if (through == null || through == candidate.Connection)
            return new CrossingCut(insert, instance.Component, null);

        var routeThrough = new RouteThroughCrossingCommand(
            session.Canvas, through, instance.Component, candidate.SegmentIsHorizontal);
        var composite = new CompositeCommand(
            $"Cut {candidate.Connection.StartPin.Name}–{candidate.Connection.EndPin.Name} and route {through.StartPin.Name}–{through.EndPin.Name} through",
            new IUndoableCommand[] { insert, routeThrough });
        return new CrossingCut(composite, instance.Component, routeThrough);
    }

    /// <summary>Whether the crossing is on the canvas after <see cref="Command"/> ran.</summary>
    public bool Inserted(AgentDesignSession session) =>
        session.Canvas.Components.Any(vm => vm.Component == Crossing);
}
