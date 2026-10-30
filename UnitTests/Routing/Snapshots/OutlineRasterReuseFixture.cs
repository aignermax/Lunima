using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.Routing.Snapshots;

internal sealed class OutlineRasterReuseFixture
{
    internal Component Component { get; } = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
    internal OutlinePoint[] Points { get; } = [new(0, 0), new(80, 0), new(80, 10), new(10, 10), new(10, 60), new(0, 60)];
    internal WaveguideRouter Router { get; } = new() { AStarCellSize = 1, ObstaclePaddingMicrometers = 3 };
    internal WaveguideConnectionManager Manager { get; }
    internal OutlineRasterReuseFixture()
    {
        Component.PhysicalX = Component.PhysicalY = 30;
        Component.WidthMicrometers = Component.UnrotatedWidthMicrometers = 80;
        Component.HeightMicrometers = Component.UnrotatedHeightMicrometers = 60;
        Component.IsRoutingObstacle = true;
        Component.OutlinePolygons = [new() { Points = Points }];
        Component.PhysicalPins[0].OffsetXMicrometers = 10;
        Component.PhysicalPins[0].OffsetYMicrometers = 40;
        Component.PhysicalPins[0].AngleDegrees = 0;
        Component.PhysicalPins[1].OffsetXMicrometers = 80;
        Component.PhysicalPins[1].OffsetYMicrometers = 5;
        Component.PhysicalPins[1].AngleDegrees = 0;
        Manager = new(Router);
        Router.InitializePathfindingGrid(0, 0, 160, 160, [Component]);
    }
    internal RoutingInputSnapshot Capture(bool preserveCells = false) =>
        RoutingInputSnapshot.Capture([Component], Router, Manager, preserveCells);

    internal static PathfindingGrid Cold(RoutingWorkGraph graph)
    {
        var source = graph.Router.PathfindingGrid!;
        var cold = new PathfindingGrid(source.MinX, source.MinY, source.MaxX, source.MaxY,
            source.CellSizeMicrometers, source.ObstaclePaddingMicrometers) { AllowedRegions = source.AllowedRegions };
        cold.RebuildFromComponents(graph.Components);
        return cold;
    }
    internal static void AssertParity(PathfindingGrid actual, PathfindingGrid expected, IEnumerable<Component> components)
    {
        actual.CaptureCellStates().ShouldBe(expected.CaptureCellStates());
        for (var x = 0; x < actual.Width; x++)
        for (var y = 0; y < actual.Height; y++)
            actual.IsPinReservationZone(x, y).ShouldBe(expected.IsPinReservationZone(x, y));
        foreach (var pin in components.SelectMany(component => component.PhysicalPins))
        {
            var corridor = actual.GetPinCorridorCells([pin]);
            corridor.SetEquals(expected.GetPinCorridorCells([pin])).ShouldBeTrue();
            foreach (var (x, y) in corridor)
                actual.IsBlockedByComponent(x, y, corridor).ShouldBe(expected.IsBlockedByComponent(x, y, corridor));
        }
    }
}
