using CAP_Core.Components.Core;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

public sealed class OutlineRasterSnapshotReuseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedPrivateGraph_ReusesRasterValuesAndRebindsAllGridOwnership(bool preserveCells)
    {
        var fixture = new OutlineRasterReuseFixture();
        fixture.Router.PathfindingGrid!.OutlineRasterizationCount.ShouldBe(2);
        var graph = fixture.Capture(preserveCells).CreatePrivateGraph();
        var grid = graph.Router.PathfindingGrid!;
        grid.OutlineRasterizationCount.ShouldBe(0, "detached immutable rows must survive the routing snapshot");
        OutlineRasterReuseFixture.AssertParity(grid, OutlineRasterReuseFixture.Cold(graph), graph.Components);
        graph.Components[0].ShouldNotBeSameAs(fixture.Component);
        var sourceCells = fixture.Router.PathfindingGrid.CaptureCellStates();
        grid.RemoveComponentObstacle(graph.Components[0]);
        grid.GetBlockedCellCount().ShouldBe(0);
        fixture.Router.PathfindingGrid.CaptureCellStates().ShouldBe(sourceCells);
    }

    [Fact]
    public void CapturedRaster_RemainsDetachedAfterCallerPointsChangeAndSourceGridIsRebuilt()
    {
        var fixture = new OutlineRasterReuseFixture();
        var captured = fixture.Capture();
        var expected = fixture.Router.PathfindingGrid!.CaptureCellStates();
        fixture.Points[2] = new(40, 30);
        fixture.Component.PhysicalX += 12;
        fixture.Router.PathfindingGrid.RebuildFromComponents([fixture.Component]);
        var first = captured.CreatePrivateGraph();
        first.Router.PathfindingGrid!.OutlineRasterizationCount.ShouldBe(0);
        first.Router.PathfindingGrid.CaptureCellStates().ShouldBe(expected);
        first.Router.PathfindingGrid.RemoveComponentObstacle(first.Components[0]);
        var second = captured.CreatePrivateGraph();
        second.Router.PathfindingGrid!.CaptureCellStates().ShouldBe(expected);
    }

    [Fact]
    public void PinEdits_RebuildCorridorsAndZonesWithoutRepeatingUnchangedOutlineRasterization()
    {
        var fixture = new OutlineRasterReuseFixture();
        var before = fixture.Router.PathfindingGrid!.GetPinCorridorCells([fixture.Component.PhysicalPins[0]]);
        fixture.Component.PhysicalPins[0].OffsetYMicrometers += 14;
        var graph = fixture.Capture().CreatePrivateGraph();
        var grid = graph.Router.PathfindingGrid!;
        grid.OutlineRasterizationCount.ShouldBe(0);
        before.SetEquals(grid.GetPinCorridorCells([graph.Components[0].PhysicalPins[0]])).ShouldBeFalse();
        OutlineRasterReuseFixture.AssertParity(grid, OutlineRasterReuseFixture.Cold(graph), graph.Components);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("rotation")]
    [InlineData("mirror")]
    [InlineData("dimensions")]
    [InlineData("original-dimensions")]
    [InlineData("point-value")]
    [InlineData("polygon-added")]
    [InlineData("padding")]
    [InlineData("grid")]
    public void ChangedGeometryOrGrid_CannotReuseStaleRaster(string change)
    {
        var fixture = new OutlineRasterReuseFixture();
        switch (change)
        {
            case "position": fixture.Component.PhysicalX += 11; break;
            case "rotation": fixture.Component.RotationDegrees = 37; break;
            case "mirror": fixture.Component.IsMirroredHorizontally = true; break;
            case "dimensions": fixture.Component.WidthMicrometers += 9; break;
            case "original-dimensions": fixture.Component.UnrotatedHeightMicrometers += 7; break;
            case "point-value": fixture.Points[2] = new(40, 30); break;
            case "polygon-added": fixture.Component.OutlinePolygons = [.. fixture.Component.OutlinePolygons!,
                new() { Points = [new(20, 20), new(30, 20), new(30, 30), new(20, 30)] }]; break;
            case "padding": fixture.Router.PathfindingGrid!.ObstaclePaddingMicrometers = 7; break;
        }
        var snapshot = fixture.Capture();
        if (change == "grid") snapshot = snapshot.WithGrid(-10, -10, 180, 170, null, 3);
        var graph = snapshot.CreatePrivateGraph();
        graph.Router.PathfindingGrid!.OutlineRasterizationCount.ShouldBeGreaterThan(0);
        OutlineRasterReuseFixture.AssertParity(graph.Router.PathfindingGrid, OutlineRasterReuseFixture.Cold(graph), graph.Components);
    }

    [Fact]
    public void RebuildAfterRemoval_DropsItsCachedOutlineAndRestoresEmptyCells()
    {
        var fixture = new OutlineRasterReuseFixture();
        var grid = fixture.Router.PathfindingGrid!;
        var before = grid.OutlineRasterizationCount;
        grid.RebuildFromComponents([fixture.Component]);
        grid.OutlineRasterizationCount.ShouldBe(before, "the same live grid also reuses its existing raster owner");
        grid.RebuildFromComponents([]);
        grid.GetBlockedCellCount().ShouldBe(0);
        grid.RebuildFromComponents([fixture.Component]);
        grid.OutlineRasterizationCount.ShouldBe(before + 2, "deleted geometry cannot remain in the bounded cache");
    }
    [Fact]
    public void MutableCallerVertices_RebuildAtTheSamePoseWithoutAReferenceOnlyFalseHit()
    {
        var fixture = new OutlineRasterReuseFixture();
        var grid = fixture.Router.PathfindingGrid!;
        var before = grid.CaptureCellStates();
        fixture.Points[2] = new(40, 30);
        grid.RebuildFromComponents([fixture.Component]);
        grid.OutlineRasterizationCount.ShouldBe(4);
        grid.CaptureCellStates().SequenceEqual(before).ShouldBeFalse();
        var graph = fixture.Capture().CreatePrivateGraph();
        graph.Router.PathfindingGrid!.OutlineRasterizationCount.ShouldBe(0);
        var cold = OutlineRasterReuseFixture.Cold(graph);
        grid.CaptureCellStates().ShouldBe(cold.CaptureCellStates());
        OutlineRasterReuseFixture.AssertParity(graph.Router.PathfindingGrid, cold, graph.Components);
    }

    [Fact]
    public void AllowedRegions_AreReappliedEvenWhenEveryComponentRasterIsReusable()
    {
        var fixture = new OutlineRasterReuseFixture();
        var graph = fixture.Capture().WithGrid(0, 0, 160, 160, [(20d, 20d, 100d, 100d)], 3).CreatePrivateGraph();
        var grid = graph.Router.PathfindingGrid!;
        grid.OutlineRasterizationCount.ShouldBe(0);
        grid.IsBlocked(1, 1).ShouldBeTrue();
        OutlineRasterReuseFixture.AssertParity(grid, OutlineRasterReuseFixture.Cold(graph), graph.Components);
    }

    [Fact]
    public void BackgroundConversion_DropsTheBodyAndItsRetainedRaster()
    {
        var fixture = new OutlineRasterReuseFixture();
        fixture.Router.PathfindingGrid!.CaptureOutlineRasters().ShouldNotBeEmpty();
        fixture.Component.IsRoutingObstacle = false;
        var graph = fixture.Capture().CreatePrivateGraph();
        var grid = graph.Router.PathfindingGrid!;
        grid.GetBlockedCellCount().ShouldBe(0);
        grid.CaptureOutlineRasters().ShouldBeEmpty();
    }

}
