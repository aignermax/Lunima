using CAP_Core.Analysis;
using CAP_Core.Components.ComponentHelpers;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

public class SweepConfigurationTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesConfiguration()
    {
        // Arrange
        var param = CreateTestParameter();

        // Act
        var config = new SweepConfiguration(param, 0.0, 1.0, 11, StandardWaveLengths.RedNM);

        // Assert
        config.Parameter.ShouldBe(param);
        config.StartValue.ShouldBe(0.0);
        config.EndValue.ShouldBe(1.0);
        config.StepCount.ShouldBe(11);
        config.WavelengthNm.ShouldBe(StandardWaveLengths.RedNM);
    }

    [Fact]
    public void Constructor_NullParameter_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SweepConfiguration(null!, 0, 1, 10, 635));
    }

    [Fact]
    public void Constructor_StepCountLessThan2_ThrowsArgumentOutOfRangeException()
    {
        var param = CreateTestParameter();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SweepConfiguration(param, 0, 1, 1, 635));
    }

    [Fact]
    public void Constructor_ZeroWavelength_ThrowsArgumentOutOfRangeException()
    {
        var param = CreateTestParameter();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SweepConfiguration(param, 0, 1, 10, 0));
    }

    [Fact]
    public void GenerateSweepValues_ReturnsCorrectCount()
    {
        // Arrange
        var config = new SweepConfiguration(CreateTestParameter(), 0, 1, 5, 635);

        // Act
        var values = config.GenerateSweepValues();

        // Assert
        values.Length.ShouldBe(5);
    }

    [Fact]
    public void GenerateSweepValues_ReturnsEvenlySpacedValues()
    {
        // Arrange
        var config = new SweepConfiguration(CreateTestParameter(), 0, 1, 5, 635);

        // Act
        var values = config.GenerateSweepValues();

        // Assert
        values[0].ShouldBe(0.0);
        values[1].ShouldBe(0.25, 1e-10);
        values[2].ShouldBe(0.5, 1e-10);
        values[3].ShouldBe(0.75, 1e-10);
        values[4].ShouldBe(1.0, 1e-10);
    }

    [Fact]
    public void GenerateSweepValues_TwoSteps_ReturnsStartAndEnd()
    {
        // Arrange
        var config = new SweepConfiguration(CreateTestParameter(), 0.1, 0.9, 2, 635);

        // Act
        var values = config.GenerateSweepValues();

        // Assert
        values.Length.ShouldBe(2);
        values[0].ShouldBe(0.1, 1e-10);
        values[1].ShouldBe(0.9, 1e-10);
    }

    private static SweepParameter CreateTestParameter()
    {
        var component = TestComponentHelper.CreateComponentWithSlider();
        return new SweepParameter(component, 0, "Test");
    }
}
