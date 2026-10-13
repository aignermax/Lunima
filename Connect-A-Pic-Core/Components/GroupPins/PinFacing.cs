namespace CAP_Core.Components.GroupPins;

/// <summary>
/// The canvas direction a pin points out of its component, snapped to the nearest
/// side. The app is Y-down: 0° points east (right), 90° south (down), 180° west
/// (left) and 270° north (up).
/// </summary>
public enum PinFacing
{
    /// <summary>Points right (around 0°).</summary>
    East,

    /// <summary>Points down (around 90°).</summary>
    South,

    /// <summary>Points left (around 180°).</summary>
    West,

    /// <summary>Points up (around 270°).</summary>
    North,
}

/// <summary>Snaps a world-space pin angle onto the nearest <see cref="PinFacing"/>.</summary>
public static class PinFacingClassifier
{
    private const double FullTurnDegrees = 360.0;
    private const double QuarterTurnDegrees = 90.0;
    private const double HalfQuarterDegrees = 45.0;

    /// <summary>
    /// Returns the side a pin with the given world-space angle points to. Angles
    /// exactly on a diagonal resolve to the next side counter-clockwise in angle order
    /// (45° → south), which keeps the result deterministic.
    /// </summary>
    /// <param name="angleDegrees">World-space pin angle in degrees, any range.</param>
    public static PinFacing Classify(double angleDegrees)
    {
        var normalized = angleDegrees % FullTurnDegrees;
        if (normalized < 0)
            normalized += FullTurnDegrees;
        var quadrant = (int)Math.Floor((normalized + HalfQuarterDegrees) / QuarterTurnDegrees) % 4;
        return (PinFacing)quadrant;
    }
}
