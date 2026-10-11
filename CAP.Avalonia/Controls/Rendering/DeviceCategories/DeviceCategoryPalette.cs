using Avalonia.Media;

namespace CAP.Avalonia.Controls.Rendering.DeviceCategories;

/// <summary>
/// Category accent colors for component bodies and the brushes derived from them. The hues
/// are decoration-level (a thin top edge and a faint body tint), deliberately distinct from the
/// state colors in <see cref="CanvasPalette"/> so selection, danger and warning keep their meaning.
/// Waveguide-class devices share the routed-waveguide teal, so a straight reads as part of the route.
/// </summary>
public static class DeviceCategoryPalette
{
    /// <summary>Strength of the category tint mixed into the body fill.</summary>
    private const double BodyTint = 0.08;

    /// <summary>Strength of the category tint for the far-zoom block, where the color carries the layout.</summary>
    private const double LodTint = 0.45;

    private static readonly CategoryBrushes[] ByCategory = Enum.GetValues<DeviceCategory>()
        .Select(category => new CategoryBrushes(AccentColor(category)))
        .ToArray();

    /// <summary>The accent hue of a category.</summary>
    public static Color AccentColor(DeviceCategory category) => category switch
    {
        DeviceCategory.Waveguide => CanvasPalette.Waveguide,
        DeviceCategory.Coupler => Color.Parse("#9D8CFF"),
        DeviceCategory.Filter => Color.Parse("#E384C4"),
        DeviceCategory.Active => Color.Parse("#F08A5D"),
        DeviceCategory.InputOutput => CanvasPalette.Info,
        DeviceCategory.Detector => Color.Parse("#A3D65C"),
        DeviceCategory.Crossing => CanvasPalette.Gray300,
        DeviceCategory.Electrical => CanvasPalette.Copper,
        _ => CanvasPalette.Gray500,
    };

    /// <summary>Brushes for <paramref name="category"/>.</summary>
    public static CategoryBrushes For(DeviceCategory category) => ByCategory[(int)category];

    /// <summary>Precomputed brushes of one category.</summary>
    public sealed class CategoryBrushes
    {
        internal CategoryBrushes(Color accent)
        {
            Accent = CanvasPalette.Brush(accent);
            Body = CanvasPalette.Brush(Mix(CanvasPalette.ComponentBody, accent, BodyTint));
            BodyUnderPreview = CanvasPalette.Brush(CanvasPalette.WithAlpha(
                Mix(CanvasPalette.ComponentBody, accent, BodyTint), CanvasPalette.ComponentBodyUnderPreview.A));
            Lod = CanvasPalette.Brush(Mix(CanvasPalette.ComponentBody, accent, LodTint));
        }

        /// <summary>Full-strength accent (top edge).</summary>
        public IBrush Accent { get; }

        /// <summary>Body fill without GDS geometry.</summary>
        public IBrush Body { get; }

        /// <summary>Translucent body fill under a GDS preview.</summary>
        public IBrush BodyUnderPreview { get; }

        /// <summary>Far-zoom block fill.</summary>
        public IBrush Lod { get; }
    }

    private static Color Mix(Color baseColor, Color tint, double amount) => Color.FromRgb(
        (byte)(baseColor.R + (tint.R - baseColor.R) * amount),
        (byte)(baseColor.G + (tint.G - baseColor.G) * amount),
        (byte)(baseColor.B + (tint.B - baseColor.B) * amount));
}
