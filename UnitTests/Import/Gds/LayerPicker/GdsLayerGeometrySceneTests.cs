using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;
using Xunit;

namespace UnitTests.Import.Gds.LayerPicker;

/// <summary>
/// Tests for <see cref="GdsLayerGeometryScene"/>: flattened geometry groups per
/// (layer, datatype) pair, paths flow in as outline polygons, texts group by
/// texttype, and the bounds cover polygons and text anchors.
/// </summary>
public class GdsLayerGeometrySceneTests
{
    private static GdsPolygon Rect(int layer, int datatype, double x0, double y0, double x1, double y1) =>
        new()
        {
            Layer = layer,
            DataType = datatype,
            Points = new[]
            {
                new GdsPoint(x0, y0), new GdsPoint(x1, y0),
                new GdsPoint(x1, y1), new GdsPoint(x0, y1), new GdsPoint(x0, y0),
            },
        };

    /// <summary>TOP holds a slab, references CHILD (offset 20 µm) with a pad, a path and a label.</summary>
    private static GdsLibrary BuildLibrary()
    {
        var library = new GdsLibrary { DatabaseUnitInMeters = 1e-9, UserUnitsPerDatabaseUnit = 1e-3 };

        var child = new GdsCell { Name = "CHILD" };
        child.Elements.Add(Rect(11, 0, 0, 0, 2, 2));
        child.Elements.Add(new GdsText { Layer = 56, TextType = 0, Text = "opt_in", Position = new GdsPoint(1, 1) });
        library.Cells["CHILD"] = child;

        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(Rect(1, 0, 0, 0, 10, 10));
        top.Elements.Add(new GdsPath
        {
            Layer = 37, DataType = 0, WidthMicrometers = 0.5,
            Points = new[] { new GdsPoint(0, 20), new GdsPoint(50, 20) },
        });
        top.Elements.Add(new GdsReference { CellName = "CHILD", Offset = new GdsPoint(20, 0) });
        library.Cells["TOP"] = top;
        return library;
    }

    [Fact]
    public void Build_GroupsGeometryByLayerPair_SortedByLayer()
    {
        var scene = GdsLayerGeometryScene.Build(BuildLibrary(), "TOP");

        scene.CellName.ShouldBe("TOP");
        scene.Layers.Select(l => l.Pair).ShouldBe(new[]
        {
            new GdsLayerPair(1, 0),
            new GdsLayerPair(11, 0),
            new GdsLayerPair(37, 0),
            new GdsLayerPair(56, 0),
        });
    }

    [Fact]
    public void Build_ExpandsPathsToPolygons()
    {
        var scene = GdsLayerGeometryScene.Build(BuildLibrary(), "TOP");

        var pathLayer = scene.Layers.Single(l => l.Pair == new GdsLayerPair(37, 0));
        pathLayer.Polygons.ShouldNotBeEmpty("the flattener expands PATH elements to outline polygons");
        pathLayer.Texts.ShouldBeEmpty();
    }

    [Fact]
    public void Build_TransformsReferencedGeometryIntoTopCellSpace()
    {
        var scene = GdsLayerGeometryScene.Build(BuildLibrary(), "TOP");

        var pad = scene.Layers.Single(l => l.Pair == new GdsLayerPair(11, 0)).Polygons.Single();
        pad.Points.Min(p => p.X).ShouldBe(20, 1e-9);
        pad.Points.Max(p => p.X).ShouldBe(22, 1e-9);

        var label = scene.Layers.Single(l => l.Pair == new GdsLayerPair(56, 0)).Texts.Single();
        label.Position.X.ShouldBe(21, 1e-9);
        label.Position.Y.ShouldBe(1, 1e-9);
    }

    [Fact]
    public void Build_BoundsCoverPolygonsAndTextAnchors()
    {
        var scene = GdsLayerGeometryScene.Build(BuildLibrary(), "TOP");

        scene.Bounds.MinX.ShouldBe(0, 1e-9);
        scene.Bounds.MaxX.ShouldBe(50, 1e-9);
        scene.Bounds.MinY.ShouldBeLessThanOrEqualTo(0);
        scene.Bounds.MaxY.ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void Build_EmptyCell_YieldsNoLayers()
    {
        var library = new GdsLibrary();
        library.Cells["EMPTY"] = new GdsCell { Name = "EMPTY" };

        var scene = GdsLayerGeometryScene.Build(library, "EMPTY");

        scene.Layers.ShouldBeEmpty();
        scene.Bounds.ShouldBe(GdsBoundingBox.Empty);
    }
}
