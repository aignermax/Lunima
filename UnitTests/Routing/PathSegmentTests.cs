using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

public class PathSegmentTests
{
    [Fact]
    public void StraightSegment_CalculatesLengthCorrectly()
    {
        // Arrange
        var segment = new StraightSegment(0, 0, 100, 0, 0);

        // Assert
        segment.LengthMicrometers.ShouldBe(100.0);
        segment.StartPoint.ShouldBe((0, 0));
        segment.EndPoint.ShouldBe((100, 0));
        segment.StartAngleDegrees.ShouldBe(0);
        segment.EndAngleDegrees.ShouldBe(0);
    }

    [Fact]
    public void StraightSegment_DiagonalLine_CalculatesLengthCorrectly()
    {
        // Arrange - 3-4-5 triangle
        var segment = new StraightSegment(0, 0, 30, 40, 0);

        // Assert
        segment.LengthMicrometers.ShouldBe(50.0, 0.001);
    }

    [Fact]
    public void BendSegment_90DegreeBend_CalculatesCorrectly()
    {
        // Arrange - 90 degree bend with radius 10
        var segment = new BendSegment(0, 0, 10, 0, 90);

        // Assert
        segment.RadiusMicrometers.ShouldBe(10);
        segment.SweepAngleDegrees.ShouldBe(90);
        segment.Equivalent90DegreeBends.ShouldBe(1.0);

        // Arc length = angle * pi/180 * radius = 90 * pi/180 * 10 = pi/2 * 10
        double expectedLength = 90 * Math.PI / 180.0 * 10;
        segment.LengthMicrometers.ShouldBe(expectedLength, 0.001);
    }

    [Fact]
    public void BendSegment_45DegreeBend_HasHalfEquivalentBends()
    {
        // Arrange
        var segment = new BendSegment(0, 0, 10, 0, 45);

        // Assert
        segment.Equivalent90DegreeBends.ShouldBe(0.5);
    }

    [Fact]
    public void BendSegment_NegativeSweep_CalculatesAbsoluteValues()
    {
        // Arrange - clockwise bend
        var segment = new BendSegment(0, 0, 10, 0, -90);

        // Assert
        segment.SweepAngleDegrees.ShouldBe(-90);
        segment.Equivalent90DegreeBends.ShouldBe(1.0); // Absolute value
        segment.LengthMicrometers.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void BendSegment_TracksStartAndEndAngles()
    {
        // Arrange
        var segment = new BendSegment(0, 0, 10, 45, 90);

        // Assert
        segment.StartAngleDegrees.ShouldBe(45);
        segment.EndAngleDegrees.ShouldBe(135); // 45 + 90
    }
}
