namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Picks background-grid spacing for the current zoom: the minor step is the smallest power of
/// ten (µm) that is still at least <see cref="MinMinorSpacingPx"/> apart on screen, the major
/// step is ten minor steps. The minor lines fade in between <see cref="MinMinorSpacingPx"/> and
/// <see cref="FullMinorSpacingPx"/>, so zooming never pops a dense grid in or out, and the grid
/// never degenerates into a wall of lines when zoomed far out.
/// </summary>
public readonly record struct AdaptiveGridSpacing(double MinorWorld, double MajorWorld, double MinorOpacity)
{
    /// <summary>Densest on-screen spacing of minor lines; below this the next decade takes over.</summary>
    public const double MinMinorSpacingPx = 8.0;

    /// <summary>On-screen spacing at which minor lines reach full (quiet) strength.</summary>
    public const double FullMinorSpacingPx = 32.0;

    /// <summary>Number of minor steps per major step.</summary>
    public const int MinorPerMajor = 10;

    /// <summary>Spacing for <paramref name="zoom"/> (screen pixels per µm).</summary>
    public static AdaptiveGridSpacing For(double zoom)
    {
        double safeZoom = zoom <= 0 ? 1.0 : zoom;
        double minorWorld = Math.Pow(10, Math.Ceiling(Math.Log10(MinMinorSpacingPx / safeZoom)));
        double minorPx = minorWorld * safeZoom;
        double opacity = Math.Clamp((minorPx - MinMinorSpacingPx) / (FullMinorSpacingPx - MinMinorSpacingPx), 0, 1);
        return new AdaptiveGridSpacing(minorWorld, minorWorld * MinorPerMajor, opacity);
    }
}
