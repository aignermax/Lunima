using CAP_Core.Analysis;
using Shouldly;

namespace UnitTests.Analysis;

public class HistogramGeneratorTests
{
    [Fact]
    public void Generate_EvenDistribution_CorrectBuckets()
    {
        var samples = new List<double> { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0 };

        var buckets = HistogramGenerator.Generate(samples, bucketCount: 5);

        buckets.Count.ShouldBe(5);
        buckets.Sum(b => b.Count).ShouldBe(10);
    }

    [Fact]
    public void Generate_EmptySamples_ReturnsEmptyList()
    {
        var buckets = HistogramGenerator.Generate(new List<double>(), bucketCount: 5);

        buckets.Count.ShouldBe(0);
    }

    [Fact]
    public void Generate_AllSameValue_SingleBucket()
    {
        var samples = new List<double> { 3.0, 3.0, 3.0, 3.0 };

        var buckets = HistogramGenerator.Generate(samples, bucketCount: 5);

        buckets.Count.ShouldBe(1);
        buckets[0].Count.ShouldBe(4);
    }

    [Fact]
    public void Generate_TwoValues_CorrectDistribution()
    {
        var samples = new List<double> { 0.0, 0.0, 0.0, 10.0 };

        var buckets = HistogramGenerator.Generate(samples, bucketCount: 2);

        buckets.Count.ShouldBe(2);
        buckets[0].Count.ShouldBe(3);
        buckets[1].Count.ShouldBe(1);
    }

    [Fact]
    public void Generate_BucketBoundsAreCorrect()
    {
        var samples = new List<double> { 0.0, 5.0, 10.0 };

        var buckets = HistogramGenerator.Generate(samples, bucketCount: 2);

        buckets[0].LowerBound.ShouldBe(0.0);
        buckets[0].UpperBound.ShouldBe(5.0);
        buckets[1].LowerBound.ShouldBe(5.0);
        buckets[1].UpperBound.ShouldBe(10.0);
    }

    [Fact]
    public void Generate_ZeroBuckets_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => HistogramGenerator.Generate(new List<double> { 1.0 }, bucketCount: 0));
    }

    [Fact]
    public void Generate_NegativeBuckets_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => HistogramGenerator.Generate(new List<double> { 1.0 }, bucketCount: -1));
    }

    [Fact]
    public void Generate_DefaultBucketCount_UsesTenBins()
    {
        var samples = Enumerable.Range(0, 100).Select(i => (double)i).ToList();

        var buckets = HistogramGenerator.Generate(samples);

        buckets.Count.ShouldBe(HistogramGenerator.DefaultBucketCount);
    }
}
