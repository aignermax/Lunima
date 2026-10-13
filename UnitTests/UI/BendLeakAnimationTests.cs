using CAP.Avalonia.Controls.HelpAnimations;
using CAP_Core.LightCalculation.BendLoss;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The insertion-loss help animation's readouts come from the real bend-loss model, so the
/// help can never drift from what the simulation computes.
/// </summary>
public class BendLeakAnimationTests
{
    [Fact]
    public void Readouts_UseTheGenericModel()
    {
        BendLeakAnimation.CornerLossDb(BendLeakAnimation.TightRadiusMicrometers)
            .ShouldBe(BendLossPresets.Generic.CornerLossDb(BendLeakAnimation.TightRadiusMicrometers, 90));
        BendLeakAnimation.ReadoutText(5).ShouldBe("R = 5 µm → 0.441 dB");
    }

    [Fact]
    public void TightBend_ReadsFarLossierThanTheGentleOne()
    {
        double tight = BendLeakAnimation.CornerLossDb(BendLeakAnimation.TightRadiusMicrometers);
        double gentle = BendLeakAnimation.CornerLossDb(BendLeakAnimation.GentleRadiusMicrometers);

        tight.ShouldBeGreaterThan(gentle * 50);
    }
}
