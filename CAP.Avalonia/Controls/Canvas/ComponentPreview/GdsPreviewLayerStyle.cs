using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Colors and paint order of GDS preview polygons. Waveguide cores carry the routed-waveguide
/// color and are painted last, so a device's geometry visibly continues into its connections;
/// metal reads as copper; every other layer (trenches, labels, pins) stays faint. Polygons that
/// merely fill the cell's bounding box (outline / floorplan layers) are skipped, because the
/// component body already draws the footprint and a full-cell slab would bury the core.
/// Which layers play the core/metal roles comes from <see cref="GdsPreviewLayerRoles"/> —
/// derived from the component's fabrication process, defaulting to the demo PDK's layers.
/// </summary>
internal static class GdsPreviewLayerStyle
{
    /// <summary>A polygon covering at least this share of the cell's bounding box is an outline.</summary>
    private const double FootprintFillShare = 0.95;

    private const byte CoreAlpha = 0xE0;
    private const byte MetalAlpha = 0x48;
    private const byte OtherAlpha = 0x40;

    private static readonly IBrush CoreBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Waveguide, CoreAlpha));
    private static readonly IBrush MetalBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Copper, MetalAlpha));
    private static readonly IBrush OtherBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray300, OtherAlpha));

    /// <summary>Fill brush for a polygon on <paramref name="layer"/> under the default (demo PDK) roles.</summary>
    public static IBrush BrushFor(int layer) => BrushFor(layer, GdsPreviewLayerRoles.Default);

    /// <summary>Fill brush for a polygon on <paramref name="layer"/> under <paramref name="roles"/>.</summary>
    public static IBrush BrushFor(int layer, GdsPreviewLayerRoles roles) =>
        roles.CoreLayers.Contains(layer) ? CoreBrush
        : roles.MetalLayers.Contains(layer) ? MetalBrush
        : OtherBrush;

    /// <summary>
    /// The polygons to paint, in paint order, under the default (demo PDK) roles:
    /// footprint-filling outlines dropped, cores last.
    /// </summary>
    public static IEnumerable<NazcaPreviewPolygon> InDrawOrder(NazcaPreviewResult result) =>
        InDrawOrder(result, GdsPreviewLayerRoles.Default);

    /// <summary>
    /// The polygons to paint, in paint order under <paramref name="roles"/>:
    /// footprint-filling outlines dropped, cores last.
    /// </summary>
    public static IEnumerable<NazcaPreviewPolygon> InDrawOrder(NazcaPreviewResult result, GdsPreviewLayerRoles roles)
    {
        double cellArea = (result.XMax - result.XMin) * (result.YMax - result.YMin);
        var visible = result.Polygons.Where(p => !FillsFootprint(p, cellArea, roles)).ToList();
        return visible.Where(p => !roles.CoreLayers.Contains(p.Layer))
            .Concat(visible.Where(p => roles.CoreLayers.Contains(p.Layer)));
    }

    private static bool FillsFootprint(NazcaPreviewPolygon polygon, double cellArea, GdsPreviewLayerRoles roles) =>
        !roles.CoreLayers.Contains(polygon.Layer) && cellArea > 0 && Area(polygon) >= FootprintFillShare * cellArea;

    private static double Area(NazcaPreviewPolygon polygon)
    {
        var vertices = polygon.Vertices;
        double twiceArea = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            var (x1, y1) = vertices[i];
            var (x2, y2) = vertices[(i + 1) % vertices.Count];
            twiceArea += x1 * y2 - x2 * y1;
        }
        return Math.Abs(twiceArea) / 2;
    }
}
