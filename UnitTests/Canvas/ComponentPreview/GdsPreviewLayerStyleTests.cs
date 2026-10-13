using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>
/// Paint order and coloring of GDS preview polygons: the waveguide core must stay visible on
/// the canvas instead of disappearing under cladding, heater and outline layers.
/// </summary>
public class GdsPreviewLayerStyleTests
{
    private const int OutlineLayer = 20;
    private const int HeaterLayer = 10;
    private const int ShallowCoreLayer = 3;

    [Fact]
    public void InDrawOrder_dropsPolygonsThatFillTheWholeCell()
    {
        var result = Cell(Rect(OutlineLayer, 0, 0, 100, 10), Rect(ShallowCoreLayer, 0, 4, 100, 6));

        var drawn = GdsPreviewLayerStyle.InDrawOrder(result).ToList();

        drawn.ShouldNotContain(p => p.Layer == OutlineLayer, "a full-cell slab would bury the core");
        drawn.ShouldContain(p => p.Layer == ShallowCoreLayer);
    }

    [Fact]
    public void InDrawOrder_paintsCoreLayersLast()
    {
        var result = Cell(Rect(1, 0, 4, 100, 6), Rect(HeaterLayer, 0, 2, 100, 8), Rect(ShallowCoreLayer, 0, 4, 50, 6));

        var layers = GdsPreviewLayerStyle.InDrawOrder(result).Select(p => p.Layer).ToList();

        layers.ShouldBe(new[] { HeaterLayer, 1, ShallowCoreLayer });
    }

    [Fact]
    public void InDrawOrder_keepsAFullCellCorePolygon()
    {
        var result = Cell(Rect(1, 0, 0, 100, 10));

        GdsPreviewLayerStyle.InDrawOrder(result).ShouldHaveSingleItem();
    }

    [Fact]
    public void BrushFor_givesCoreLayersTheSameBrushAndOtherLayersADifferentOne()
    {
        GdsPreviewLayerStyle.BrushFor(1).ShouldBeSameAs(GdsPreviewLayerStyle.BrushFor(ShallowCoreLayer));
        GdsPreviewLayerStyle.BrushFor(HeaterLayer).ShouldNotBeSameAs(GdsPreviewLayerStyle.BrushFor(1));
        GdsPreviewLayerStyle.BrushFor(1003).ShouldNotBeSameAs(GdsPreviewLayerStyle.BrushFor(1));
    }

    private static NazcaPreviewResult Cell(params NazcaPreviewPolygon[] polygons) => new()
    {
        Success = true,
        XMin = 0,
        YMin = 0,
        XMax = 100,
        YMax = 10,
        Polygons = polygons,
    };

    private static NazcaPreviewPolygon Rect(int layer, double x0, double y0, double x1, double y1) => new()
    {
        Layer = layer,
        Vertices = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) },
    };
}
