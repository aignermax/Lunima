using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;
using Xunit;

namespace UnitTests.Import.Gds.LayerPicker;

/// <summary>
/// Tests for <see cref="GdsLayerHitTester"/>: smallest containing polygon wins
/// (pad over slab), text anchors win within their tolerance, and empty space
/// misses.
/// </summary>
public class GdsLayerHitTesterTests
{
    private static GdsPolygon Rect(int layer, double x0, double y0, double x1, double y1) =>
        new()
        {
            Layer = layer,
            DataType = 0,
            Points = new[]
            {
                new GdsPoint(x0, y0), new GdsPoint(x1, y0),
                new GdsPoint(x1, y1), new GdsPoint(x0, y1), new GdsPoint(x0, y0),
            },
        };

    /// <summary>A 100×100 slab on layer 1, a 4×4 pad on layer 11 at (10..14), a label on 56 at (50,50).</summary>
    private static GdsLayerGeometryScene BuildScene()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(Rect(1, 0, 0, 100, 100));
        top.Elements.Add(Rect(11, 10, 10, 14, 14));
        top.Elements.Add(new GdsText { Layer = 56, TextType = 0, Text = "opt_in", Position = new GdsPoint(50, 50) });
        library.Cells["TOP"] = top;
        return GdsLayerGeometryScene.Build(library, "TOP");
    }

    [Fact]
    public void TryPick_InsideOverlappingPolygons_SmallestAreaWins()
    {
        GdsLayerHitTester.TryPick(BuildScene(), 12, 12, 0.1, out var hit).ShouldBeTrue();
        hit.ShouldBe(new GdsLayerPair(11, 0));
    }

    [Fact]
    public void TryPick_InsideSlabOnly_ReturnsSlabLayer()
    {
        GdsLayerHitTester.TryPick(BuildScene(), 80, 80, 0.1, out var hit).ShouldBeTrue();
        hit.ShouldBe(new GdsLayerPair(1, 0));
    }

    [Fact]
    public void TryPick_NearTextAnchor_TextWinsOverUnderlyingPolygon()
    {
        // (50,50) lies inside the layer-1 slab, but the label anchor sits there too.
        GdsLayerHitTester.TryPick(BuildScene(), 50.5, 50.5, 2.0, out var hit).ShouldBeTrue();
        hit.ShouldBe(new GdsLayerPair(56, 0));
    }

    [Fact]
    public void TryPick_NearTextAnchor_OutsideTolerance_FallsThroughToPolygon()
    {
        GdsLayerHitTester.TryPick(BuildScene(), 55, 55, 1.0, out var hit).ShouldBeTrue();
        hit.ShouldBe(new GdsLayerPair(1, 0));
    }

    [Fact]
    public void TryPick_EmptySpace_ReturnsFalse()
    {
        GdsLayerHitTester.TryPick(BuildScene(), 500, 500, 1.0, out _).ShouldBeFalse();
    }
}
