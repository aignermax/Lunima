using CAP.Avalonia.Controls.HelpAnimations;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Pins the #1391 crossing help animation's teaching story: the pulse approaches both
/// junctions in lockstep, at the bare X a leak pulse peels off into the crossing arm
/// while the main pulse dims, and through the crossing component the pulse keeps full
/// brightness. Static entry points only — no UI thread needed.
/// </summary>
public class WaveguideCrossingHelpAnimationTests
{
    private const double Tolerance = 1e-12;

    /// <summary>Approach phase: pulses en route to the junctions, no leak yet.</summary>
    [Fact]
    public void ApproachPhase_PulseBeforeJunction_NoLeak()
    {
        double phase = WaveguideCrossingHelpAnimation.ShowcasePhases[0];

        WaveguideCrossingHelpAnimation.MainPulseXAt(phase, 0)
            .ShouldBeLessThan(WaveguideCrossingHelpAnimation.LeftJunctionX - 8);
        WaveguideCrossingHelpAnimation.LeakOpacityAt(phase).ShouldBe(0);
        WaveguideCrossingHelpAnimation.BareXMainOpacityAt(phase).ShouldBe(1.0, Tolerance);
    }

    /// <summary>Split phase: the leak pulse is climbing the crossing arm, the main pulse has dimmed.</summary>
    [Fact]
    public void SplitPhase_LeakPeelsOff_MainPulseDims()
    {
        double phase = WaveguideCrossingHelpAnimation.ShowcasePhases[1];

        WaveguideCrossingHelpAnimation.LeakOpacityAt(phase).ShouldBeGreaterThan(0);
        WaveguideCrossingHelpAnimation.BareXMainOpacityAt(phase)
            .ShouldBeLessThan(1.0 - Tolerance);
        WaveguideCrossingHelpAnimation.LeakPulseYAt(phase)
            .ShouldBeLessThan(52); // AxisY: the leak has left the junction upward
    }

    /// <summary>End phase: the full leak fraction is lost from the bare-X arm.</summary>
    [Fact]
    public void EndPhase_BareXLosesExactLeakFraction()
    {
        double phase = WaveguideCrossingHelpAnimation.ShowcasePhases[2];

        WaveguideCrossingHelpAnimation.LeakOpacityAt(phase).ShouldBe(1.0, Tolerance);
        WaveguideCrossingHelpAnimation.BareXMainOpacityAt(phase)
            .ShouldBe(1.0 - WaveguideCrossingHelpAnimation.LeakFraction, Tolerance);
    }

    /// <summary>Both panels' pulses advance monotonically and stay inside their panel.</summary>
    [Fact]
    public void MainPulse_AdvancesMonotonicallyWithinPanel()
    {
        var previous = 0.0;
        for (var p = 0.0; p <= 1.0; p += 0.05)
        {
            double x = WaveguideCrossingHelpAnimation.MainPulseXAt(p, 0);
            x.ShouldBeGreaterThanOrEqualTo(previous);
            x.ShouldBeLessThanOrEqualTo(160 + Tolerance); // PanelWidth
            previous = x;
        }
    }

    /// <summary>Both panels share the timeline: same relative position at every loop point.</summary>
    [Fact]
    public void BothPanels_MoveInLockstep()
    {
        for (var p = 0.0; p <= 1.0; p += 0.1)
        {
            double left = WaveguideCrossingHelpAnimation.MainPulseXAt(p, 0);
            double right = WaveguideCrossingHelpAnimation.MainPulseXAt(p, 168); // RightOriginX - LeftOriginX
            (right - 168).ShouldBe(left, Tolerance);
        }
    }
}
