using CAP_Core.Routing.WaveguideCompanions;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.WaveguideCompanions;

/// <summary>
/// Band geometry of a companion layer (offset each side vs absolute width) and the spec's
/// keep-out / union rules.
/// </summary>
public class WaveguideCompanionSpecTests
{
    [Fact]
    public void OffsetLayer_GrowsBeyondEachCoreEdge()
    {
        var layer = new WaveguideCompanionLayer("T", 2, 0, WaveguideCompanionRole.Trench, 8.0, null);

        layer.OffsetFor(0.45).ShouldBe(8.0);
        layer.BandWidthFor(0.45).ShouldBe(16.45, 1e-9);
    }

    [Fact]
    public void AbsoluteWidthLayer_IsCenteredOnTheCore()
    {
        var layer = new WaveguideCompanionLayer("C", 22, 0, WaveguideCompanionRole.Cladding, null, 10.5);

        layer.OffsetFor(0.5).ShouldBe(5.0, 1e-9);
        layer.BandWidthFor(0.5).ShouldBe(10.5, 1e-9);
    }

    [Fact]
    public void AbsoluteWidthNarrowerThanCore_NeverShrinksBelowTheCore()
    {
        var layer = new WaveguideCompanionLayer("C", 22, 0, WaveguideCompanionRole.Cladding, null, 0.3);

        layer.OffsetFor(1.0).ShouldBe(0.0);
        layer.BandWidthFor(1.0).ShouldBe(1.0);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1.0, 5.0)]
    [InlineData(-1.0, null)]
    [InlineData(null, 0.0)]
    [InlineData(double.NaN, null)]
    public void Constructor_RejectsAmbiguousOrInvalidWidths(double? offset, double? width)
    {
        Should.Throw<ArgumentException>(() =>
            new WaveguideCompanionLayer("X", 1, 0, WaveguideCompanionRole.Trench, offset, width));
    }

    [Fact]
    public void KeepOutOffset_IsTheWidestKeepOutBandOnly()
    {
        var spec = new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("T", 2, 0, WaveguideCompanionRole.Trench, 20.0, null),
            new WaveguideCompanionLayer("K1", 40, 0, WaveguideCompanionRole.KeepOut, 2.0, null),
            new WaveguideCompanionLayer("K2", 41, 0, WaveguideCompanionRole.KeepOut, 3.0, null),
        });

        spec.KeepOutOffsetFor(0.5).ShouldBe(3.0);
    }

    [Fact]
    public void KeepOutOffset_WithoutKeepOutLayer_IsZero()
    {
        var spec = new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("T", 2, 0, WaveguideCompanionRole.Trench, 8.0, null),
        });

        spec.KeepOutOffsetFor(0.5).ShouldBe(0.0);
        WaveguideCompanionSpec.Empty.KeepOutOffsetFor(0.5).ShouldBe(0.0);
    }

    [Fact]
    public void Union_KeepsFirstSidesLayerPerGdsPairAndAddsTheRest()
    {
        var first = new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("T", 2, 0, WaveguideCompanionRole.Trench, 8.0, null),
        });
        var second = new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("T-other", 2, 0, WaveguideCompanionRole.Trench, 4.0, null),
            new WaveguideCompanionLayer("K", 40, 0, WaveguideCompanionRole.KeepOut, 2.0, null),
        });

        var union = WaveguideCompanionSpec.Union(first, second);

        union.Layers.Select(l => l.Name).ShouldBe(new[] { "T", "K" });
        WaveguideCompanionSpec.Union(WaveguideCompanionSpec.Empty, second).ShouldBeSameAs(second);
        WaveguideCompanionSpec.Union(first, WaveguideCompanionSpec.Empty).ShouldBeSameAs(first);
    }
}
