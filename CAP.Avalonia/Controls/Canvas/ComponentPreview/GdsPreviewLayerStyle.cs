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
/// </summary>
internal static class GdsPreviewLayerStyle
{
    /// <summary>Core layers of the bundled PDKs: 1 = deep-etch / Si core, 3 = shallow-etch core.</summary>
    private static readonly HashSet<int> CoreLayers = [1, 3];

    /// <summary>Metal / heater layer of the bundled demo PDK.</summary>
    private const int MetalLayer = 10;

    /// <summary>A polygon covering at least this share of the cell's bounding box is an outline.</summary>
    private const double FootprintFillShare = 0.95;

    private const byte CoreAlpha = 0xE0;
    private const byte MetalAlpha = 0x48;
    private const byte OtherAlpha = 0x40;

    private static readonly IBrush CoreBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Waveguide, CoreAlpha));
    private static readonly IBrush MetalBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Copper, MetalAlpha));
    private static readonly IBrush OtherBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray300, OtherAlpha));

    /// <summary>Fill brush for a polygon on <paramref name="layer"/>.</summary>
    public static IBrush BrushFor(int layer) =>
        CoreLayers.Contains(layer) ? CoreBrush
        : layer == MetalLayer ? MetalBrush
        : OtherBrush;

    /// <summary>
    /// The polygons to paint, in paint order: footprint-filling outlines dropped, cores last.
    /// </summary>
    public static IEnumerable<NazcaPreviewPolygon> InDrawOrder(NazcaPreviewResult result)
    {
        double cellArea = (result.XMax - result.XMin) * (result.YMax - result.YMin);
        var visible = result.Polygons.Where(p => !FillsFootprint(p, cellArea)).ToList();
        return visible.Where(p => !CoreLayers.Contains(p.Layer))
            .Concat(visible.Where(p => CoreLayers.Contains(p.Layer)));
    }

    private static bool FillsFootprint(NazcaPreviewPolygon polygon, double cellArea) =>
        !CoreLayers.Contains(polygon.Layer) && cellArea > 0 && Area(polygon) >= FootprintFillShare * cellArea;

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
