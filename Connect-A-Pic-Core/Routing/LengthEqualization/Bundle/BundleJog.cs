namespace CAP_Core.Routing.LengthEqualization.Bundle;

/// <summary>
/// A sideways offset of a bundle wire: it leaves its run heading along the bundle axis and
/// rejoins it <c>Height</c> further across. From twice the radius on it is two quarter arcs
/// with a straight leg between; below that, two equal partial arcs (an S-bend), so any height —
/// and therefore any extra length — is reachable without a gap.
/// </summary>
/// <param name="Height">Signed offset across the axis (positive = toward +v, a counter-clockwise first turn).</param>
/// <param name="Radius">Arc radius.</param>
internal readonly record struct BundleJog(double Height, double Radius)
{
    private const double QuarterTurnDegrees = 90.0;

    /// <summary>Length along the axis the jog occupies.</summary>
    public double Extent => IsFull ? 2 * Radius : Math.Abs(Height) < 1e-9 ? 0 : 2 * Radius * Math.Sin(PartialAngle);

    /// <summary>Path length of the jog.</summary>
    public double Length => IsFull
        ? Math.PI * Radius + Math.Abs(Height) - 2 * Radius
        : 2 * Radius * PartialAngle;

    /// <summary>How much longer the jog is than the straight it replaces along the axis.</summary>
    public double Excess => Length - Extent;

    private bool IsFull => Math.Abs(Height) >= 2 * Radius;

    /// <summary>Arc angle (radians) of each half of a partial jog.</summary>
    private double PartialAngle => Math.Acos(1 - Math.Abs(Height) / (2 * Radius));

    /// <summary>The jog as path steps, travelling along +u.</summary>
    public IEnumerable<BundleStep> Steps()
    {
        if (Math.Abs(Height) < 1e-9)
            yield break;
        double sign = Math.Sign(Height);
        double turn = IsFull ? QuarterTurnDegrees : PartialAngle * 180 / Math.PI;
        yield return BundleStep.Arc(sign * turn, Radius);
        if (IsFull)
            yield return BundleStep.Straight(Math.Abs(Height) - 2 * Radius);
        yield return BundleStep.Arc(-sign * turn, Radius);
    }
}

/// <summary>One piece of a bundle wire: a straight of <c>Value</c> µm or an arc of <c>Value</c> degrees.</summary>
/// <param name="IsArc">True for an arc.</param>
/// <param name="Value">Straight length (µm) or signed sweep (degrees, positive = counter-clockwise).</param>
/// <param name="Radius">Arc radius; 0 for a straight.</param>
internal readonly record struct BundleStep(bool IsArc, double Value, double Radius)
{
    /// <summary>A straight piece.</summary>
    public static BundleStep Straight(double length) => new(false, length, 0);

    /// <summary>An arc piece.</summary>
    public static BundleStep Arc(double sweepDegrees, double radius) => new(true, sweepDegrees, radius);
}
