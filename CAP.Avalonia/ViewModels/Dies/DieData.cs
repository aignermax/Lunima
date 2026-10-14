using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Dies;

/// <summary>
/// Serialisable form of a <see cref="DieDefinition"/> stored on a top-level group of a
/// .lun file. Absent for ordinary groups and for every single-die design, so files that
/// never used dies round-trip byte-identically.
/// </summary>
public class DieData
{
    /// <summary>Outline width in micrometers.</summary>
    public double WidthMicrometers { get; set; }

    /// <summary>Outline height in micrometers.</summary>
    public double HeightMicrometers { get; set; }

    /// <summary>Outline left edge relative to the group origin.</summary>
    public double OutlineOffsetXMicrometers { get; set; }

    /// <summary>Outline top edge relative to the group origin.</summary>
    public double OutlineOffsetYMicrometers { get; set; }

    /// <summary>Edge-zone width in micrometers.</summary>
    public double EdgeZoneMicrometers { get; set; } = DieDefinition.DefaultEdgeZoneMicrometers;

    /// <summary>PDKs enabled on the die; null means all PDKs of the die's process.</summary>
    public List<string>? EnabledPdkNames { get; set; }

    /// <summary>Serialisable form of <paramref name="die"/>; null for a non-die group.</summary>
    public static DieData? From(DieDefinition? die) => die == null ? null : new DieData
    {
        WidthMicrometers = die.WidthMicrometers,
        HeightMicrometers = die.HeightMicrometers,
        OutlineOffsetXMicrometers = die.OutlineOffsetXMicrometers,
        OutlineOffsetYMicrometers = die.OutlineOffsetYMicrometers,
        EdgeZoneMicrometers = die.EdgeZoneMicrometers,
        EnabledPdkNames = die.AllProcessPdksEnabled ? null : die.EnabledPdkNames!.ToList(),
    };

    /// <summary>
    /// The die definition, or null when the stored outline is invalid (a hand-edited or
    /// corrupted file); the caller then falls back to sizing the die to its content.
    /// </summary>
    public DieDefinition? ToDefinition()
    {
        if (!DieDefinition.IsValidDimension(WidthMicrometers) || !DieDefinition.IsValidDimension(HeightMicrometers))
            return null;
        if (!double.IsFinite(OutlineOffsetXMicrometers) || !double.IsFinite(OutlineOffsetYMicrometers))
            return null;
        double edgeZone = double.IsFinite(EdgeZoneMicrometers) && EdgeZoneMicrometers >= 0
            ? EdgeZoneMicrometers
            : DieDefinition.DefaultEdgeZoneMicrometers;
        return new DieDefinition(
            WidthMicrometers, HeightMicrometers,
            OutlineOffsetXMicrometers, OutlineOffsetYMicrometers,
            edgeZone,
            EnabledPdkNames is { Count: > 0 } names ? names.ToList() : null);
    }
}
