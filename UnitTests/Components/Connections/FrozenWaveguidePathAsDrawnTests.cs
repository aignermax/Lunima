using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Components.Connections;

/// <summary>
/// Drawn polygons on a frozen path (inside a group or on the canvas) follow translations
/// and disappear on any other change of the path, so they are never drawn stale.
/// </summary>
public class FrozenWaveguidePathAsDrawnTests
{
    private static FrozenWaveguidePath PathWithDrawing()
    {
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        return new FrozenWaveguidePath
        {
            Path = route,
            AsDrawnGeometry = new AsDrawnGeometry(new[]
            {
                new OutlinePolygon
                {
                    Layer = 1,
                    Points = new[] { new OutlinePoint(0, -1), new OutlinePoint(100, -1), new OutlinePoint(100, 1), new OutlinePoint(0, 1) },
                },
            }),
        };
    }

    [Fact]
    public void TranslateBy_MovesThePolygonsWithThePath()
    {
        var path = PathWithDrawing();

        path.TranslateBy(10, 20);

        var polygon = path.AsDrawnGeometry.ShouldNotBeNull().Polygons.Single();
        polygon.Points[0].X.ShouldBe(10);
        polygon.Points[0].Y.ShouldBe(19);
    }

    [Fact]
    public void RotatedPath_HidesThePolygons()
    {
        var path = PathWithDrawing();
        var rotated = new RoutedPath();
        rotated.Segments.Add(new StraightSegment(0, 0, 0, 100, 90));

        path.Path = rotated;

        path.AsDrawnGeometry.ShouldBeNull("a group rotation reshapes the path; the old polygons would be stale");
    }

    [Fact]
    public void AttachAsDrawnTo_HandsThePolygonsToTheUngroupedConnection()
    {
        var path = PathWithDrawing();
        var connection = new WaveguideConnection();
        connection.RestoreCachedPath(path.Path.DeepCopy());
        connection.IsRouteFrozen = true;

        path.AttachAsDrawnTo(connection);

        connection.AsDrawnGeometry.ShouldNotBeNull();
    }
}
