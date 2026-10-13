using System;
using System.Collections.Generic;
using CAP_Core.Components.Process;

namespace CAP_Core.Components.Dies;

/// <summary>A named die size offered for a process (MPW cell or common die format).</summary>
/// <param name="Name">Display name, e.g. "2 × 2 mm".</param>
/// <param name="WidthMicrometers">Die width.</param>
/// <param name="HeightMicrometers">Die height.</param>
public sealed record DieSizePreset(string Name, double WidthMicrometers, double HeightMicrometers);

/// <summary>
/// Die size presets per process family. Shuttle runs sell fixed cell formats that differ
/// by platform — small InP gain dies next to larger SiN/SOI passive dies is the typical
/// MPW assembly — so the presets follow the process's core material.
/// </summary>
public static class DieSizePresets
{
    private const double Mm = 1000.0;

    private static readonly IReadOnlyList<DieSizePreset> SiliconNitride = new[]
    {
        new DieSizePreset("2 × 2 mm", 2 * Mm, 2 * Mm),
        new DieSizePreset("5 × 5 mm", 5 * Mm, 5 * Mm),
        new DieSizePreset("10 × 10 mm", 10 * Mm, 10 * Mm),
    };

    private static readonly IReadOnlyList<DieSizePreset> Silicon = new[]
    {
        new DieSizePreset("1 × 1 mm", 1 * Mm, 1 * Mm),
        new DieSizePreset("2 × 2 mm", 2 * Mm, 2 * Mm),
        new DieSizePreset("5 × 5 mm", 5 * Mm, 5 * Mm),
    };

    private static readonly IReadOnlyList<DieSizePreset> IndiumPhosphide = new[]
    {
        new DieSizePreset("1 × 2 mm", 1 * Mm, 2 * Mm),
        new DieSizePreset("2 × 2 mm", 2 * Mm, 2 * Mm),
        new DieSizePreset("4 × 4 mm", 4 * Mm, 4 * Mm),
    };

    private static readonly IReadOnlyList<DieSizePreset> Generic = new[]
    {
        new DieSizePreset("1 × 1 mm", 1 * Mm, 1 * Mm),
        new DieSizePreset("2 × 2 mm", 2 * Mm, 2 * Mm),
        new DieSizePreset("5 × 5 mm", 5 * Mm, 5 * Mm),
    };

    /// <summary>The presets offered for <paramref name="process"/> (generic sizes for unknown or no process).</summary>
    public static IReadOnlyList<DieSizePreset> For(ActiveProcessSelection? process) =>
        ForMaterial(process?.Fingerprint?.CoreMaterial);

    /// <summary>The presets offered for a waveguide core material ("SiN", "Si", "InP", …).</summary>
    public static IReadOnlyList<DieSizePreset> ForMaterial(string? coreMaterial)
    {
        if (string.IsNullOrWhiteSpace(coreMaterial))
            return Generic;
        if (coreMaterial.Contains("InP", StringComparison.OrdinalIgnoreCase)
            || coreMaterial.Contains("InGaAsP", StringComparison.OrdinalIgnoreCase))
            return IndiumPhosphide;
        if (coreMaterial.Contains("SiN", StringComparison.OrdinalIgnoreCase)
            || coreMaterial.Contains("Si3N4", StringComparison.OrdinalIgnoreCase))
            return SiliconNitride;
        if (coreMaterial.Equals("Si", StringComparison.OrdinalIgnoreCase)
            || coreMaterial.Contains("silicon", StringComparison.OrdinalIgnoreCase))
            return Silicon;
        return Generic;
    }

    /// <summary>The default preset for a new die of <paramref name="process"/>: the smallest offered size.</summary>
    public static DieSizePreset DefaultFor(ActiveProcessSelection? process) => For(process)[0];
}
