using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

public sealed class OutlineRasterCacheBoundsTests
{
    private static readonly OutlinePoint[][] Shape = [[new(1, 1), new(5, 1), new(5, 5), new(1, 5)]];

    [Fact]
    public void CardinalityEviction_IsBoundedAndAnEvictedShapeIsRasterizedAgain()
    {
        var cache = new OutlineRasterCache(maximumEntries: 2, maximumBytes: 100_000);
        var grid = new PathfindingGrid(0, 0, 20, 20);
        var first = Guid.NewGuid();
        cache.GetOrBuild(first, Shape, grid, 1);
        cache.Count.ShouldBe(1);
        cache.GetOrBuild(Guid.NewGuid(), Shape, grid, 1);
        cache.GetOrBuild(Guid.NewGuid(), Shape, grid, 1);
        cache.Count.ShouldBe(2);
        var before = cache.RasterizationCount;
        cache.GetOrBuild(first, Shape, grid, 1);
        cache.RasterizationCount.ShouldBe(before + 2);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void WeightLimit_IncludesOwnedPointsAndRowsAndDoesNotRetainOversizedEntries()
    {
        var cache = new OutlineRasterCache(maximumEntries: 100, maximumBytes: 8192);
        var grid = new PathfindingGrid(0, 0, 20, 20);
        cache.GetOrBuild(Guid.NewGuid(), Shape, grid, 1);
        cache.RetainedBytes.ShouldBeGreaterThan(0);
        for (var i = 0; i < 50; i++) cache.GetOrBuild(Guid.NewGuid(), Shape, grid, 1);
        cache.RetainedBytes.ShouldBeLessThanOrEqualTo(8192);
        cache.Count.ShouldBeLessThan(50);
        var oversized = new[] { Enumerable.Range(0, 2000).Select(i => new OutlinePoint(i % 2 + 1, i % 3 + 1)).ToArray() };
        var id = Guid.NewGuid();
        cache.GetOrBuild(id, oversized, grid, 1);
        var before = cache.RasterizationCount;
        cache.GetOrBuild(id, oversized, grid, 1);
        cache.RasterizationCount.ShouldBe(before + 2, "a single oversized entry must not bypass the retained-memory limit");
        cache.RetainedBytes.ShouldBeLessThanOrEqualTo(8192);
    }

    [Fact]
    public void EqualValuesReuseButAnyPointChangeOrRemovalRequiresNewRasterization()
    {
        var cache = new OutlineRasterCache();
        var grid = new PathfindingGrid(0, 0, 20, 20);
        var id = Guid.NewGuid();
        cache.GetOrBuild(id, Shape, grid, 1);
        var copy = Shape.Select(points => points.ToArray()).ToArray();
        cache.GetOrBuild(id, copy, grid, 1);
        cache.RasterizationCount.ShouldBe(2, "value equality is independent of array identity");
        copy[0][1] = new(9, 2);
        cache.GetOrBuild(id, copy, grid, 1);
        cache.RasterizationCount.ShouldBe(4);
        cache.Remove(id);
        cache.Count.ShouldBe(0);
        cache.GetOrBuild(id, copy, grid, 1);
        cache.RasterizationCount.ShouldBe(6);
    }
    [Fact]
    public void MergedRaster_RetainsUnionStorageRatherThanEveryOverlappingInputSpan()
    {
        var grid = new PathfindingGrid(0, 0, 20, 20);
        var single = OutlineRasterRows.Build(Shape, grid, 1);
        var repeated = OutlineRasterRows.Build(Enumerable.Repeat(Shape[0], 512), grid, 1);
        repeated.Cells().ToHashSet().SetEquals(single.Cells()).ShouldBeTrue();
        repeated.RetainedBytes.ShouldBeLessThanOrEqualTo(single.RetainedBytes * 2,
            "merged duplicate polygons must not retain hundreds of obsolete span slots per final row");
    }

    [Fact]
    public void IdenticalFlattenedVertices_WithDifferentPolygonBoundariesCannotHit()
    {
        var cache = new OutlineRasterCache();
        var grid = new PathfindingGrid(0, 0, 20, 20);
        var id = Guid.NewGuid();
        OutlinePoint[] vertices = [new(1, 1), new(9, 1), new(9, 9), new(1, 9),
            new(3, 3), new(5, 3), new(5, 5), new(3, 5)];
        cache.GetOrBuild(id, [vertices[..4], vertices[4..]], grid, 1);
        OutlinePoint[][] repartitioned = [vertices[..3], vertices[3..]];
        var changed = cache.GetOrBuild(id, repartitioned, grid, 1);
        cache.RasterizationCount.ShouldBe(4, "all vertices match, but their polygon boundaries do not");
        var cold = OutlineRasterRows.Build(repartitioned, grid, 1);
        changed.Padded.Cells().ToHashSet().SetEquals(cold.Cells()).ShouldBeTrue();
        cache.GetOrBuild(id, repartitioned.Select(points => points.ToArray()).ToArray(), grid, 1);
        cache.RasterizationCount.ShouldBe(4);
    }

}
