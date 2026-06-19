namespace CAP_DataAccess.Persistence.PIR;

/// <summary>
/// Persisted representation of a physical optical port derived from a per-instance
/// Nazca raw-code override (issue #561). Stored inside
/// <see cref="NazcaCodeOverride.OverridePins"/> and <see cref="NazcaCodeOverride.TemplatePins"/>.
/// Coordinates are in component-local µm space (same convention as
/// <c>CAP_Core.Components.Core.PhysicalPin</c>).
/// </summary>
public class OverridePinData
{
    /// <summary>Pin name as defined in the override cell (e.g. "a0", "b0").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// X offset from the component bounding-box left edge in micrometers.
    /// Derived from <c>NazcaPreviewPin.X − bbox.XMin</c>.
    /// </summary>
    public double OffsetXMicrometers { get; set; }

    /// <summary>
    /// Y offset from the component bounding-box top edge in micrometers (Y-down).
    /// Derived from <c>bbox.YMax − NazcaPreviewPin.Y</c>.
    /// </summary>
    public double OffsetYMicrometers { get; set; }

    /// <summary>
    /// Port angle in degrees in component-local space.
    /// Derived as the negation of the Nazca preview's pin angle (Y-axis flip).
    /// </summary>
    public double AngleDegrees { get; set; }
}
