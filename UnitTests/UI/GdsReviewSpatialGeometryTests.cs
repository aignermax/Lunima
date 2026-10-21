using System.Collections;
using CAP.Avalonia.Controls.GdsImport;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

public class GdsReviewSpatialGeometryTests
{
    [Theory]
    [InlineData(8_000, false)]
    [InlineData(8_000, true)]
    [InlineData(100_000, false)]
    [InlineData(100_000, true)]
    public void SawtoothSimplificationBoundsWorkAndPreservesExactSource(int teeth, bool explicitlyClosed)
    {
        var source = Enumerable.Range(0, teeth)
            .Select(i => new GdsPoint((double)i / teeth, .1 * (i % 2)))
            .Concat([new GdsPoint(1, -.01), new GdsPoint(0, -.01)])
            .ToList();
        if (explicitlyClosed) source.Add(source[0]);
        var original = source.ToArray();
        var points = new ReadBudgetPoints(source, 64L * source.Count);

        var result = GdsReviewSpatialGeometry.Simplify(points, 1d / 512);

        result.ShouldBeSameAs(points, "exhausted simplification must retain exact geometry");
        source.ShouldBe(original);
    }

    private sealed class ReadBudgetPoints(IReadOnlyList<GdsPoint> points, long remainingReads)
        : IReadOnlyList<GdsPoint>
    {
        public int Count => points.Count;
        public GdsPoint this[int index]
        {
            get
            {
                if (remainingReads-- <= 0)
                    throw new InvalidOperationException("Simplification exceeded the linear point-read budget.");
                return points[index];
            }
        }

        public IEnumerator<GdsPoint> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
