using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>
/// Deterministic repair ideas for one blocked wire, in the order a person would try them:
/// cut a wire in the way and route the blocked wire straight through the crossing (swaps the
/// order of two wires, e.g. at an MMI's 8 µm input pair); slide a nearby crossing along one
/// of its wires; shift a straight of a neighbouring wire; nudge the component at either end
/// across or along its pin axis.
/// The categories are interleaved so a short time budget still samples each of them.
/// </summary>
public sealed partial class RepairCandidateGenerator
{
    /// <summary>Most candidates taken from each category.</summary>
    public const int PerCategory = 4;

    /// <summary>Crossings this close (µm) to a pin of the blocked wire are slid.</summary>
    public const double CrossingSearchRadiusMicrometers = 80;

    private static readonly double[] CrossingSteps = { 10, -10, 20, -20 };

    private readonly AgentDesignSession _session;
    private readonly WaveguideConnection _blocked;

    /// <summary>Creates the generator for <paramref name="blocked"/>.</summary>
    public RepairCandidateGenerator(AgentDesignSession session, WaveguideConnection blocked)
    {
        _session = session;
        _blocked = blocked;
    }

    /// <summary>All candidates, interleaved by category (cut, neighbour reorder, crossing slide, shift, nudge across and along the pin axis).</summary>
    public IReadOnlyList<RepairCandidate> Generate()
    {
        var categories = new[] { CutThrough(), NeighbourReorders(), CrossingSlides(), SegmentShifts(), Nudges(), AxisNudges() }
            .Select(c => c.Take(PerCategory).ToList())
            .ToList();
        var result = new List<RepairCandidate>();
        for (var i = 0; i < PerCategory; i++)
            foreach (var category in categories.Where(c => i < c.Count))
                result.Add(category[i]);
        return result;
    }

    private IEnumerable<RepairCandidate> CutThrough()
    {
        var supply = CrossingSupply.Resolve(_session);
        if (supply == null)
            return Enumerable.Empty<RepairCandidate>();

        return new CutSpotFinder(_session, supply).Find(_blocked)
            .Where(s => s.Fits && s.Through == _blocked && !s.Cut.IsBlockedFallback)
            .OrderBy(s => DistanceToPins(s.Point))
            .Select(CutCandidate);
    }

    private RepairCandidate CutCandidate(CutSpot spot)
    {
        var (x, y) = spot.Point;
        var cut = spot.Cut;
        var description = string.Create(CultureInfo.InvariantCulture,
            $"Cut {Name(cut)} at ({x:F1}, {y:F1}) with a crossing and route the blocked wire straight through it");
        return new RepairCandidate("cut_through", description, 0,
            new { Tool = "insert_crossing", ConnectionId = cut.Id.ToString(), X = x, Y = y, ThroughConnectionId = _blocked.Id.ToString() },
            () =>
            {
                var plan = CrossingCut.Create(_session, cut, x, y, _blocked, out var error);
                return plan == null
                    ? (null, error)
                    : (new RepairEditPlan(plan.Command, () => plan.Insert.Succeeded && plan.ThroughStep?.Applied == true), null);
            });
    }

    private IEnumerable<RepairCandidate> CrossingSlides()
    {
        var crossings = _session.Canvas.Components
            .Where(vm => CrossingComponentCatalog.IsCrossing(vm.Component))
            .Select(vm => (Crossing: vm.Component, Distance: DistanceToPins(Center(vm.Component))))
            .Where(c => c.Distance <= CrossingSearchRadiusMicrometers)
            .OrderBy(c => c.Distance);

        foreach (var (crossing, _) in crossings)
            foreach (var axis in new[] { CrossingAxis.Horizontal, CrossingAxis.Vertical })
                foreach (var step in CrossingSteps)
                {
                    if (MoveCrossingAlongAxisCommand.Validate(_session.Canvas, crossing, axis, step) != null)
                        continue;
                    yield return CrossingSlideCandidate(crossing, axis, step);
                }
    }

    private RepairCandidate CrossingSlideCandidate(Component crossing, CrossingAxis axis, double step)
    {
        var axisName = axis == CrossingAxis.Horizontal ? "horizontal" : "vertical";
        var description = string.Create(CultureInfo.InvariantCulture,
            $"Slide crossing {crossing.Identifier} {step:+0;-0} µm along its {axisName} wire");
        return new RepairCandidate("move_crossing", description, Math.Abs(step),
            new { Tool = "move_crossing_along_axis", CrossingId = crossing.Identifier, Axis = axisName, DeltaUm = step },
            () =>
            {
                var error = MoveCrossingAlongAxisCommand.Validate(_session.Canvas, crossing, axis, step);
                if (error != null)
                    return (null, error);
                var command = new MoveCrossingAlongAxisCommand(_session.Canvas, crossing, axis, step);
                return (new RepairEditPlan(command, () => command.Succeeded), null);
            });
    }

    private double DistanceToPins((double X, double Y) point)
    {
        var (sx, sy) = _blocked.StartPin.GetAbsolutePosition();
        var (ex, ey) = _blocked.EndPin.GetAbsolutePosition();
        return Math.Min(Hypot(point.X - sx, point.Y - sy), Hypot(point.X - ex, point.Y - ey));
    }

    private static (double X, double Y) Center(Component component) =>
        (component.PhysicalX + component.WidthMicrometers / 2.0, component.PhysicalY + component.HeightMicrometers / 2.0);

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private static string Name(WaveguideConnection c) =>
        $"{c.StartPin.ParentComponent.Identifier}.{c.StartPin.Name}→{c.EndPin.ParentComponent.Identifier}.{c.EndPin.Name}";
}
