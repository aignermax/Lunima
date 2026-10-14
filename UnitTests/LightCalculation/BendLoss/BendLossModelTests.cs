using CAP_Core.LightCalculation.BendLoss;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.BendLoss;

/// <summary>
/// The radius-dependent bend-loss model: radiation per 90° decays exponentially with radius,
/// scales linearly with the sweep angle, junction loss grows with the square of the curvature
/// step, every value is finite, non-negative and capped — never a gain.
/// </summary>
public class BendLossModelTests
{
    public static TheoryData<WaveguideFamily> Families => new()
    {
        WaveguideFamily.Generic, WaveguideFamily.SoiStrip, WaveguideFamily.SiliconNitride,
        WaveguideFamily.InpDeepEtch, WaveguideFamily.InpShallowRib,
    };

    [Theory]
    [MemberData(nameof(Families))]
    public void RadiationAndCornerLoss_DecreaseMonotonicallyWithRadius(WaveguideFamily family)
    {
        var model = BendLossPresets.ForFamily(family);
        double previousRadiation = double.MaxValue;
        double previousCorner = double.MaxValue;
        for (double radius = 0.5; radius <= 500; radius *= 1.1)
        {
            double radiation = model.RadiationLossDbPer90Deg(radius);
            double corner = model.CornerLossDb(radius, 90);
            radiation.ShouldBeLessThanOrEqualTo(previousRadiation, $"{family} radiation at R = {radius:F2} µm");
            corner.ShouldBeLessThanOrEqualTo(previousCorner, $"{family} corner loss at R = {radius:F2} µm");
            radiation.ShouldBeGreaterThanOrEqualTo(0);
            previousRadiation = radiation;
            previousCorner = corner;
        }
    }

    [Fact]
    public void Radiation_ScalesLinearlyWithSweepAngle()
    {
        var model = BendLossPresets.Generic;
        double quarter = model.RadiationLossDb(8, 90);

        model.RadiationLossDb(8, 45).ShouldBe(quarter / 2, 1e-12);
        model.RadiationLossDb(8, 180).ShouldBe(quarter * 2, 1e-12);
        model.RadiationLossDb(8, -90).ShouldBe(quarter, 1e-12, "the turn direction does not matter");
    }

    [Fact]
    public void Radiation_FollowsTheExponentialForm()
    {
        var model = BendLossPresets.Generic;
        double r1 = 12, r2 = 15;

        double ratio = model.RadiationLossDbPer90Deg(r1) / model.RadiationLossDbPer90Deg(r2);

        ratio.ShouldBe(Math.Exp((r2 - r1) / model.RadiationDecayLengthMicrometers), 1e-9);
    }

    [Fact]
    public void Junction_GrowsWithTheSquareOfTheCurvatureStep()
    {
        var model = BendLossPresets.Generic;
        double straightToBend = model.JunctionLossDb(1.0 / 10);

        straightToBend.ShouldBe(model.JunctionLossDbAtReference, 1e-12, "R = R_jref is the reference point");
        model.JunctionLossDb(2.0 / 10).ShouldBe(4 * straightToBend, 1e-12, "an S-bend reversal doubles Δκ");
        model.JunctionLossDb(0).ShouldBe(0);
    }

    [Fact]
    public void Generic_ReproducesTheFormerConstantAtTheDefaultRadius()
    {
        BendLossPresets.Generic.CornerLossDb(10, 90).ShouldBe(0.05, 1e-3,
            "existing designs routed at the 10 µm default keep their loss");
    }

    [Fact]
    public void Generic_TightBendLosesFarMoreThanAGentleOne()
    {
        double tight = BendLossPresets.Generic.CornerLossDb(5, 90);
        double gentle = BendLossPresets.Generic.CornerLossDb(71, 90);

        tight.ShouldBeGreaterThan(0.3);
        gentle.ShouldBeLessThan(0.001);
        (tight / gentle).ShouldBeGreaterThan(100);
    }

    [Theory]
    [InlineData(WaveguideFamily.SoiStrip, 5.0, 0.005)]
    [InlineData(WaveguideFamily.SiliconNitride, 100.0, 0.005)]
    [InlineData(WaveguideFamily.InpShallowRib, 500.0, 0.005)]
    public void FamilyDefaults_AreNegligibleAtComfortableRadii(WaveguideFamily family, double radius, double maxDb)
    {
        BendLossPresets.ForFamily(family).CornerLossDb(radius, 90).ShouldBeLessThan(maxDb);
    }

    [Theory]
    [InlineData(WaveguideFamily.SiliconNitride, 30.0, 0.03)]
    [InlineData(WaveguideFamily.InpShallowRib, 150.0, 0.03)]
    [InlineData(WaveguideFamily.InpDeepEtch, 10.0, 0.03)]
    public void LowContrastFamilies_AreSignificantAtTheirSmallRadii(WaveguideFamily family, double radius, double minDb)
    {
        BendLossPresets.ForFamily(family).CornerLossDb(radius, 90).ShouldBeGreaterThan(minDb);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void BelowTheCalibratedRange_LossRisesSteeperThanTheFit(WaveguideFamily family)
    {
        var model = BendLossPresets.ForFamily(family);
        double rMin = Math.Max(model.CalibratedMinRadiusMicrometers, 0.5);
        double below = rMin * 0.6;
        double pureFit = model.RadiationLossDbPer90AtReference
            * Math.Exp(-(below - model.RadiationReferenceRadiusMicrometers) / model.RadiationDecayLengthMicrometers);

        model.RadiationLossDbPer90Deg(below).ShouldBeGreaterThan(Math.Min(pureFit, BendLossModel.MaxLossDb - 1e-9));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void DegenerateRadius_GivesTheCapNotNaN(double radius)
    {
        double loss = BendLossPresets.Generic.RadiationLossDbPer90Deg(radius);

        double.IsNaN(loss).ShouldBeFalse();
        loss.ShouldBeLessThanOrEqualTo(BendLossModel.MaxLossDb);
    }

    [Fact]
    public void VeryTightBend_IsCappedAndFinite()
    {
        var model = BendLossPresets.InpShallowRib;

        model.RadiationLossDbPer90Deg(0.01).ShouldBe(BendLossModel.MaxLossDb);
        model.JunctionLossDb(double.PositiveInfinity).ShouldBe(BendLossModel.MaxLossDb);
        model.CornerLossDb(0.01, 90).ShouldBeLessThanOrEqualTo(BendLossModel.MaxLossDb);
    }

    [Fact]
    public void None_HasNoBendLossAtAnyRadius()
    {
        BendLossPresets.None.CornerLossDb(1, 90).ShouldBe(0);
        BendLossPresets.None.JunctionLossDb(1).ShouldBe(0);
    }
}
