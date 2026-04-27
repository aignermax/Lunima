namespace CAP_Core.Grid;

/// <summary>
/// Represents the physical dimensions of a PIC chip and provides helpers for preset selection
/// and tile-grid dimension computation based on the standard grid pitch.
/// </summary>
public sealed class ChipSizeConfiguration
{
    /// <summary>Smallest acceptable chip dimension — one tile wide. Anything smaller produces a 0-tile grid.</summary>
    public static readonly double MinDimensionMicrometers = PhotonicConstants.GridSizeMicrometers;

    /// <summary>Largest acceptable chip dimension (100 mm). Beyond this, A* grid allocation explodes.</summary>
    public const double MaxDimensionMicrometers = 100_000.0;

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
    /// <param name="widthMicrometers">Chip width in micrometers; must be in [MinDimensionMicrometers, MaxDimensionMicrometers], finite.</param>
    /// <param name="heightMicrometers">Chip height in micrometers; same constraints.</param>
    /// <exception cref="ArgumentOutOfRangeException">If a dimension is NaN, infinite, or outside the supported range.</exception>
    public ChipSizeConfiguration(double widthMicrometers, double heightMicrometers)
    {
        ValidateDimension(widthMicrometers, nameof(widthMicrometers));
        ValidateDimension(heightMicrometers, nameof(heightMicrometers));

        WidthMicrometers = widthMicrometers;
        HeightMicrometers = heightMicrometers;
    }

    private static void ValidateDimension(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(paramName, $"Chip dimension must be finite (got {value}).");
        if (value < MinDimensionMicrometers)
            throw new ArgumentOutOfRangeException(paramName,
                $"Chip dimension must be at least one tile ({MinDimensionMicrometers} µm), got {value} µm.");
        if (value > MaxDimensionMicrometers)
            throw new ArgumentOutOfRangeException(paramName,
                $"Chip dimension must not exceed {MaxDimensionMicrometers} µm (100 mm), got {value} µm.");
    }

    /// <summary>
    /// Creates a <see cref="ChipSizeConfiguration"/> from millimeter values.
    /// </summary>
    /// <param name="widthMm">Width in millimeters.</param>
    /// <param name="heightMm">Height in millimeters.</param>
    public static ChipSizeConfiguration FromMillimeters(double widthMm, double heightMm)
        => new ChipSizeConfiguration(widthMm * 1000.0, heightMm * 1000.0);

    /// <summary>The "Custom" preset entry shown in the dropdown — signals user-typed values.</summary>
    public static ChipSizePreset Custom { get; } = new ChipSizePreset("Custom", 0.0, 0.0, isCustom: true);

    /// <summary>
    /// Common MPW / shuttle-mask presets (width × height in mm). For the special "Custom"
    /// option that signals "user wants to type their own values", use <see cref="Custom"/>.
    /// </summary>
    public static IReadOnlyList<ChipSizePreset> Presets { get; } = new ChipSizePreset[]
    {
        new ChipSizePreset("2 × 2 mm",   2.0,  2.0,  isCustom: false),
        new ChipSizePreset("5 × 5 mm",   5.0,  5.0,  isCustom: false),
        new ChipSizePreset("10 × 10 mm", 10.0, 10.0, isCustom: false),
        new ChipSizePreset("20 × 30 mm", 20.0, 30.0, isCustom: false),
        Custom,
    };
}

/// <summary>
/// Named preset for a common MPW / shuttle-mask chip size.
/// </summary>
public sealed class ChipSizePreset
{
    /// <summary>Human-readable display name shown in the UI.</summary>
    public string Name { get; }

    /// <summary>Width in millimeters. Meaningless when <see cref="IsCustom"/>.</summary>
    public double WidthMm { get; }

    /// <summary>Height in millimeters. Meaningless when <see cref="IsCustom"/>.</summary>
    public double HeightMm { get; }

    /// <summary>True for the "Custom" entry (user types their own dimensions); false for named presets.</summary>
    public bool IsCustom { get; }

    /// <summary>Initializes a named preset. Use <see cref="ChipSizeConfiguration.Custom"/> for the user-typed entry.</summary>
    public ChipSizePreset(string name, double widthMm, double heightMm, bool isCustom)
    {
        Name = name;
        WidthMm = widthMm;
        HeightMm = heightMm;
        IsCustom = isCustom;
    }
}
