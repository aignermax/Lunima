using CAP_Core.Analysis;
using Shouldly;

namespace UnitTests.Analysis;

public class ParameterVariationTests
{
    [Fact]
    public void Constructor_ValidFraction_CreatesInstance()
    {
        var variation = new ParameterVariation(ParameterType.Coupling, 0.05);

        variation.Type.ShouldBe(ParameterType.Coupling);
        variation.VariationFraction.ShouldBe(0.05);
    }

    [Fact]
    public void Constructor_ZeroFraction_CreatesInstance()
    {
        var variation = new ParameterVariation(ParameterType.Phase, 0.0);

        variation.VariationFraction.ShouldBe(0.0);
    }

    [Fact]
    public void Constructor_MaxFraction_CreatesInstance()
    {
        var variation = new ParameterVariation(ParameterType.Loss, 1.0);

        variation.VariationFraction.ShouldBe(1.0);
    }

    [Fact]
    public void Constructor_NegativeFraction_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ParameterVariation(ParameterType.Coupling, -0.01));
    }

    [Fact]
    public void Constructor_FractionAboveOne_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ParameterVariation(ParameterType.Loss, 1.01));
    }
}
