using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests;
using Xunit;

namespace UnitTests.Analysis;

public class SweepParameterTests
{
    [Fact]
    public void Constructor_ValidComponentAndSlider_CreatesParameter()
    {
        // Arrange
        var component = CreateComponentWithSlider();

        // Act
        var param = new SweepParameter(component, 0, "Test Param");

        // Assert
        param.TargetComponent.ShouldBe(component);
        param.SliderIndex.ShouldBe(0);
        param.DisplayName.ShouldBe("Test Param");
    }

    [Fact]
    public void Constructor_NullComponent_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SweepParameter(null!, 0, "Test"));
    }

    [Fact]
    public void Constructor_NullDisplayName_ThrowsArgumentNullException()
    {
        var component = CreateComponentWithSlider();

        Should.Throw<ArgumentNullException>(() =>
            new SweepParameter(component, 0, null!));
    }

    [Fact]
    public void Constructor_NegativeSliderIndex_ThrowsArgumentOutOfRangeException()
    {
        var component = CreateComponentWithSlider();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SweepParameter(component, -1, "Test"));
    }

    [Fact]
    public void Constructor_InvalidSliderIndex_ThrowsArgumentException()
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();

        Should.Throw<ArgumentException>(() =>
            new SweepParameter(component, 0, "Test"));
    }

    [Fact]
    public void GetSlider_ReturnsCorrectSlider()
    {
        // Arrange
        var component = CreateComponentWithSlider();
        var param = new SweepParameter(component, 0, "Test");

        // Act
        var slider = param.GetSlider();

        // Assert
        slider.ShouldNotBeNull();
        slider.ShouldBe(component.GetSlider(0));
    }

    private static Component CreateComponentWithSlider()
    {
        return TestComponentHelper.CreateComponentWithSlider(
            minValue: 0, maxValue: 1, initialValue: 0.5);
    }
}
