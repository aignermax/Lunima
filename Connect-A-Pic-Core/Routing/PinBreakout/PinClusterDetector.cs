using CAP_Core.Components.Core;

namespace CAP_Core.Routing.PinBreakout;

/// <summary>
/// A tight pin cluster: wired pins on one face (same outward heading, same forward
/// position) whose neighbours sit closer together than the router can lay parallel wires.
/// </summary>
/// <param name="Pins">The cluster's pins, ordered by lateral position (along heading + 90°).</param>
/// <param name="HeadingDegrees">The shared outward heading in degrees.</param>
public sealed record PinCluster(IReadOnlyList<PhysicalPin> Pins, double HeadingDegrees);

/// <summary>
/// Finds tight pin clusters among wired pins. Pins of different components count too — a
/// group's external pins belong to different inner cells but still form one face.
/// Deterministic: clusters and their pins come out in a stable geometric order.
/// </summary>
public static class PinClusterDetector
{
    /// <summary>Heading tolerance (degrees) for "the same face direction".</summary>
    public const double HeadingToleranceDegrees = 0.5;

    /// <summary>Forward-position tolerance (µm) for "on the same face line".</summary>
    public const double FaceToleranceMicrometers = 0.5;

    /// <summary>Pins closer than this (µm) are the same physical port and never a cluster pair.</summary>
    private const double CoincidentPinMicrometers = 1e-3;

    private const double FullTurnDegrees = 360.0;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>
    /// Groups <paramref name="pins"/> into clusters whose consecutive lateral pitch is below
    /// <paramref name="routablePitchMicrometers"/>. Only clusters of two or more pins are returned.
    /// </summary>
    public static List<PinCluster> Detect(IEnumerable<PhysicalPin> pins, double routablePitchMicrometers)
    {
        var clusters = new List<PinCluster>();
        var distinct = pins.Distinct().ToList();
        foreach (var heading in GroupByHeading(distinct))
        {
            double headingDegrees = MeanHeading(heading);
            foreach (var face in GroupByFace(heading, headingDegrees))
                clusters.AddRange(SplitByPitch(face, headingDegrees, routablePitchMicrometers));
        }
        return clusters;
    }

    /// <summary>Lateral coordinate (µm) of a point along heading + 90°.</summary>
    public static double Lateral(double x, double y, double headingDegrees)
    {
        double rad = headingDegrees * DegreesToRadians;
        return -x * Math.Sin(rad) + y * Math.Cos(rad);
    }

    /// <summary>Forward coordinate (µm) of a point along the heading.</summary>
    public static double Forward(double x, double y, double headingDegrees)
    {
        double rad = headingDegrees * DegreesToRadians;
        return x * Math.Cos(rad) + y * Math.Sin(rad);
    }

    private static IEnumerable<List<PhysicalPin>> GroupByHeading(List<PhysicalPin> pins)
    {
        var ordered = pins.OrderBy(p => Normalize(p.GetAbsoluteAngle())).ToList();
        var group = new List<PhysicalPin>();
        foreach (var pin in ordered)
        {
            if (group.Count > 0 && !SameHeading(group[0].GetAbsoluteAngle(), pin.GetAbsoluteAngle()))
            {
                yield return group;
                group = new List<PhysicalPin>();
            }
            group.Add(pin);
        }
        if (group.Count > 0) yield return group;
    }

    private static IEnumerable<List<PhysicalPin>> GroupByFace(List<PhysicalPin> pins, double headingDegrees)
    {
        var ordered = pins.OrderBy(p => ForwardOf(p, headingDegrees)).ThenBy(p => LateralOf(p, headingDegrees)).ToList();
        var face = new List<PhysicalPin>();
        foreach (var pin in ordered)
        {
            if (face.Count > 0 &&
                ForwardOf(pin, headingDegrees) - ForwardOf(face[0], headingDegrees) > FaceToleranceMicrometers)
            {
                yield return face;
                face = new List<PhysicalPin>();
            }
            face.Add(pin);
        }
        if (face.Count > 0) yield return face;
    }

    private static IEnumerable<PinCluster> SplitByPitch(
        List<PhysicalPin> face, double headingDegrees, double routablePitchMicrometers)
    {
        var ordered = face.OrderBy(p => LateralOf(p, headingDegrees)).ToList();
        var run = new List<PhysicalPin>();
        foreach (var pin in ordered)
        {
            if (run.Count > 0)
            {
                double gap = LateralOf(pin, headingDegrees) - LateralOf(run[^1], headingDegrees);
                if (gap >= routablePitchMicrometers || gap < CoincidentPinMicrometers)
                {
                    if (run.Count > 1) yield return new PinCluster(run, headingDegrees);
                    run = new List<PhysicalPin>();
                }
            }
            run.Add(pin);
        }
        if (run.Count > 1) yield return new PinCluster(run, headingDegrees);
    }

    private static double LateralOf(PhysicalPin pin, double headingDegrees)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return Lateral(x, y, headingDegrees);
    }

    private static double ForwardOf(PhysicalPin pin, double headingDegrees)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return Forward(x, y, headingDegrees);
    }

    private static bool SameHeading(double a, double b)
    {
        double diff = Math.Abs(Normalize(a) - Normalize(b));
        return Math.Min(diff, FullTurnDegrees - diff) <= HeadingToleranceDegrees;
    }

    private static double MeanHeading(List<PhysicalPin> pins) => Normalize(pins[0].GetAbsoluteAngle());

    private static double Normalize(double degrees)
    {
        double normalized = degrees % FullTurnDegrees;
        return normalized < 0 ? normalized + FullTurnDegrees : normalized;
    }
}
