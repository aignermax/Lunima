using CAP_Core.Routing.MeanderGeneration;

namespace CAP_Core.Routing.LengthEqualization.Bundle;

/// <summary>
/// Turns a planned wire shape into route geometry from the connection's start pin. The shape is
/// laid near pin to far pin; a reversed wire walks the same pieces backwards (reverse order,
/// every turn mirrored), so its route still starts at its own start pin.
/// </summary>
internal static class BundlePathEmitter
{
    /// <summary>The route of <paramref name="shape"/> for the connection behind <paramref name="request"/>.</summary>
    public static RoutedPath Emit(BundleWireShape shape, MeanderRequest request, double heading)
    {
        var steps = Steps(shape);
        if (shape.Wire.Reversed)
        {
            steps.Reverse();
            steps = steps.Select(s => s.IsArc ? s with { Value = -s.Value } : s).ToList();
        }
        var path = new RoutedPath();
        var builder = new PosePathBuilder(path, request.StartX, request.StartY, request.StartDirectionDegrees);
        foreach (var step in steps)
        {
            if (step.IsArc)
                builder.AppendArc(step.Value, step.Radius);
            else
                builder.AppendStraight(step.Value);
        }
        return path;
    }

    private static List<BundleStep> Steps(BundleWireShape shape)
    {
        var wire = shape.Wire;
        var (first, second) = (shape.FirstJog, shape.SecondJog);
        var steps = new List<BundleStep> { BundleStep.Straight(shape.FirstJogU - wire.StartU) };
        steps.AddRange(first.Steps());
        double firstEnd = shape.FirstJogU + first.Extent;
        if (second.Extent > 0)
        {
            steps.Add(BundleStep.Straight(shape.SecondJogU - firstEnd));
            steps.AddRange(second.Steps());
            steps.Add(BundleStep.Straight(wire.EndU - shape.SecondJogU - second.Extent));
        }
        else
        {
            steps.Add(BundleStep.Straight(wire.EndU - firstEnd));
        }
        return steps;
    }
}
