using CAP_Core.LightCalculation.MaterialDispersion;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.MaterialDispersion;

/// <summary>
/// Tests for <see cref="PolynomialDispersion"/>.
/// </summary>
public class PolynomialDispersionTests
{
    private const double Lambda0 = 1550.0;

    [Fact]
    public void NEffAt_AtCenterWavelength_ReturnsN0()
    {
        var model = new PolynomialDispersion(Lambda0, n0: 2.45, n1: -1e-3, n2: 3.5e-8);
        model.NEffAt(Lambda0).ShouldBe(2.45, tolerance: 1e-12);
    }

    [Fact]
    public void NEffAt_OffCenter_AppliesPolynomial()
    {
        double n0 = 2.45, n1 = -1e-3, n2 = 0.0;
        var model = new PolynomialDispersion(Lambda0, n0: n0, n1: n1, n2: n2);

        double delta = 50.0; // λ = 1600 nm
        double expected = n0 + n1 * delta;
        model.NEffAt(Lambda0 + delta).ShouldBe(expected, tolerance: 1e-12);
    }

    [Fact]
    public void GroupIndexAt_DerivedFromSlope_Correct()
    {
        // n_g = n_eff - λ·dn/dλ  →  at λ₀ with only n1 term: n_g = n0 - λ₀·n1
        double n0 = 2.45, n1 = -1e-3;
        var model = new PolynomialDispersion(Lambda0, n0: n0, n1: n1);

        double expected = n0 - Lambda0 * n1;
        model.GroupIndexAt(Lambda0).ShouldBe(expected, tolerance: 1e-10);
    }

    [Fact]
    public void GroupIndexAt_ExplicitNg0_UsedCorrectly()
    {
        var model = new PolynomialDispersion(Lambda0, n0: 2.45, ng0: 4.2);
        // At center wavelength, dn/dλ = n1 = 0, so n_g = n_eff - λ·0 = 2.45 … but n_g formula gives 4.2
        // The explicit ng0 overrides the derivation
        model.GroupIndexAt(Lambda0).ShouldBe(4.2, tolerance: 1e-10);
    }

    [Fact]
    public void LossAt_AtCenter_ReturnsLoss0()
    {
        var model = new PolynomialDispersion(Lambda0, n0: 2.45, loss0: 0.5);
        model.LossDbPerCmAt(Lambda0).ShouldBe(0.5, tolerance: 1e-12);
    }

    [Fact]
    public void LossAt_WithSlope_VariesWithWavelength()
    {
        double loss0 = 0.5, lossSlope = -1e-3; // decreasing loss at longer λ
        var model = new PolynomialDispersion(Lambda0, n0: 2.45, loss0: loss0, lossSlope: lossSlope);

        double lossAt1600 = model.LossDbPerCmAt(1600.0);
        double lossAt1500 = model.LossDbPerCmAt(1500.0);

        lossAt1600.ShouldBe(loss0 + lossSlope * 50, tolerance: 1e-12);
        lossAt1500.ShouldBe(loss0 + lossSlope * (-50), tolerance: 1e-12);
        lossAt1600.ShouldBeLessThan(lossAt1500); // loss decreases with λ
    }

    [Fact]
    public void LossAt_NeverNegative()
    {
        // A large negative slope can produce negative values — must be clamped
        var model = new PolynomialDispersion(Lambda0, n0: 2.45, loss0: 0.01, lossSlope: -10.0);
        model.LossDbPerCmAt(1600.0).ShouldBeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public void Constructor_NegativeLoss0_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PolynomialDispersion(Lambda0, n0: 2.45, loss0: -1.0));
    }

    [Fact]
    public void Constructor_ZeroCenterWavelength_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PolynomialDispersion(0, n0: 2.45));
    }
}
