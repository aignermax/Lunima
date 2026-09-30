using System.Globalization;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP_Core.Analysis.LogicAnalysis;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Pins the #1256 help animation's readout to the simulation: the arrival-time gap the
/// Length Matching flyout shows is the group-delay relation Δt = ΔL·n_g/c evaluated with
/// the core's <see cref="GateDelayCalculator.DefaultGroupIndex"/> and
/// <see cref="GateDelayCalculator.SpeedOfLightMicrometersPerPicosecond"/> — the same
/// constants <see cref="WireDelayCalculator"/> applies to routed wires — never a
/// hard-coded number, so the help can never drift from the simulation. Static entry
/// points only — no UI thread needed.
/// </summary>
public class LengthMatchArrivalAnimationTests
{
    private const double Tolerance = 1e-12;

    /// <summary>Mismatched run: full length difference, meander not grown yet.</summary>
    [Fact]
    public void MismatchedPhase_CarriesFullMismatch()
    {
        double phase = LengthMatchArrivalAnimation.ShowcasePhases[0];

        LengthMatchArrivalAnimation.MeanderFillAt(phase).ShouldBe(0);
        LengthMatchArrivalAnimation.LengthMismatchMicrometersAt(phase)
            .ShouldBe(LengthMatchArrivalAnimation.MaxLengthMismatchMicrometers, Tolerance);
        LengthMatchArrivalAnimation.DelayPicosecondsAt(phase).ShouldBeGreaterThan(0);
    }

    /// <summary>Matched phase: the meander has closed the gap, the pulses arrive together.</summary>
    [Fact]
    public void MatchedPhase_MismatchAndDelayAreZero()
    {
        double phase = LengthMatchArrivalAnimation.ShowcasePhases[^1];

        LengthMatchArrivalAnimation.MeanderFillAt(phase).ShouldBe(1);
        LengthMatchArrivalAnimation.LengthMismatchMicrometersAt(phase).ShouldBe(0, Tolerance);
        LengthMatchArrivalAnimation.DelayPicosecondsAt(phase).ShouldBe(0, Tolerance);
    }

    /// <summary>The readout equals the core group-delay formula, nothing else.</summary>
    [Fact]
    public void DelayReadout_EqualsCoreGroupDelayFormula()
    {
        const double deltaLMicrometers = 200;
        var expected = deltaLMicrometers * GateDelayCalculator.DefaultGroupIndex
            / GateDelayCalculator.SpeedOfLightMicrometersPerPicosecond;

        LengthMatchArrivalAnimation.GroupDelayPicoseconds(deltaLMicrometers)
            .ShouldBe(expected, Tolerance);
        LengthMatchArrivalAnimation.DelayPicosecondsAt(0).ShouldBe(expected, Tolerance);
    }

    /// <summary>The readout text renders ΔL and Δt with invariant-culture formatting.</summary>
    [Fact]
    public void ReadoutText_FormatsMismatchAndDelayInvariant()
    {
        foreach (var phase in LengthMatchArrivalAnimation.ShowcasePhases)
        {
            var expected = string.Create(CultureInfo.InvariantCulture,
                $"ΔL = {LengthMatchArrivalAnimation.LengthMismatchMicrometersAt(phase):0} µm · " +
                $"Δt = {LengthMatchArrivalAnimation.DelayPicosecondsAt(phase):0.00} ps");
            LengthMatchArrivalAnimation.ReadoutText(phase).ShouldBe(expected);
        }
    }

    /// <summary>The meander grows monotonically and the mismatch never goes negative.</summary>
    [Fact]
    public void MeanderFill_GrowsMonotonicallyWithinBounds()
    {
        var previousFill = 0.0;
        for (var p = 0.0; p <= 1.0; p += 0.05)
        {
            var fill = LengthMatchArrivalAnimation.MeanderFillAt(p);
            fill.ShouldBeGreaterThanOrEqualTo(previousFill);
            fill.ShouldBeLessThanOrEqualTo(1.0);
            LengthMatchArrivalAnimation.LengthMismatchMicrometersAt(p)
                .ShouldBeGreaterThanOrEqualTo(0);
            previousFill = fill;
        }
    }
}
