using System;
using System.Collections.Generic;

namespace CAP_Core.Components.Dies;

/// <summary>
/// Physical definition of a die (chiplet) on a multi-die assembly: a fixed outline that is
/// independent of the content's bounding box, the edge zone in which edge couplers snap to
/// the die edge, and the subset of the die's process PDKs whose parts may be placed on it.
/// The die's fabrication process itself is the owning group's
/// <see cref="Core.ComponentGroup.ProcessBinding"/> — one source of truth for process scope.
/// Immutable: edits replace the whole record so undo can swap old and new values.
/// </summary>
/// <param name="WidthMicrometers">Outline width in micrometers.</param>
/// <param name="HeightMicrometers">Outline height in micrometers.</param>
/// <param name="OutlineOffsetXMicrometers">Outline left edge relative to the group origin.</param>
/// <param name="OutlineOffsetYMicrometers">Outline top edge relative to the group origin.</param>
/// <param name="EdgeZoneMicrometers">Width of the band along the outline in which edge couplers snap to the edge.</param>
/// <param name="EnabledPdkNames">PDKs enabled on this die; null or empty means every PDK of the die's process.</param>
public sealed record DieDefinition(
    double WidthMicrometers,
    double HeightMicrometers,
    double OutlineOffsetXMicrometers = 0,
    double OutlineOffsetYMicrometers = 0,
    double EdgeZoneMicrometers = DieDefinition.DefaultEdgeZoneMicrometers,
    IReadOnlyList<string>? EnabledPdkNames = null)
{
    /// <summary>Default edge-zone width: wide enough to grab a coupler, narrow enough to stay out of the way.</summary>
    public const double DefaultEdgeZoneMicrometers = 50.0;

    /// <summary>Smallest die edge accepted.</summary>
    public const double MinDimensionMicrometers = 50.0;

    /// <summary>Largest die edge accepted (100 mm, the same upper bound the chip size uses).</summary>
    public const double MaxDimensionMicrometers = 100_000.0;

    /// <summary>True when every PDK of the process is enabled (no explicit subset).</summary>
    public bool AllProcessPdksEnabled => EnabledPdkNames is not { Count: > 0 };

    /// <summary>
    /// Returns this definition when the outline is a finite size within
    /// [<see cref="MinDimensionMicrometers"/>, <see cref="MaxDimensionMicrometers"/>] and the
    /// edge zone is non-negative; throws otherwise.
    /// </summary>
    public DieDefinition Validated()
    {
        ValidateDimension(WidthMicrometers, nameof(WidthMicrometers));
        ValidateDimension(HeightMicrometers, nameof(HeightMicrometers));
        if (!double.IsFinite(EdgeZoneMicrometers) || EdgeZoneMicrometers < 0)
            throw new ArgumentOutOfRangeException(nameof(EdgeZoneMicrometers), "Edge zone must be finite and non-negative.");
        return this;
    }

    /// <summary>True when <paramref name="value"/> is an acceptable die edge length.</summary>
    public static bool IsValidDimension(double value) =>
        double.IsFinite(value) && value >= MinDimensionMicrometers && value <= MaxDimensionMicrometers;

    private static void ValidateDimension(double value, string name)
    {
        if (!IsValidDimension(value))
            throw new ArgumentOutOfRangeException(name,
                $"Die dimension must be within {MinDimensionMicrometers}–{MaxDimensionMicrometers} µm (got {value}).");
    }
}
