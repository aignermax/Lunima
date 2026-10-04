using CAP.Avalonia.Controls.HelpAnimations;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Pins the #1362 Optimization help animation to a deterministic frame state: the
/// scripted hill-climb is a pure function of the loop position, so a fixed t must
/// always yield the same marker position, best-so-far point, step size and verdict.
/// Static entry points only — no UI thread needed. Same pattern as
/// <see cref="LengthMatchArrivalAnimationTests"/>.
/// </summary>
public class HillClimbAnimationTests
{
    private const double Tolerance = 1e-12;

    /// <summary>The loop starts with the marker on the start point, best = start, full step.</summary>
    [Fact]
    public void FrameAt_LoopStart_SitsOnStartPointWithFullStep()
    {
        var frame = HillClimbAnimation.FrameAt(0);

        frame.CurrentT.ShouldBe(0.30, Tolerance);
        frame.BestT.ShouldBe(0.30, Tolerance);
        frame.Outcome.ShouldBe(HillClimbAnimation.StepOutcome.Moving);
        frame.StepFraction.ShouldBeGreaterThan(0);
    }

    /// <summary>Mid-travel of an uphill attempt: the marker is strictly between from and to.</summary>
    [Fact]
    public void FrameAt_MidTravel_MovesTowardProbe()
    {
        // First attempt (0.30 → 0.38), a quarter into its window.
        var frame = HillClimbAnimation.FrameAt(0.25 / AttemptCount);

        frame.CurrentT.ShouldBeGreaterThan(0.30);
        frame.CurrentT.ShouldBeLessThan(0.38);
        frame.Outcome.ShouldBe(HillClimbAnimation.StepOutcome.Moving);
        frame.BestT.ShouldBe(0.30, Tolerance, "the probe is not judged yet while travelling");
    }

    /// <summary>Once an uphill probe is judged, it becomes the new best point.</summary>
    [Fact]
    public void FrameAt_AcceptedProbe_BecomesNewBest()
    {
        // Fourth attempt (0.54 → 0.60, uphill), during the verdict hold.
        var frame = HillClimbAnimation.FrameAt((3 + 0.6) / AttemptCount);

        frame.Outcome.ShouldBe(HillClimbAnimation.StepOutcome.Accepted);
        frame.CurrentT.ShouldBe(0.60, Tolerance);
        frame.BestT.ShouldBe(0.60, Tolerance);
    }

    /// <summary>A non-improving probe flashes rejected and bounces back to the best point.</summary>
    [Fact]
    public void FrameAt_RejectedProbe_BouncesBackToBest()
    {
        // Fifth attempt (0.60 → 0.50, downhill): verdict hold, then settled back.
        var hold = HillClimbAnimation.FrameAt((4 + 0.6) / AttemptCount);
        hold.Outcome.ShouldBe(HillClimbAnimation.StepOutcome.Rejected);
        hold.CurrentT.ShouldBe(0.50, Tolerance);
        hold.BestT.ShouldBe(0.60, Tolerance, "a rejected probe never moves the best point");

        var settled = HillClimbAnimation.FrameAt((4 + 0.999) / AttemptCount);
        settled.Outcome.ShouldBe(HillClimbAnimation.StepOutcome.Rejected);
        settled.CurrentT.ShouldBe(0.60, 1e-3, "the marker must return to the best point");
    }

    /// <summary>The step size shrinks after a rejection and resets after an improvement.</summary>
    [Fact]
    public void FrameAt_StepSize_ShrinksAfterRejectionAndResetsAfterImprovement()
    {
        double beforeReject = HillClimbAnimation.FrameAt((4 + 0.1) / AttemptCount).StepFraction;
        double afterReject = HillClimbAnimation.FrameAt((5 + 0.1) / AttemptCount).StepFraction;
        double afterImprove = HillClimbAnimation.FrameAt((6 + 0.1) / AttemptCount).StepFraction;

        afterReject.ShouldBeLessThan(beforeReject);
        afterImprove.ShouldBe(beforeReject, Tolerance, "an improvement resets the step to full size");
    }

    /// <summary>The loop ends resting on the local peak with a shrunk step — never on the global one.</summary>
    [Fact]
    public void FrameAt_LoopEnd_RestsAtLocalBest()
    {
        var frame = HillClimbAnimation.FrameAt(0.999);

        frame.BestT.ShouldBe(0.63, Tolerance);
        frame.CurrentT.ShouldBe(0.63, 1e-3);
        frame.StepFraction.ShouldBeLessThan(
            HillClimbAnimation.FrameAt(0).StepFraction,
            "the final frame shows the shrunk step of the last rejected probes");
    }

    /// <summary>The curve really has a taller peak the climb never visits (local-best caveat).</summary>
    [Fact]
    public void Objective_LocalPeakStaysBelowGlobalPeak()
    {
        double localBest = HillClimbAnimation.Objective(0.63);
        double globalPeak = HillClimbAnimation.Objective(0.90);

        localBest.ShouldBeGreaterThan(HillClimbAnimation.Objective(0.60));
        globalPeak.ShouldBeGreaterThan(localBest);
    }

    /// <summary>The two screenshot showcase phases render visibly different frames.</summary>
    [Fact]
    public void ShowcasePhases_ProduceDistinctFrames()
    {
        var first = HillClimbAnimation.FrameAt(HillClimbAnimation.ShowcasePhases[0]);
        var second = HillClimbAnimation.FrameAt(HillClimbAnimation.ShowcasePhases[1]);

        first.ShouldNotBe(second);
    }

    private const double AttemptCount = 8;
}
