using CAP_Core.Components.Core;
using CAP_Core.Grid;
using CAP_Core.Grid.GroupCollision;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Routing;
using Shouldly;
using Xunit;
using System.Collections;

namespace UnitTests.Grid.GroupCollision;

public class ImportedOutlineCollisionTests
{
    private static Component Imported()
    {
        var component = CollisionScene.Part("import", 20, 20, 100, 100);
        component.PhysicalPins.Clear();
        component.OutlinePolygons = new[] { new OutlinePolygon { Points = new[] {
            new OutlinePoint(0, 0), new OutlinePoint(100, 0), new OutlinePoint(100, 10),
            new OutlinePoint(10, 10), new OutlinePoint(10, 100), new OutlinePoint(0, 100) } } };
        return component;
    }

    [Fact]
    public void Placement_UsesConcaveOutlineInsteadOfItsBox()
    {
        var imported = Imported();
        var detector = new GroupCollisionDetector();
        detector.CanPlaceFootprint(60, 60, 10, 10, new[] { imported }).ShouldBeTrue();
        detector.CanPlaceFootprint(22, 60, 10, 10, new[] { imported }).ShouldBeFalse();
        detector.CanPlaceFootprint(32, 60, 10, 10, new[] { imported }).ShouldBeFalse("clearance still applies");
    }

    [Fact]
    public void Routing_LeavesVoidOpenAndRetainsRealGeometry()
    {
        var grid = new PathfindingGrid(0, 0, 160, 160);
        grid.AddComponentObstacle(Imported());
        grid.IsBlocked(60, 60).ShouldBeFalse();
        grid.IsBlocked(25, 60).ShouldBeTrue();
        grid.IsBlocked(30, 60).ShouldBeTrue("padding follows the outline");
    }

    [Fact]
    public void Route_FromExternalComponent_ToInnerFacet_UsesImportedCellsOpenVoid()
    {
        var imported = Imported();
        // The facet is on the right edge of the L's vertical leg, far inside its
        // 100um bounding box; the empty right-hand cavity opens toward the source.
        var innerPin = new PhysicalPin { Name = "inner", OffsetXMicrometers = 10,
            OffsetYMicrometers = 50, AngleDegrees = 0, ParentComponent = imported };
        imported.PhysicalPins.Add(innerPin);
        var external = CollisionScene.Part("external", 150, 65, 10, 10);
        var externalPin = external.PhysicalPins[0];
        externalPin.OffsetYMicrometers = 5;
        externalPin.AngleDegrees = 180;
        var router = new WaveguideRouter { MinBendRadiusMicrometers = 5,
            AStarCellSize = 1, ObstaclePaddingMicrometers = 1 };
        router.InitializePathfindingGrid(0, 0, 200, 160, new[] { imported, external });

        var path = router.Route(externalPin, innerPin);

        path.ShouldNotBeNull();
        path.Segments.ShouldNotBeEmpty();
        path.IsBlockedFallback.ShouldBeFalse("the internal facet is accessible through the imported cell's empty cavity");
        path.IsInvalidGeometry.ShouldBeFalse();
        router.IsPathBlocked(path.Segments).ShouldBeFalse("the finished route must avoid actual outline geometry");
        path.Segments[0].StartPoint.X.ShouldBe(150, 0.01);
        path.Segments[0].StartPoint.Y.ShouldBe(70, 0.01);
        path.Segments[^1].EndPoint.X.ShouldBe(30, 0.01);
        path.Segments[^1].EndPoint.Y.ShouldBe(70, 0.01);
    }

    [Fact]
    public void Routing_InnerPinCorridorDoesNotTunnelThroughSeparateWall()
    {
        var imported = Imported();
        imported.PhysicalPins.Add(new PhysicalPin { Name = "inner", OffsetXMicrometers = 15,
            OffsetYMicrometers = 40, AngleDegrees = 180, ParentComponent = imported });
        var grid = new PathfindingGrid(0, 0, 160, 160);
        grid.AddComponentObstacle(imported);
        grid.IsBlocked(35, 60).ShouldBeFalse();
        grid.IsBlocked(27, 60).ShouldBeTrue("outward corridors cannot carve other polygon bodies");
        grid.ClearPinCorridor(35, 60, 180, 20, 4);
        grid.IsBlocked(27, 60).ShouldBeTrue("temporary route escape must also respect the wall");
    }

    [Fact]
    public void RotatedAndMirroredOutline_UsesTheRenderedPose()
    {
        var imported = Imported();
        ComponentPoseTransform.MirrorPinsHorizontally(imported);
        ComponentPoseTransform.Rotate90CounterClockwise(imported);
        var grid = new PathfindingGrid(0, 0, 160, 160);
        grid.AddComponentObstacle(imported);
        grid.IsBlocked(100, 100).ShouldBeFalse();
        grid.IsBlocked(25, 70).ShouldBeTrue();
        new GroupCollisionDetector().CanPlaceFootprint(90, 90, 10, 10, new[] { imported }).ShouldBeTrue();
    }

    [Fact]
    public void MovingOutlinedComponent_AlsoUsesItsGeometry()
    {
        var imported = Imported();
        var obstacle = CollisionScene.Part("other", 160, 160, 10, 10);
        new GroupCollisionDetector().CanPlaceFootprint(120, 120, 100, 100,
            new[] { obstacle }, imported).ShouldBeTrue();
    }

