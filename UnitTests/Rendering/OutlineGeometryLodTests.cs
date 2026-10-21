using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Rendering;

public class OutlineGeometryLodTests
{
    [Fact]
    public void AdversarialSawtoothRing_BoundsVertexReadsAndFallsBackToExactSource()
    {
        var vertices = Enumerable.Range(0, 2049)
            .Select(i => new OutlinePoint(i, i % 2 == 0 ? 0 : 1)).ToList();
        vertices.Add(new OutlinePoint(2048, -2));
        vertices.Add(new OutlinePoint(0, -2));
        vertices.Add(vertices[0]);
        var ring = new CountingRing(vertices);

        var result = OutlineGeometryLod.Simplify(ring, 0.1);

        ring.VertexReads.ShouldBeLessThan(96L * ring.Count,
            "an alternating edge must not cause quadratic source traversal on the UI thread");
        result.ShouldBeSameAs(ring, "exhausting the simplification budget keeps exact source geometry");
    }

    private sealed class CountingRing(IReadOnlyList<OutlinePoint> points) : IReadOnlyList<OutlinePoint>
    {
        public long VertexReads { get; private set; }
        public int Count => points.Count;
        public OutlinePoint this[int index] { get { VertexReads++; return points[index]; } }
        public IEnumerator<OutlinePoint> GetEnumerator() => points.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void DenseCurve_RemovesInvisibleVerticesWithoutChangingSourceOrExtents()
    {
        var points = Enumerable.Range(0, 1001).Select(i => new OutlinePoint(
            100 * Math.Cos(i * 2 * Math.PI / 1000), 100 * Math.Sin(i * 2 * Math.PI / 1000))).ToArray();
        points[^1] = points[0];
        var simplified = OutlineGeometryLod.Simplify(points, 0.5);
        simplified.Count.ShouldBeLessThan(100);
        points.Length.ShouldBe(1001);
        simplified[0].ShouldBe(simplified[^1]);
        simplified.Min(p => p.X).ShouldBe(-100);
        simplified.Max(p => p.Y).ShouldBe(100);
        foreach (var point in points)
        {
            double nearest = double.PositiveInfinity;
            for (int i = 1; i < simplified.Count; i++)
            {
                var a = simplified[i - 1]; var b = simplified[i];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
                nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(point.X - a.X - t * dx, 2) + Math.Pow(point.Y - a.Y - t * dy, 2)));
            }
            nearest.ShouldBeLessThanOrEqualTo(0.500001);
        }
    }

    [Fact]
    public void ThinDenselySampledWaveguide_DoesNotCollapseToHalfItsArea()
    {
        OutlinePoint[] points = [new(0, 0), new(50, 0), new(100, 0), new(100, 0.05),
            new(100, 0.1), new(50, 0.1), new(0, 0.1), new(0, 0.05), new(0, 0)];
        OutlineGeometryLod.Simplify(points, 4).ShouldBe(points);
    }
    [Fact]
    public void ThinWaveguide_KeepsItsWidthAndCorners()
    {
        OutlinePoint[] points = [new(0, 0), new(100, 0), new(100, 0.1), new(0, 0.1), new(0, 0)];
        OutlineGeometryLod.Simplify(points, 4).ShouldBe(points);
        OutlineGeometryLod.Simplify(points, 0).ShouldBeSameAs(points);
    }
}
