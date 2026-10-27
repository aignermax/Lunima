using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

public sealed class ComponentObstacleOwnershipTransferTests
{
    [Fact]
    public void RebindOwners_PreservesCellsAndCorridors_ThenRemovesUsingAcceptedIdentities()
    {
        var grid = new PathfindingGrid(0, 0, 100, 100, cellSize: 1, padding: 2);
        var worker = CreatePinnedComponent();
        var accepted = CreatePinnedComponent();
        accepted.RestoreSnapshotIdentity(worker.Id);
        accepted.PhysicalPins[0].PinId = worker.PhysicalPins[0].PinId;
        grid.AddComponentObstacle(worker);
        var cells = grid.CaptureCellStates();
        var zones = PinZones(grid);
        var corridors = grid.GetPinCorridorCells(worker.PhysicalPins);
        corridors.ShouldNotBeEmpty();
        zones.ShouldContain(true);

        grid.RebindOwners(new Dictionary<Guid, Component> { [accepted.Id] = accepted });

        grid.CaptureCellStates().ShouldBe(cells);
        PinZones(grid).ShouldBe(zones);
        grid.GetPinCorridorCells(accepted.PhysicalPins).SetEquals(corridors).ShouldBeTrue();
        grid.GetPinCorridorCells(worker.PhysicalPins).ShouldBeEmpty();
        grid.ObstructsBreakout(22, 22, new HashSet<Component> { accepted }).ShouldBeFalse();
        grid.ObstructsBreakout(22, 22, new HashSet<Component> { worker }).ShouldBeTrue();

        grid.RemoveComponentObstacle(accepted);

        grid.GetBlockedCellCount().ShouldBe(0);
        grid.GetPinCorridorCells(accepted.PhysicalPins).ShouldBeEmpty();
        PinZones(grid).ShouldNotContain(true);
        grid.SetCellState(22, 22, 1);
        grid.ObstructsBreakout(22, 22, new HashSet<Component> { accepted }).ShouldBeTrue(
            "a manually blocked cell has no remaining component claim after removal");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(127)]
    public void BodyAndPaddingClaims_AcrossBucketAndClippedGridBoundaries(int x)
    {
        var grid = new PathfindingGrid(0, 0, 200, 100, cellSize: 1, padding: 2);
        var component = TestComponentFactory.CreatePinlessComponent(x, 20, width: 4, height: 4);
        grid.AddComponentObstacle(component);
        var padding = (x + 5, 22);
        var body = (x + 1, 22);
        var tolerated = new HashSet<(int, int)> { padding, body };

        grid.IsComponentPaddingOnly(padding.Item1, padding.Item2).ShouldBeTrue();
        grid.IsBlockedByComponent(padding.Item1, padding.Item2, tolerated).ShouldBeFalse();
        grid.IsComponentPaddingOnly(body.Item1, body.Item2).ShouldBeFalse();
        grid.IsBlockedByComponent(body.Item1, body.Item2, tolerated).ShouldBeTrue();

        grid.RebuildFromComponents([]);
        grid.SetCellState(padding.Item1, padding.Item2, 1);

        grid.IsComponentPaddingOnly(padding.Item1, padding.Item2).ShouldBeFalse();
        grid.IsBlockedByComponent(padding.Item1, padding.Item2, tolerated).ShouldBeTrue(
            "rebuild must remove the old padding claim, leaving unknown blocked cells conservative");
    }

    private static Component CreatePinnedComponent()
    {
        var component = TestComponentFactory.CreatePinlessComponent(20, 20, width: 20, height: 20);
        component.PhysicalPins.Add(TestComponentFactory.CreateRoutingPin(component, 20, 10, 0));
        return component;
    }

    private static bool[] PinZones(PathfindingGrid grid) =>
        Enumerable.Range(0, grid.Width).SelectMany(x => Enumerable.Range(0, grid.Height)
            .Select(y => grid.IsPinReservationZone(x, y))).ToArray();
}
