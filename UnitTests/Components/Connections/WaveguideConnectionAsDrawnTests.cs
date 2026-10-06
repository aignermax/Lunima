using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Components.Connections;

/// <summary>
/// The drawn polygons of an imported route describe the connection only while its
/// route is still exactly the imported one — these tests pin when they stay and when
/// they disappear on their own.
/// </summary>
public class WaveguideConnectionAsDrawnTests
{
    private static readonly AsDrawnGeometry Drawn = new(new[]
    {
        new OutlinePolygon
        {
            Layer = 1,
            Points = new[] { new OutlinePoint(0, -1), new OutlinePoint(100, -1), new OutlinePoint(100, 1), new OutlinePoint(0, 1), new OutlinePoint(0, -1) },
        },
    });

    private static WaveguideConnection FrozenImportedConnection()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        var connection = new WaveguideConnection();
        connection.RestoreCachedPath(path);
        connection.IsRouteFrozen = true;
        connection.AttachAsDrawnGeometry(Drawn);
        return connection;
    }

    [Fact]
    public void FrozenUntouchedRoute_ShowsTheDrawnPolygons()
    {
        FrozenImportedConnection().AsDrawnGeometry.ShouldBeSameAs(Drawn);
    }

    [Fact]
    public void IdenticalCopyOfTheRoute_KeepsTheDrawnPolygons()
    {
        var connection = FrozenImportedConnection();

        connection.ReplaceRoutedPath(connection.RoutedPath!.DeepCopy());

        connection.AsDrawnGeometry.ShouldBeSameAs(Drawn, "routing passes swap in identical copies");
    }

    [Fact]
    public void Unfreezing_HidesTheDrawnPolygons()
    {
        var connection = FrozenImportedConnection();

        connection.IsRouteFrozen = false;

        connection.AsDrawnGeometry.ShouldBeNull();
    }

    [Fact]
    public void InvalidatedRoute_HidesTheDrawnPolygons()
    {
        var connection = FrozenImportedConnection();

        connection.InvalidateRoute();

        connection.AsDrawnGeometry.ShouldBeNull();
    }

    [Fact]
    public void ManualBendEdit_HidesTheDrawnPolygons()
    {
        var connection = FrozenImportedConnection();

        connection.BendRadiusOverrides[0] = 25;

        connection.AsDrawnGeometry.ShouldBeNull("an edited route no longer matches the drawing");
    }

    [Fact]
    public void DifferentRoute_HidesTheDrawnPolygons()
    {
        var connection = FrozenImportedConnection();
        var rerouted = new RoutedPath();
        rerouted.Segments.Add(new StraightSegment(0, 0, 50, 0, 0));
        rerouted.Segments.Add(new StraightSegment(50, 0, 100, 0, 0));

        connection.ReplaceRoutedPath(rerouted);

        connection.AsDrawnGeometry.ShouldBeNull();
    }

    [Fact]
    public void Translated_ShiftsEveryVertexAndTheBounds()
    {
        var moved = Drawn.Translated(10, -5);

        moved.Polygons[0].Points[0].ShouldBe(new OutlinePoint(10, -6));
        moved.Bounds.ShouldBe((10.0, -6.0, 110.0, -4.0));
        Drawn.Bounds.ShouldBe((0.0, -1.0, 100.0, 1.0), "the original stays untouched");
    }

    [Fact]
    public void EmptyGeometry_IsRejected()
    {
        Should.Throw<ArgumentException>(() => new AsDrawnGeometry(Array.Empty<OutlinePolygon>()));
    }
}
