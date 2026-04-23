namespace CAP_Core.Grid;

/// <summary>
/// Represents the physical dimensions of a PIC chip and provides helpers for preset selection
/// and tile-grid dimension computation based on the standard grid pitch.
/// </summary>
public class ChipSizeConfiguration
{
    /// <summary>Default chip width in micrometers (5 mm).</summary>
    public const double DefaultWidthMicrometers = 5000.0;

    /// <summary>Default chip height in micrometers (5 mm).</summary>
    public const double DefaultHeightMicrometers = 5000.0;

    /// <summary>Gets the chip width in micrometers.</summary>
    public double WidthMicrometers { get; }

    /// <summary>Gets the chip height in micrometers.</summary>
    public double HeightMicrometers { get; }

    /// <summary>
    /// Number of tile columns at the standard <see cref="PhotonicConstants.GridSizeMicrometers"/> pitch.
    /// </summary>
    public int TileColumns => (int)(WidthMicrometers / PhotonicConstants.GridSizeMicrometers);

    /// <summary>
    /// Number of tile rows at the standard <see cref="PhotonicConstants.GridSizeMicrometers"/> pitch.
    /// </summary>
    public int TileRows => (int)(HeightMicrometers / PhotonicConstants.GridSizeMicrometers);

    /// <summary>
    /// Initializes a new chip size from physical dimensions in micrometers.
    /// </summary>
    /// <param name="widthMicrometers">Chip width in micrometers (must be positive).</param>
    /// <param name="heightMicrometers">Chip height in micrometers (must be positive).</param>
    public ChipSizeConfiguration(double widthMicrometers, double heightMicrometers)
    {
        if (widthMicrometers <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthMicrometers), "Width must be positive.");
        if (heightMicrometers <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightMicrometers), "Height must be positive.");

        WidthMicrometers = widthMicrometers;
        HeightMicrometers = heightMicrometers;
    }

    /// <summary>
    /// Creates a <see cref="ChipSizeConfiguration"/> from millimeter values.
    /// </summary>
    /// <param name="widthMm">Width in millimeters.</param>
    /// <param name="heightMm">Height in millimeters.</param>
    public static ChipSizeConfiguration FromMillimeters(double widthMm, double heightMm)
        => new ChipSizeConfiguration(widthMm * 1000.0, heightMm * 1000.0);

    /// <summary>
    /// Common MPW / shuttle-mask presets (width × height in mm).
    /// The last entry is a sentinel for "Custom" and has zero dimensions.
    /// </summary>
    public static IReadOnlyList<ChipSizePreset> Presets { get; } = new ChipSizePreset[]
    {
        new ChipSizePreset("2 × 2 mm",   2.0,  2.0),
        new ChipSizePreset("5 × 5 mm",   5.0,  5.0),
        new ChipSizePreset("10 × 10 mm", 10.0, 10.0),
        new ChipSizePreset("20 × 30 mm", 20.0, 30.0),
        new ChipSizePreset("Custom",      0.0,  0.0),
    };
}

/// <summary>
/// Named preset for a common MPW / shuttle-mask chip size.
/// </summary>
public class ChipSizePreset
{
    /// <summary>Human-readable display name shown in the UI.</summary>
    public string Name { get; }

    /// <summary>Width in millimeters, or 0 for the Custom sentinel.</summary>
    public double WidthMm { get; }

    /// <summary>Height in millimeters, or 0 for the Custom sentinel.</summary>
    public double HeightMm { get; }

    /// <summary>
    /// Returns <c>true</c> for the Custom sentinel entry (the user must type custom values).
    /// </summary>
    public bool IsCustom => WidthMm <= 0 || HeightMm <= 0;

    /// <summary>Initializes a named preset.</summary>
    public ChipSizePreset(string name, double widthMm, double heightMm)
    {
        Name = name;
        WidthMm = widthMm;
        HeightMm = heightMm;
    }
}
