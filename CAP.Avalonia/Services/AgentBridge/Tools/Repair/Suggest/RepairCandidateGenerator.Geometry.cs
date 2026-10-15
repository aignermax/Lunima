using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.InterconnectRouting.SegmentShift;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>Geometry nudges: shifting straights of neighbouring wires and moving the end components.</summary>
public sealed partial class RepairCandidateGenerator
{
    /// <summary>Wires with a pin this close (µm) to a pin of the blocked wire count as its pin neighbours.</summary>
    public const double NeighbourRadiusMicrometers = PinPairRerouter.NeighbourPinRadiusMicrometers;

    /// <summary>Shiftable straights (nearest the blocked wire's pins) whose shifts are tried.</summary>
    private const int NearestHandles = 2;

    private static readonly double[] ShiftSteps = { 10, -10, 20, -20 };
    private static readonly double[] NudgeSteps = { 10, -10, 20, -20 };

    private IEnumerable<RepairCandidate> SegmentShifts()
    {
        var neighbours = _session.Canvas.Connections
            .Where(vm => vm.Connection != _blocked && !vm.Connection.IsBlockedFallback && !vm.Connection.IsElectrical)
            .Where(vm => IsPinNeighbour(vm.Connection.StartPin) || IsPinNeighbour(vm.Connection.EndPin))
            .ToList();

        var handles = neighbours
            .SelectMany(vm => SegmentShiftGeometry.GetHandles(vm.Connection.GetPathSegments()).Select(h => (Wire: vm, Handle: h)))
            .OrderBy(x => DistanceToPins(x.Handle.Midpoint))
            .Take(NearestHandles)
            .ToList();

        foreach (var step in ShiftSteps)
            foreach (var (wire, handle) in handles)
                yield return ShiftCandidate(wire, handle.StraightIndex, step);
    }

    private RepairCandidate ShiftCandidate(WaveguideConnectionViewModel wire, int straightIndex, double step)
    {
        var connection = wire.Connection;
        var description = string.Create(CultureInfo.InvariantCulture,
            $"Shift straight #{straightIndex} of neighbouring wire {Name(connection)} by {step:+0;-0} µm");
        return new RepairCandidate("shift_segment", description, Math.Abs(step),
            new { Tool = "shift_segment", ConnectionId = connection.Id.ToString(), StraightIndex = straightIndex, OffsetUm = step },
            () =>
            {
                var command = ShiftSegmentTool.Build(_session, wire, straightIndex, step, out var error);
                if (command == null)
                    return (null, error);
                var target = ShiftSegmentTool.CurrentOffset(connection, straightIndex) + step;
                return (new RepairEditPlan(command,
                    () => Math.Abs(ShiftSegmentTool.CurrentOffset(connection, straightIndex) - target) < 1e-6), null);
            });
    }

    private IEnumerable<RepairCandidate> Nudges()
    {
        var ends = new[] { _blocked.EndPin, _blocked.StartPin }
            .Select(pin => (Pin: pin, Vm: TopLevelViewModel(pin.ParentComponent)))
            .Where(x => x.Vm != null && !x.Vm.Component.IsLocked
                        && !CrossingComponentCatalog.IsCrossing(x.Vm.Component))
            .GroupBy(x => x.Vm)
            .Select(g => g.First())
            .ToList();

        foreach (var step in NudgeSteps)
            foreach (var (pin, vm) in ends)
            {
                var (dx, dy) = AcrossPin(pin, step);
                if (!_session.Canvas.CanMoveComponentTo(vm!, vm!.X + dx, vm.Y + dy))
                    continue;
                yield return NudgeCandidate(vm, dx, dy);
            }
    }

    /// <summary>
    /// Moves the component at either end along its pin's axis — towards or away from the
    /// wire. Moving a gate a pitch away from a crowded channel widens the channel in front of
    /// its pins, which an across-pin nudge cannot do.
    /// </summary>
    private IEnumerable<RepairCandidate> AxisNudges()
    {
        var ends = new[] { _blocked.EndPin, _blocked.StartPin }
            .Select(pin => (Pin: pin, Vm: TopLevelViewModel(pin.ParentComponent)))
            .Where(x => x.Vm != null && !x.Vm.Component.IsLocked
                        && !CrossingComponentCatalog.IsCrossing(x.Vm.Component))
            .GroupBy(x => x.Vm)
            .Select(g => g.First())
            .ToList();

        foreach (var step in NudgeSteps)
            foreach (var (pin, vm) in ends)
            {
                var (dx, dy) = AlongPin(pin, step);
                if (!_session.Canvas.CanMoveComponentTo(vm!, vm!.X + dx, vm.Y + dy))
                    continue;
                yield return NudgeCandidate(vm, dx, dy);
            }
    }

    private RepairCandidate NudgeCandidate(ComponentViewModel vm, double dx, double dy)
    {
        var id = vm.Component.Identifier;
        var description = string.Create(CultureInfo.InvariantCulture, $"Move {id} by ({dx:+0;-0;0}, {dy:+0;-0;0}) µm");
        return new RepairCandidate("nudge_component", description, Hypot(dx, dy),
            new { Tool = "move_component", Id = id, Dx = dx, Dy = dy },
            () =>
            {
                var (x, y) = (vm.X, vm.Y);
                if (!_session.Canvas.CanMoveComponentTo(vm, x + dx, y + dy))
                    return (null, "The move would overlap another component.");
                var command = new MoveComponentCommand(_session.Canvas, vm, x, y, x + dx, y + dy);
                return (new RepairEditPlan(command, () => Math.Abs(vm.X - x - dx) < 1e-6 && Math.Abs(vm.Y - y - dy) < 1e-6), null);
            });
    }

    /// <summary>A move perpendicular to the pin's facing direction — it changes the order wires arrive in.</summary>
    private static (double Dx, double Dy) AlongPin(PhysicalPin pin, double step)
    {
        var radians = pin.GetAbsoluteAngle() * Math.PI / 180.0;
        return Math.Abs(Math.Cos(radians)) >= Math.Abs(Math.Sin(radians)) ? (step, 0) : (0, step);
    }

    private static (double Dx, double Dy) AcrossPin(PhysicalPin pin, double step)
    {
        var radians = pin.GetAbsoluteAngle() * Math.PI / 180.0;
        return Math.Abs(Math.Cos(radians)) >= Math.Abs(Math.Sin(radians)) ? (0, step) : (step, 0);
    }

    private bool IsPinNeighbour(PhysicalPin pin)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return DistanceToPins((x, y)) <= NeighbourRadiusMicrometers
               && pin != _blocked.StartPin && pin != _blocked.EndPin;
    }

    private ComponentViewModel? TopLevelViewModel(Component component)
    {
        var top = DesignDescriber.TopLevelOf(component);
        return _session.Canvas.Components.FirstOrDefault(vm => vm.Component == top);
    }
}
