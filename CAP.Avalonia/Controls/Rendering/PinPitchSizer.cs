using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Shrinks pin glyph sizes when a component's ports sit closer together than the default
/// glyph diameter — e.g. a 2x2 MMI whose two ports per side are only 8 µm apart would
/// otherwise render as one merged "8"-shaped blob whose ports look unselectable
/// individually. The shrink factor is uniform per component so all of its pins keep a
/// consistent size, and it only ever shrinks (widely spaced pins render exactly as before).
/// </summary>
public static class PinPitchSizer
{
    /// <summary>Largest glyph radius allowed, as a fraction of the smallest port-to-port
    /// distance — 0.45 leaves a visible gap of 10% of the pitch between two markers.</summary>
    public const double MaxRadiusToPitchFraction = 0.45;

    /// <summary>Lower bound for the shrink factor so degenerate layouts (two pins at the
    /// exact same position) still render a visible marker instead of a zero-size glyph.</summary>
    public const double MinScale = 0.25;

    /// <summary>
    /// Returns the multiplier (≤ 1) to apply to a base pin glyph radius for the given pin
    /// set: 1 when pins are far enough apart, otherwise
    /// <c>minPitch * <see cref="MaxRadiusToPitchFraction"/> / baseRadius</c>, floored at
    /// <see cref="MinScale"/>.
    /// </summary>
    /// <param name="pins">The component's physical pins (relative offsets are sufficient —
    /// only pairwise distances matter).</param>
    /// <param name="baseRadius">The unscaled glyph radius in world units (µm).</param>
    public static double ComputeScale(IReadOnlyList<PhysicalPin> pins, double baseRadius)
    {
        if (pins.Count < 2 || baseRadius <= 0)
            return 1.0;

        double minDistance = ComputeMinPairDistance(pins);
        double scale = minDistance * MaxRadiusToPitchFraction / baseRadius;
        return Math.Clamp(scale, MinScale, 1.0);
    }

    private static double ComputeMinPairDistance(IReadOnlyList<PhysicalPin> pins)
    {
        double minSquared = double.MaxValue;
        for (int i = 0; i < pins.Count; i++)
        {
            for (int j = i + 1; j < pins.Count; j++)
            {
                double dx = pins[i].OffsetXMicrometers - pins[j].OffsetXMicrometers;
                double dy = pins[i].OffsetYMicrometers - pins[j].OffsetYMicrometers;
                double squared = dx * dx + dy * dy;
                if (squared < minSquared)
                    minSquared = squared;
            }
        }
        return Math.Sqrt(minSquared);
    }
}
