using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// The pruned minimum distance between two sampled routes equals the exhaustive edge-against-
/// edge minimum — for random crossing, touching and far-apart routes made of straights and arcs.
/// </summary>
public class SampledPolylineMinimumDistanceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void MinimumDistance_EqualsExhaustiveSearch(int seed)
    {
        var random = new Random(seed);
        for (int trial = 0; trial < 40; trial++)
        {
            var first = RandomRoute(random);
            var second = RandomRoute(random);

            double pruned = PathIntersectionDetector.MinimumDistance(first, second);

            pruned.ShouldBe(Exhaustive(first, second), 1e-9, $"seed {seed}, trial {trial}");
        }
    }

    private static RoutedPath RandomRoute(Random random)
    {
        var path = new RoutedPath();
        var builder = new PosePathBuilder(path, random.NextDouble() * 400, random.NextDouble() * 400, random.Next(4) * 90);
        for (int piece = 0; piece < 6; piece++)
        {
            if (random.Next(2) == 0)
                builder.AppendStraight(10 + random.NextDouble() * 200);
            else
                builder.AppendArc((random.Next(2) == 0 ? 1 : -1) * (30 + random.NextDouble() * 120), 10 + random.NextDouble() * 80);
        }
        return path;
    }

    private static double Exhaustive(RoutedPath first, RoutedPath second)
    {
        var a = PathIntersectionDetector.SamplePolyline(first);
        var b = PathIntersectionDetector.SamplePolyline(second);
        if (a.Count < 2 || b.Count < 2)
            return 0;
        double min = double.MaxValue;
        for (int i = 0; i < a.Count - 1; i++)
            for (int j = 0; j < b.Count - 1; j++)
            {
                if (PathIntersectionDetector.SegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1]))
                    return 0;
                min = Math.Min(min, PathIntersectionDetector.SegmentDistance(a[i], a[i + 1], b[j], b[j + 1]));
            }
        return min;
    }
}
