using Avalonia.Media;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Draws <see cref="AsDrawnGeometry"/> — the exact polygons an imported waveguide or
/// other top-cell shape was drawn with — filled per layer like imported component
/// outlines. The polygons are stored in absolute canvas coordinates, so they go
/// through the outline renderer with an identity pose; its geometry cache is keyed
/// by the polygon list, which an <see cref="AsDrawnGeometry"/> instance keeps for life.
/// </summary>
internal static class AsDrawnGeometryRenderer
{
    private static readonly ComponentOutlineRenderer OutlineRenderer = new();

    /// <summary>Draws the polygons of <paramref name="geometry"/>.</summary>
    /// <param name="context">Drawing context in world (µm) space.</param>
    /// <param name="geometry">Polygons to draw, absolute canvas coordinates.</param>
    /// <param name="zoom">Canvas zoom for the per-polygon level-of-detail cull.</param>
    /// <param name="layerVisibility">Per-design layer view filter; null shows every layer.</param>
    /// <param name="isDimmed">True draws at half opacity (e.g. while power flow is shown).</param>
    public static void Draw(
        DrawingContext context,
        AsDrawnGeometry geometry,
        double zoom,
        GdsLayerVisibilityState? layerVisibility,
        bool isDimmed = false)
    {
        // Identity pose: an unrotated frame anchored at the world origin maps the
        // outline points 1:1 onto world coordinates (the size only matters for rotation).
        OutlineRenderer.Draw(context, 0, 0, 1, 1, 0,
            geometry.Polygons, isDimmed, zoom, layerVisibility: layerVisibility);
    }
}
