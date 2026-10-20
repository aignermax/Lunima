using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;

namespace UnitTests.Import.Gds;

public class GdsReviewSceneBuilderTests
{
    [Fact]
    public void HugeArray_IsBoundedButRetainsFullExtentAndEveryLayer()
    {
        var library = Library();
        library.Cells["TOP"].Elements.Add(new GdsReference
        {
            CellName = "leaf",
            Columns = 1_000_000,
            Rows = 1_000_000,
            ColumnSpacingMicrometers = 3,
            RowSpacingMicrometers = 4,
        });
        library.Cells["TOP"].Elements.Add(Rectangle(99));
        var scene = new GdsReviewSceneBuilder(library).Build("TOP");

        scene.IsLimited.ShouldBeTrue();
        scene.Bounds.ShouldBe(new GdsBoundingBox(0, 0, 2_999_999, 3_999_997));
        scene.Layers.Select(x => x.Pair.Layer).ShouldBe([1, 99]);
        scene.Layers.Sum(x => x.Polygons.Count).ShouldBeLessThanOrEqualTo(20_000);
        scene.Layers.Sum(x => x.Polygons.Sum(p => p.Points.Count)).ShouldBeLessThanOrEqualTo(250_000);
        library.Cells["leaf"].Elements.Count.ShouldBe(1);
    }

    [Fact]
    public void RotatedNestedArraysAndPaths_MatchImporterBounds()
    {
        var library = Library();
        library.Cells["leaf"].Elements.Add(new GdsText { Layer = 5, Position = new(-3, 7) });
        library.Cells["leaf"].Elements.Add(new GdsPath
        {
            Layer = 6,
            WidthMicrometers = 2,
            Points = [new(-1, 2), new(7, 4)],
        });
        var middle = new GdsCell { Name = "middle" };
        middle.Elements.Add(new GdsReference
        {
            CellName = "leaf",
            Columns = 3,
            Rows = 2,
            AngleDegrees = 31,
            Reflected = true,
            Magnification = 1.5,
            Offset = new(-10, 3),
            ColumnSpacingMicrometers = 8,
            RowSpacingMicrometers = -5,
        });
        library.Cells.Add(middle.Name, middle);
        library.Cells["TOP"].Elements.Add(new GdsReference
        {
            CellName = "middle",
            AngleDegrees = 17,
            Magnification = 2,
            Offset = new(50, -20),
        });
        var expected = new GdsCellFlattener(library).GetBoundingBox("TOP");
        var builder = new GdsReviewSceneBuilder(library);
        var actual = builder.GetBounds("TOP");
        actual.MinX.ShouldBe(expected.MinX, 1e-9);
        actual.MinY.ShouldBe(expected.MinY, 1e-9);
        actual.MaxX.ShouldBe(expected.MaxX, 1e-9);
        actual.MaxY.ShouldBe(expected.MaxY, 1e-9);
        builder.Build("TOP").IsLimited.ShouldBeFalse();
    }

    [Fact]
    public void TextArray_StopsAtTextBudget()
    {
        var library = Library();
        library.Cells["leaf"].Elements.Clear();
        library.Cells["leaf"].Elements.Add(new GdsText { Layer = 7, Text = "label", Position = new(2, 3) });
        library.Cells["TOP"].Elements.Add(new GdsReference
        {
            CellName = "leaf",
            Columns = 1_000_000,
            ColumnSpacingMicrometers = 2,
        });
        var scene = new GdsReviewSceneBuilder(library).Build("TOP");
        scene.IsLimited.ShouldBeTrue();
        scene.Layers.Sum(x => x.Texts.Count).ShouldBe(5_000);
        scene.Bounds.MaxX.ShouldBe(2_000_000);
    }

    [Fact]
    public void CancelledRequest_ThrowsEvenAfterBoundsCached()
    {
        var builder = new GdsReviewSceneBuilder(Library());
        builder.GetBounds("leaf");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Should.Throw<OperationCanceledException>(() => builder.GetBounds("leaf", cancellation.Token));
        Should.Throw<OperationCanceledException>(() => builder.Build("leaf", cancellation.Token));
    }

    [Fact]
    public void OversizedPolygon_IsNotCopiedIntoPreview()
    {
        var library = Library();
        library.Cells["TOP"].Elements.Add(new GdsPolygon
        {
            Layer = 8,
            Points = Enumerable.Range(0, 300_000).Select(i => new GdsPoint(i, i % 2)).ToArray(),
        });
        var scene = new GdsReviewSceneBuilder(library).Build("TOP");
        scene.IsLimited.ShouldBeTrue();
        scene.Layers.ShouldHaveSingleItem().Polygons.ShouldBeEmpty();
        scene.Bounds.MaxX.ShouldBe(299_999);
    }

    [Fact]
    public void LongPath_StopsBeforeOutliningWholeSource()
    {
        var library = Library();
        library.Cells["TOP"].Elements.Add(new GdsPath
        {
            Layer = 8,
            WidthMicrometers = 1,
            Points = Enumerable.Range(0, 100_000).Select(i => new GdsPoint(i, i % 2)).ToArray(),
        });
        var scene = new GdsReviewSceneBuilder(library).Build("TOP");
        scene.IsLimited.ShouldBeTrue();
        scene.Layers.ShouldHaveSingleItem().Polygons.Count.ShouldBeLessThanOrEqualTo(20_000);
        scene.Bounds.MaxX.ShouldBe(99_999.5);
    }

    [Fact]
    public void CyclicReferences_AreRejected()
    {
        var library = Library();
        library.Cells["TOP"].Elements.Add(new GdsReference { CellName = "TOP" });
        Should.Throw<InvalidDataException>(() => new GdsReviewSceneBuilder(library).Build("TOP"));
    }

    private static GdsLibrary Library()
    {
        var library = new GdsLibrary();
        library.Cells.Add("TOP", new GdsCell { Name = "TOP" });
        var leaf = new GdsCell { Name = "leaf" };
        leaf.Elements.Add(Rectangle(1));
        library.Cells.Add("leaf", leaf);
        return library;
    }

    private static GdsPolygon Rectangle(int layer) => new()
    {
        Layer = layer,
        Points = [new(0, 0), new(2, 0), new(2, 1), new(0, 1), new(0, 0)],
    };
}
