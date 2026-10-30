using CAP_Core.Routing;
using CAP_Core.Routing.LengthEqualization.Bundle;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.LengthEqualization.Bundle;

/// <summary>
/// The sideways jog a bundle wire uses to change lines: its pieces really end the given height
/// across and the given extent along, at the advertised length, and the extra length grows
/// without a jump from the S-bend range into the two-quarter-arc range.
/// </summary>
public class BundleJogTests
{
    private const double Radius = 50;

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-60)]
    [InlineData(100)]
    [InlineData(-340)]
    public void Steps_EndAtHeightAndExtent_WithTheAdvertisedLength(double height)
    {
        var jog = new BundleJog(height, Radius);
        var path = new RoutedPath();
        var builder = new PosePathBuilder(path, 0, 0, 0);
        foreach (var step in jog.Steps())
        {
            if (step.IsArc)
                builder.AppendArc(step.Value, step.Radius);
            else
                builder.AppendStraight(step.Value);
        }

        builder.Position.X.ShouldBe(jog.Extent, 1e-6);
        builder.Position.Y.ShouldBe(height, 1e-6);
        path.TotalLengthMicrometers.ShouldBe(jog.Length, 1e-6);
        path.Segments.OfType<BendSegment>().ShouldAllBe(b => Math.Abs(b.RadiusMicrometers - Radius) < 1e-9);
    }

    [Fact]
    public void Excess_IsContinuousAndGrowingAcrossTwoRadii()
    {
        double below = new BundleJog(2 * Radius - 1e-6, Radius).Excess;
        double at = new BundleJog(2 * Radius, Radius).Excess;

        at.ShouldBe(below, 1e-4);
        new BundleJog(30, Radius).Excess.ShouldBeLessThan(new BundleJog(60, Radius).Excess);
        new BundleJog(300, Radius).Excess.ShouldBeGreaterThan(at);
        new BundleJog(0, Radius).Excess.ShouldBe(0);
    }
}
