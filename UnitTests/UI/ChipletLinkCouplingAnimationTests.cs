using System.Globalization;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP_Core.Analysis;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Pins the #1247 help animation's readout to the simulation: the coupled fraction the
/// "Chiplet links" flyout shows is computed with the real
/// <see cref="ChipletEdgeCouplerCoupling.PowerCouplingForOffset"/> /
/// <see cref="ChipletEdgeCouplerCoupling.PowerCouplingForGap"/> at 1550 nm, so the help can
/// never drift from the simulation. Static entry points only — no UI thread needed.
/// </summary>
public class ChipletLinkCouplingAnimationTests
{
    private const double Tolerance = 1e-12;

    /// <summary>Butt-coupled phase: no gap, no offset, full coupling.</summary>
    [Fact]
    public void ButtCoupledPhase_CouplesEverything()
    {
        double phase = ChipletLinkCouplingAnimation.ShowcasePhases[0];

        ChipletLinkCouplingAnimation.GapMicrometersAt(phase).ShouldBe(0);
        ChipletLinkCouplingAnimation.OffsetMicrometersAt(phase).ShouldBe(0);
        ChipletLinkCouplingAnimation.PowerCouplingAt(phase).ShouldBe(1.0, Tolerance);
    }

    /// <summary>Gap phase: the readout equals the core gap-divergence function, nothing else.</summary>
    [Fact]
    public void GapPhase_ReadoutEqualsCoreGapFunction()
    {
        double phase = ChipletLinkCouplingAnimation.ShowcasePhases[1];

        ChipletLinkCouplingAnimation.GapMicrometersAt(phase)
            .ShouldBe(ChipletLinkCouplingAnimation.MaxGapMicrometers, Tolerance);
        ChipletLinkCouplingAnimation.OffsetMicrometersAt(phase).ShouldBe(0);
        ChipletLinkCouplingAnimation.PowerCouplingAt(phase).ShouldBe(
            ChipletEdgeCouplerCoupling.PowerCouplingForGap(
                ChipletLinkCouplingAnimation.MaxGapMicrometers,
                ChipletLinkCouplingAnimation.WavelengthNm),
            Tolerance);
    }

    /// <summary>Gap+offset phase: the readout equals the product of both core functions.</summary>
    [Fact]
    public void GapAndOffsetPhase_ReadoutEqualsCoreProduct()
    {
        double phase = ChipletLinkCouplingAnimation.ShowcasePhases[2];

        ChipletLinkCouplingAnimation.OffsetMicrometersAt(phase)
            .ShouldBe(ChipletLinkCouplingAnimation.MaxOffsetMicrometers, Tolerance);
        ChipletLinkCouplingAnimation.PowerCouplingAt(phase).ShouldBe(
            ChipletEdgeCouplerCoupling.PowerCouplingForOffset(
                ChipletLinkCouplingAnimation.MaxOffsetMicrometers)
            * ChipletEdgeCouplerCoupling.PowerCouplingForGap(
                ChipletLinkCouplingAnimation.MaxGapMicrometers,
                ChipletLinkCouplingAnimation.WavelengthNm),
            Tolerance);
    }

    /// <summary>The readout text renders η as an invariant-culture integer percentage.</summary>
    [Fact]
    public void ReadoutText_FormatsCouplingAsInvariantPercent()
    {
        foreach (var phase in ChipletLinkCouplingAnimation.ShowcasePhases)
        {
            var expected = string.Create(CultureInfo.InvariantCulture,
                $"η = {ChipletLinkCouplingAnimation.PowerCouplingAt(phase) * 100:0} %");
            ChipletLinkCouplingAnimation.ReadoutText(phase).ShouldBe(expected);
        }
    }

    /// <summary>Gap and offset grow monotonically and never overshoot their maxima.</summary>
    [Fact]
    public void GapAndOffset_GrowMonotonicallyWithinBounds()
    {
        var previousGap = 0.0;
        var previousOffset = 0.0;
        for (var p = 0.0; p <= 1.0; p += 0.05)
        {
            var gap = ChipletLinkCouplingAnimation.GapMicrometersAt(p);
            var offset = ChipletLinkCouplingAnimation.OffsetMicrometersAt(p);
            gap.ShouldBeGreaterThanOrEqualTo(previousGap);
            offset.ShouldBeGreaterThanOrEqualTo(previousOffset);
            gap.ShouldBeLessThanOrEqualTo(ChipletLinkCouplingAnimation.MaxGapMicrometers);
            offset.ShouldBeLessThanOrEqualTo(ChipletLinkCouplingAnimation.MaxOffsetMicrometers);
            previousGap = gap;
            previousOffset = offset;
        }
    }
}
