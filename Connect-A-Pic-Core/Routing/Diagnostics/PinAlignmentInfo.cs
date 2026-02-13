namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Describes the geometric alignment relationship between two pins.
/// </summary>
public class PinAlignmentInfo
{
    /// <summary>
    /// Euclidean distance between pins in micrometers.
    /// </summary>
    public double DistanceMicrometers { get; set; }

    /// <summary>
    /// Forward distance from start pin (along its facing direction) in micrometers.
    /// Negative means the end pin is behind the start pin.
    /// </summary>
    public double ForwardDistanceMicrometers { get; set; }

    /// <summary>
    /// Lateral offset perpendicular to start pin direction in micrometers.
    /// </summary>
    public double LateralOffsetMicrometers { get; set; }

    /// <summary>
    /// Angle difference between start pin direction and end pin input direction.
    /// </summary>
    public double AngleDifferenceDegrees { get; set; }

    /// <summary>
    /// Whether the pins are collinear (aligned on the same axis).
    /// </summary>
    public bool AreCollinear { get; set; }

    /// <summary>
    /// Whether the pins face each other (anti-parallel directions).
    /// </summary>
    public bool AreFacing { get; set; }
}
