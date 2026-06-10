using CAP_Core.Analysis.OnaAnalysis;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.OnaAnalysis;

public class WavelengthSweepConfigurationTests
{
    [Fact]
    public void Constructor_ValidArgs_SetsProperties()
    {
        var config = new WavelengthSweepConfiguration(1500, 1600, 21);

        config.StartNm.ShouldBe(1500);
        config.EndNm.ShouldBe(1600);
        config.StepCount.ShouldBe(21);
    }

    [Fact]
    public void Constructor_StartNotPositive_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new WavelengthSweepConfiguration(0, 1600, 10));
    }

    [Fact]
    public void Constructor_EndNotGreaterThanStart_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new WavelengthSweepConfiguration(1600, 1500, 10));
    }

    [Fact]
    public void Constructor_StepCountLessThanTwo_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new WavelengthSweepConfiguration(1500, 1600, 1));
    }

    [Fact]
    public void Constructor_StepCountExceedsMax_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new WavelengthSweepConfiguration(1500, 1600, WavelengthSweepConfiguration.MaxStepCount + 1));
    }

    [Fact]
    public void GenerateWavelengthValues_ThreeSteps_ReturnsEndpoints()
    {
        var config = new WavelengthSweepConfiguration(1500, 1600, 3);
        var values = config.GenerateWavelengthValues();

        values.Length.ShouldBe(3);
        values[0].ShouldBe(1500);
        values[2].ShouldBe(1600);
    }

    [Fact]
    public void GenerateWavelengthValues_TwoSteps_ReturnsStartAndEnd()
    {
        var config = new WavelengthSweepConfiguration(1500, 1600, 2);
        var values = config.GenerateWavelengthValues();

        values[0].ShouldBe(1500);
        values[1].ShouldBe(1600);
    }
}