    [Fact]
    public void NonCardinalPose_LeavesRotatedBoundingBoxCornersOpen()
    {
        var imported = CollisionScene.Part("rotated", 20, 20, 100, 20);
        imported.PhysicalPins.Clear();
        imported.OutlinePolygons = new[] { new OutlinePolygon { Points = new[] {
            new OutlinePoint(0, 0), new OutlinePoint(100, 0),
            new OutlinePoint(100, 20), new OutlinePoint(0, 20) } } };
        ComponentPoseTransform.RotateByDegrees(imported, 45);
        var grid = new PathfindingGrid(0, 0, 160, 160);
        grid.AddComponentObstacle(imported);
        grid.IsBlocked(25, 95).ShouldBeFalse();
        grid.IsBlocked(60, 60).ShouldBeTrue();
        var detector = new GroupCollisionDetector();
        detector.CanPlaceFootprint(22, 90, 5, 5, new[] { imported }).ShouldBeTrue();
        detector.CanPlaceFootprint(60, 60, 5, 5, new[] { imported }).ShouldBeFalse();
    }

    [Fact]
    public void SubcellOutlineAndBackground_PreserveObstacleSemantics()
    {
        var imported = Imported();
        imported.OutlinePolygons = new[] { new OutlinePolygon { Points = new[] {
            new OutlinePoint(40.2, 0), new OutlinePoint(40.3, 0),
            new OutlinePoint(40.3, 100), new OutlinePoint(40.2, 100) } } };
        var grid = new PathfindingGrid(0, 0, 160, 160, padding: 0);
        grid.AddComponentObstacle(imported);
        grid.IsBlocked(60, 60).ShouldBeTrue("thin geometry cannot disappear between cell centers");
        grid.IsBlocked(80, 60).ShouldBeFalse();
        grid.RemoveComponentObstacle(imported);
        grid.IsBlocked(60, 60).ShouldBeFalse();
        imported.IsRoutingObstacle = false;
        grid.AddComponentObstacle(imported);
        grid.IsBlocked(60, 60).ShouldBeFalse();
        new GroupCollisionDetector().CanPlaceFootprint(60, 60, 5, 5, new[] { imported }).ShouldBeTrue();
    }

    [Fact]
    public void DenseImportedGeometry_IsReusedAcrossPlacementQueriesAndTranslation()
    {
        var imported = CollisionScene.Part("dense", 0, 0, 2000, 2000);
        var sources = Enumerable.Range(0, 10000).Select(i => new CountingPoints(new[] {
            new OutlinePoint(i % 100 * 20, i / 100 * 20), new OutlinePoint(i % 100 * 20 + 2, i / 100 * 20),
            new OutlinePoint(i % 100 * 20 + 2, i / 100 * 20 + 2), new OutlinePoint(i % 100 * 20, i / 100 * 20 + 2)
        })).ToArray();
        imported.OutlinePolygons = sources.Select(p => new OutlinePolygon { Points = p }).ToArray();
        var detector = new GroupCollisionDetector();
        detector.CanPlaceFootprint(9, 9, 1, 1, new[] { imported }).ShouldBeTrue();
        int reads = sources.Sum(p => p.Reads);
        reads.ShouldBeGreaterThan(0);
        for (int i = 0; i < 20; i++)
        {
            imported.PhysicalX = i;
            detector.CanPlaceFootprint(9 + i, 9, 1, 1, new[] { imported }).ShouldBeTrue();
        }
        sources.Sum(p => p.Reads).ShouldBe(reads, "dragging must not retransform every imported vertex");
        imported.OutlinePolygons = new[] { new OutlinePolygon { Points = new[] {
            new OutlinePoint(0, 0), new OutlinePoint(100, 0), new OutlinePoint(100, 100), new OutlinePoint(0, 100) } } };
        detector.CanPlaceFootprint(30, 30, 1, 1, new[] { imported }).ShouldBeFalse("replacing outlines invalidates cached geometry");
    }

    private sealed class CountingPoints(OutlinePoint[] points) : IReadOnlyList<OutlinePoint>
    {
        public int Reads { get; private set; }
        public int Count => points.Length;
        public OutlinePoint this[int index] { get { Reads++; return points[index]; } }
        public IEnumerator<OutlinePoint> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void DensePolygonPair_WithOverlappingBoxes_UsesSpatialEdgeQueries()
    {
        static CountingPoints Circle(double x, double y) => new(Enumerable.Range(0, 10000)
            .Select(i => new OutlinePoint(x + 1000 * Math.Cos(i * 2 * Math.PI / 10000),
                y + 1000 * Math.Sin(i * 2 * Math.PI / 10000))).ToArray());
        var first = Circle(0, 0);
        var second = Circle(1500, 1500);
        OutlineCollisionGeometry.WithinDistance(first, second, 5, Imported()).ShouldBeFalse();
        (first.Reads + second.Reads).ShouldBeLessThan(400000,
            "dense disjoint polygons must not compare every edge pair or repeat point containment per edge");
        OutlineCollisionGeometry.WithinDistance(first, Circle(1400, 1400), 5, Imported()).ShouldBeTrue();
    }
}
