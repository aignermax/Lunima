using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// The bounding-box pruning of <see cref="SampledPolyline"/> must never change a verdict:
/// it is compared against testing every edge pair of the same sampled polylines.
/// </summary>
public class SampledPolylineTests
{
    [Fact]
    public void Crosses_AgreesWithBruteForce_AcrossChunkBoundaries()
    {
        var random = new Random(1234);
        for (int trial = 0; trial < 300; trial++)
        {
            var first = RandomZigzag(random, 20 + random.Next(40));
            var second = RandomZigzag(random, 20 + random.Next(40));

            SampledPolyline.From(first).Crosses(SampledPolyline.From(second))
                .ShouldBe(BruteForceCrosses(first, second), $"trial {trial}");
        }
    }

    [Fact]
    public void Crosses_FindsACrossingInTheLastEdgeOfALongPath()
    {
        var longPath = Zigzag(Enumerable.Range(0, 40).Select(i => (i * 10.0, i % 2 == 0 ? 0.0 : 1.0)).Append((400.0, 50.0)));
        var vertical = Zigzag(new[] { (395.0, 40.0), (395.0, 10.0) });

        SampledPolyline.From(longPath).Crosses(SampledPolyline.From(vertical)).ShouldBeTrue();
    }

    private static RoutedPath RandomZigzag(Random random, int vertices) =>
        Zigzag(Enumerable.Range(0, vertices).Select(i => (i * 5.0 + random.NextDouble() * 20, random.NextDouble() * 100)));

    private static RoutedPath Zigzag(IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        var path = new RoutedPath();
        for (int i = 1; i < list.Count; i++)
        {
            var (a, b) = (list[i - 1], list[i]);
            path.Segments.Add(new StraightSegment(a.X, a.Y, b.X, b.Y, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI));
        }
        return path;
    }

    private static bool BruteForceCrosses(RoutedPath first, RoutedPath second)
    {
        var a = PathIntersectionDetector.SamplePolyline(first);
        var b = PathIntersectionDetector.SamplePolyline(second);
        for (int i = 0; i + 1 < a.Count; i++)
        {
            for (int j = 0; j + 1 < b.Count; j++)
            {
                if (PathIntersectionDetector.SegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1]))
                    return true;
            }
        }
        return false;
    }
}
