using CAP_Core.Analysis;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

public class LossSeverityClassifierTests
{
    [Theory]
    [InlineData(0.0, LossSeverity.Low)]
    [InlineData(1.5, LossSeverity.Low)]
    [InlineData(2.99, LossSeverity.Low)]
    [InlineData(3.0, LossSeverity.Medium)]
    [InlineData(5.0, LossSeverity.Medium)]
    [InlineData(9.99, LossSeverity.Medium)]
    [InlineData(10.0, LossSeverity.High)]
    [InlineData(15.0, LossSeverity.High)]
    [InlineData(100.0, LossSeverity.High)]
    public void Classify_ReturnsExpectedSeverity(double lossDb, LossSeverity expected)
    {
        LossSeverityClassifier.Classify(lossDb).ShouldBe(expected);
    }

    [Fact]
    public void Thresholds_HaveExpectedValues()
    {
        LossSeverityClassifier.LowThresholdDb.ShouldBe(3.0);
        LossSeverityClassifier.MediumThresholdDb.ShouldBe(10.0);
    }
}
