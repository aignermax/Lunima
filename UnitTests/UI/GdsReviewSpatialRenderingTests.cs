using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.GdsImport;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

public class GdsReviewSpatialRenderingTests
{
    [AvaloniaFact]
    public void ZoomedViewportDoesNotSubmitDistantPolygonsFromTheSameCellLayer()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        library.Cells.Add(top.Name, top);
        for (int x = 0; x < 1000; x++)
            top.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(x * 100, 0), new(x * 100 + 10, 0), new(x * 100 + 10, 10), new(x * 100, 10)] });
        var cache = new GdsReviewHierarchyCache(new GdsReviewSceneBuilder(library).Build("TOP"));
        using var frame = new RenderTargetBitmap(new PixelSize(50, 50));
        using (var context = frame.CreateDrawingContext())
            cache.Render(context, Matrix.Identity, new Rect(0, 0, 50, 50), null, null);
        cache.LastRenderStats.Vertices.ShouldBe(4, "only the first square intersects this viewport");
        cache.LastRenderStats.CulledNodes.ShouldBeGreaterThan(0);
    }
    [AvaloniaFact]
    public void AutomaticDetailSimplifiesCurvesThenRestoresEverySourceVertexWhenZoomed()
    {
        var points = Enumerable.Range(0, 4096).Select(i => new GdsPoint(100 + 90 * Math.Cos(i * Math.PI / 2048), 100 + 90 * Math.Sin(i * Math.PI / 2048))).ToArray();
        var original = points.ToArray();
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsPolygon { Layer = 1, Points = points });
        library.Cells.Add(top.Name, top);
        var cache = new GdsReviewHierarchyCache(new GdsReviewSceneBuilder(library).Build("TOP"));
        using var frame = new RenderTargetBitmap(new PixelSize(500, 500));
        Render(Matrix.Identity);
        cache.LastRenderStats.Vertices.ShouldBeLessThan(100);
        Render(Matrix.CreateScale(6, 6));
        cache.LastRenderStats.Vertices.ShouldBe(4096);
        Render(Matrix.CreateScale(.005, .005));
        cache.LastRenderStats.Proxies.ShouldBe(1);
        cache.LastRenderStats.Vertices.ShouldBe(0);
        points.ShouldBe(original);
        void Render(Matrix matrix)
        {
            using var context = frame.CreateDrawingContext();
            cache.Render(context, matrix, new Rect(0, 0, 500, 500), null, null);
        }
    }

    [AvaloniaFact]
    public void IsolatedLayerRendersAndPicksOnlySelectedSourceGeometry()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] });
        top.Elements.Add(new GdsPolygon { Layer = 2, Points = [new(40, 40), new(60, 40), new(60, 60), new(40, 60)] });
        library.Cells.Add(top.Name, top);
        var cache = new GdsReviewHierarchyCache(new GdsReviewSceneBuilder(library).Build("TOP"));
        using var frame = new RenderTargetBitmap(new PixelSize(100, 100));
        using (var context = frame.CreateDrawingContext())
            cache.Render(context, Matrix.Identity, new Rect(0, 0, 100, 100), new(1, 0), null);
        cache.LastRenderStats.Polygons.ShouldBe(1);
        cache.TryPick(50, 50, 1, out var pair, out _, out _, new(1, 0)).ShouldBeTrue();
        pair.ShouldBe(new GdsLayerPair(1, 0));
        cache.TryPick(50, 50, 1, out pair, out _, out _).ShouldBeTrue();
        pair.ShouldBe(new GdsLayerPair(2, 0));
        using (var context = frame.CreateDrawingContext())
            cache.Render(context, Matrix.Identity, new Rect(0, 0, 100, 100), new(1, 0), null, false);
        cache.LastRenderStats.Polygons.ShouldBe(2);
    }
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void DenseOverviewReusesTilesSeparatesSelectionAndRestoresVectorDetail(bool wholeOverview)
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        for (int i = 0; i < 100; i++)
        {
            double x = i % 10 * 10, y = i / 10 * 10;
            top.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(x, y), new(x + 8, y), new(x + 8, y + 8), new(x, y + 8)] });
        }
        library.Cells.Add(top.Name, top);
        using var cache = new GdsReviewHierarchyCache(new GdsReviewSceneBuilder(library).Build("TOP"));
        using var frame = new RenderTargetBitmap(new PixelSize(120, 120));
        Render();
        cache.LastRenderStats.Vertices.ShouldBe(400);
        Render();
        cache.LastRenderStats.Vertices.ShouldBe(0, "a stationary overview reuses its raster tile");
        Render(matrix: Matrix.CreateTranslation(5, 5));
        cache.LastRenderStats.Vertices.ShouldBe(0, "panning reuses cached source-space tiles");
        Render(new(1, 0));
        cache.LastRenderStats.Vertices.ShouldBe(400, "selected outlines require a distinct tile");
        Render(new(1, 0));
        cache.LastRenderStats.Vertices.ShouldBe(0);
        Render(detail: 2);
        cache.LastRenderStats.Vertices.ShouldBe(400, "fine detail bypasses overview rasterization");
        cache.Dispose();
        Render();
        cache.LastRenderStats.Vertices.ShouldBe(400, "reattaching after disposal rebuilds live tiles");
        void Render(GdsLayerPair? selected = null, int detail = 0, Matrix? matrix = null)
        {
            using var context = frame.CreateDrawingContext();
            cache.Render(context, matrix ?? Matrix.Identity, new Rect(0, 0, 120, 120), selected, null, true, detail, wholeOverview);
        }
    }

    [AvaloniaFact]
    public void RasterPixelBudgetEvictsOldTilesInsteadOfRetainingEveryVisitedBlock()
    {
        using var raster = new GdsReviewRasterCache();
        var geometry = new GdsReviewGeometryLru();
        var first = Node();
        raster.Get(first, 1, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red), false, 0, geometry).NewVertices.ShouldBe(3);
        for (int i = 0; i < 40; i++) raster.Get(Node(), 1, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red), false, 0, geometry);
        raster.Get(first, 1, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red), false, 0, geometry).NewVertices.ShouldBe(3,
            "more than forty million pixels must evict the original tile");
        static GdsReviewSpatialGeometry.Node Node() => new()
        {
            Bounds = new Rect(0, 0, 1000, 1000),
            Polygon = new GdsPolygon { Layer = 1, Points = [new(0, 0), new(1000, 0), new(0, 1000)] }
        };
    }
    [AvaloniaFact]
    public void HiddenDenseArrayCannotConsumeIsolatedLayerPickingBudget()
    {
        var library = new GdsLibrary();
        foreach (int layer in new[] { 1, 2 })
        {
            var leaf = new GdsCell { Name = $"leaf{layer}" };
            leaf.Elements.Add(new GdsPolygon { Layer = layer, Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] });
            library.Cells.Add(leaf.Name, leaf);
        }
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsReference { CellName = "leaf1", Columns = 100_000, ColumnSpacingMicrometers = .00001 });
        top.Elements.Add(new GdsReference { CellName = "leaf2" });
        library.Cells.Add(top.Name, top);
        using var cache = new GdsReviewHierarchyCache(new GdsReviewSceneBuilder(library).Build("TOP"));
        cache.TryPick(5, 5, 1, out var pair, out var cell, out _, new(2, 0)).ShouldBeTrue();
        pair.ShouldBe(new GdsLayerPair(2, 0));
        cell.ShouldBe("leaf2");
    }
}
