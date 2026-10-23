using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>Immutable identities and parameters; commands bind only to the receiving design.</summary>
public abstract record RepairInstruction
{
    public abstract (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session);
}

public sealed record NudgeRepair(Guid ComponentId, double Dx, double Dy) : RepairInstruction
{
    public override (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session)
    {
        var vm = session.Canvas.Components.FirstOrDefault(view => view.Component.Id == ComponentId);
        if (vm == null || vm.Component.IsLocked) return (null, "The component is missing or locked.");
        var (x, y) = (vm.X, vm.Y);
        if (!session.Canvas.CanMoveComponentTo(vm, x + Dx, y + Dy))
            return (null, "The move would overlap another component.");
        return (new(new MoveComponentCommand(session.Canvas, vm, x, y, x + Dx, y + Dy),
            () => Math.Abs(vm.X - x - Dx) < 1e-6 && Math.Abs(vm.Y - y - Dy) < 1e-6), null);
    }
}

public sealed record SlideRepair(Guid ComponentId, CrossingAxis Axis, double Delta) : RepairInstruction
{
    public override (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session)
    {
        var component = session.FindComponent(ComponentId.ToString())?.Component;
        if (component == null) return (null, "The crossing is missing.");
        var error = MoveCrossingAlongAxisCommand.Validate(session.Canvas, component, Axis, Delta);
        if (error != null) return (null, error);
        var command = new MoveCrossingAlongAxisCommand(session.Canvas, component, Axis, Delta);
        return (new(command, () => command.Succeeded), null);
    }
}

public sealed record ShiftRepair(Guid ConnectionId, int StraightIndex, double Delta) : RepairInstruction
{
    public override (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session)
    {
        var wire = session.FindConnection(ConnectionId.ToString());
        if (wire == null) return (null, "The wire is missing.");
        var command = ShiftSegmentTool.Build(session, wire, StraightIndex, Delta, out var error);
        if (command == null) return (null, error);
        var target = ShiftSegmentTool.CurrentOffset(wire.Connection, StraightIndex) + Delta;
        return (new(command, () => Math.Abs(ShiftSegmentTool.CurrentOffset(wire.Connection, StraightIndex) - target) < 1e-6), null);
    }
}

public sealed record CutRepair(Guid CutId, Guid ThroughId, double X, double Y) : RepairInstruction
{
    public override (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session)
    {
        var cut = session.FindConnection(CutId.ToString())?.Connection;
        var through = session.FindConnection(ThroughId.ToString())?.Connection;
        if (cut == null || through == null) return (null, "A cut wire is missing.");
        var plan = CrossingCut.Create(session, cut, X, Y, through, out var error);
        return plan == null ? (null, error)
            : (new RepairEditPlan(plan.Command, () => plan.Insert.Succeeded && plan.ThroughStep?.Applied == true), null);
    }
}

public sealed record ReorderRepair(Guid ConnectionId) : RepairInstruction
{
    public override (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session)
    {
        var wire = session.FindConnection(ConnectionId.ToString());
        if (wire == null) return (null, "The neighbour wire is gone.");
        var connection = wire.Connection;
        return (new(new DeleteConnectionCommand(session.Canvas, wire),
            () => session.Canvas.Connections.All(vm => vm.Connection != connection),
            [new CreateConnectionCommand(session.Canvas, connection.StartPin, connection.EndPin)]), null);
    }
}

/// <summary>A bounded, immutable two-step repair; the second command follows a complete routing pass.</summary>
public sealed record TwoStepRepair(RepairInstruction First, RepairInstruction Second) : RepairInstruction
{
    public override (RepairEditPlan? Plan, string? Error) Build(AgentDesignSession session)
    {
        var (first, error) = First.Build(session);
        if (first == null) return (null, error);
        var (second, nextError) = Second.Build(session);
        if (second == null) return (null, nextError);
        if (first.FollowUps?.Count > 0 || second.FollowUps?.Count > 0)
            throw new InvalidOperationException("Nested multi-step repairs are not supported.");
        return (first with { FollowUps = new[] { second.Command } }, null);
    }
}
