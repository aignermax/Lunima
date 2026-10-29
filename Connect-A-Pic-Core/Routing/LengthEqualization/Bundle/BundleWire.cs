using CAP_Core.Routing.MeanderGeneration;

namespace CAP_Core.Routing.LengthEqualization.Bundle;

/// <summary>
/// One connection of a bundle in the bundle's local frame: <c>u</c> runs along the common pin
/// direction from the near column to the far one, <c>v</c> across it. A reversed wire's
/// connection starts on the far column; its route is laid near to far and then walked backwards.
/// </summary>
/// <param name="Item">Index of the connection in the equalization plan.</param>
/// <param name="StartU">Near pin, along the axis.</param>
/// <param name="StartV">Near pin, across the axis.</param>
/// <param name="EndU">Far pin, along the axis.</param>
/// <param name="EndV">Far pin, across the axis.</param>
/// <param name="Reversed">True when the connection's start pin is the far one.</param>
internal sealed record BundleWire(int Item, double StartU, double StartV, double EndU, double EndV, bool Reversed);

/// <summary>
/// Recognises a bundle: every connection joins a pin facing along one direction to a pin facing
/// straight back at it, the far pin lies ahead of the near one, and the wires keep their order
/// across the axis at both ends — so they can be laid side by side without crossing.
/// </summary>
internal static class BundleDetector
{
    private const double AngleToleranceDegrees = 1e-3;

    private const int MinimumWires = 2;

    /// <summary>
    /// The largest group of connections whose pins face each other along one axis, as a bundle
    /// sorted across that axis with the frame's heading; null when that group does not form a
    /// bundle. Connections outside the group (other directions, loops back to the same column)
    /// are not part of it.
    /// </summary>
    public static (double HeadingDegrees, IReadOnlyList<BundleWire> Wires)? Detect(IReadOnlyList<MeanderRequest> requests)
    {
        var group = requests.Select((r, i) => (Request: r, Item: i))
            .Where(x => SameAngle(x.Request.StartDirectionDegrees, x.Request.EndDirectionDegrees))
            .GroupBy(x => Math.Round(((x.Request.StartDirectionDegrees % 180) + 180) % 180, 3) % 180)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (group == null || group.Count() < MinimumWires)
            return null;
        double heading = group.First().Request.StartDirectionDegrees;
        double cos = Math.Cos(heading * Math.PI / 180), sin = Math.Sin(heading * Math.PI / 180);
        var wires = new List<BundleWire>();
        foreach (var (request, item) in group)
        {
            var wire = ToWire(item, request, heading, cos, sin);
            if (wire == null || wire.EndU <= wire.StartU)
                return null;
            wires.Add(wire);
        }
        wires.Sort((a, b) => a.StartV.CompareTo(b.StartV));
        for (int i = 1; i < wires.Count; i++)
        {
            if (wires[i].StartV <= wires[i - 1].StartV || wires[i].EndV <= wires[i - 1].EndV)
                return null;
        }
        return (heading, wires);
    }

    private static BundleWire? ToWire(int item, MeanderRequest request, double heading, double cos, double sin)
    {
        bool forward = SameAngle(request.StartDirectionDegrees, heading) && SameAngle(request.EndDirectionDegrees, heading);
        bool backward = SameAngle(request.StartDirectionDegrees, heading + 180) && SameAngle(request.EndDirectionDegrees, heading + 180);
        if (!forward && !backward)
            return null;
        var near = forward ? (request.StartX, request.StartY) : (request.EndX, request.EndY);
        var far = forward ? (request.EndX, request.EndY) : (request.StartX, request.StartY);
        return new BundleWire(item,
            near.Item1 * cos + near.Item2 * sin, -near.Item1 * sin + near.Item2 * cos,
            far.Item1 * cos + far.Item2 * sin, -far.Item1 * sin + far.Item2 * cos,
            !forward);
    }

    private static bool SameAngle(double a, double b)
    {
        double difference = ((a - b) % 360 + 360) % 360;
        return difference < AngleToleranceDegrees || difference > 360 - AngleToleranceDegrees;
    }
}
