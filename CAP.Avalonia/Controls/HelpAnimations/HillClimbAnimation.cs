using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Self-contained scene of the Optimization tab help flyout (#1362): a 1-D objective
/// curve (target metric vs. one slider) with a marker that takes scripted hill-climb
/// steps, mirroring <c>CircuitOptimizer</c> — an uphill step is kept (the green
/// best-so-far dot moves up), a non-improving step flashes red, bounces back to the
/// best point and the step-size bracket under the curve shrinks. The curve has a
/// taller peak the climb never visits, so the "local best" caveat is visible, not
/// just stated. The whole frame is a pure function of <see cref="HelpAnimationBase.Progress"/>
/// via <see cref="FrameAt"/>, so tests and screenshots pin an exact frame.
/// Draws in the flyout's fixed 368×150 coordinate space like <see cref="SweepSliderFringeAnimation"/>.
/// </summary>
public class HillClimbAnimation : HelpAnimationBase
{
    /// <summary>Loop positions worth a screenshot: mid-climb with a wide step, local best reached with a shrunk step.</summary>
    public static double[] ShowcasePhases { get; } = { 0.28, 0.99 };

    /// <summary>What one attempted step did — drives the marker color.</summary>
    internal enum StepOutcome
    {
        /// <summary>The marker is still travelling toward the probe point.</summary>
        Moving,

        /// <summary>The probe improved the score and is kept (new best).</summary>
        Accepted,

        /// <summary>The probe did not improve; the marker bounces back and the step shrinks.</summary>
        Rejected,
    }

    /// <summary>Deterministic frame state at a loop position (test seam, InternalsVisibleTo UnitTests).</summary>
    internal readonly record struct HillClimbFrame(
        double CurrentT,
        double BestT,
        double StepFraction,
        StepOutcome Outcome);

    private readonly record struct Attempt(double FromT, double ToT);

    // Scripted climb up the local peak at t = 0.62: four uphill steps, one overshoot
    // that is rejected, a small uphill step onto the peak, two rejected probes that
    // shrink the step. Accepted/rejected is derived from Objective(), never scripted,
    // so the scene cannot contradict the curve. The taller peak at t = 0.90 stays
    // unvisited — that is the "local best" the help text talks about.
    private static readonly Attempt[] Attempts =
    {
        new(0.30, 0.38),
        new(0.38, 0.46),
        new(0.46, 0.54),
        new(0.54, 0.60),
        new(0.60, 0.50),
        new(0.60, 0.63),
        new(0.63, 0.70),
        new(0.63, 0.60),
    };

    // Illustrative step sizes following the CircuitOptimizer rule: reset after an
    // improvement, decay after a non-improving probe (never below the floor).
    private const double InitialStepFraction = 0.08;
    private const double StepDecayFactor = 0.5;
    private const double MinStepFraction = 0.01;

    // Window phases: travel toward the probe, hold (verdict flash), settle/bounce back.
    private const double TravelEnd = 0.5;
    private const double HoldEnd = 0.65;

    // Objective curve shape: broad shoulder + local peak at LocalPeakT + global peak.
    private const double BaselineLevel = 0.05;
    private const double ShoulderAmplitude = 0.30;
    private const double ShoulderCenterT = 0.66;
    private const double ShoulderWidthT = 0.55;
    private const double LocalPeakAmplitude = 0.72;
    private const double LocalPeakT = 0.62;
    private const double LocalPeakWidthT = 0.09;
    private const double GlobalPeakAmplitude = 1.00;
    private const double GlobalPeakT = 0.90;
    private const double GlobalPeakWidthT = 0.05;
    private const int CurveSamples = 96;

    private static readonly double MaxLevel = SampleMaxLevel();
    private static readonly double[] StepUsed = ComputeStepSizes();

    private const double AxisStartX = 30;
    private const double AxisEndX = 340;
    private const double PlotTopY = 16;
    private const double PlotBaselineY = 118;
    private const double StepTrackY = 136;
    private const double StepBarMaxHalfWidth = 45;
    private const double MarkerDiameter = 9;
    private const double BestHaloDiameter = 15;

    private static readonly IBrush AxisBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush GhostCurveBrush = new ImmutableSolidColorBrush(0x554FC3F7);
    private static readonly IBrush MarkerBrush = new ImmutableSolidColorBrush(0xFFFF8A65);
    private static readonly IBrush RejectBrush = new ImmutableSolidColorBrush(0xFFE57373);
    private static readonly IBrush BestBrush = new ImmutableSolidColorBrush(0xFF81C784);
    private static readonly IBrush StepBarBrush = new ImmutableSolidColorBrush(0xFFCFCFD6);

    private readonly Ellipse _bestHalo;
    private readonly Ellipse _marker;
    private readonly Line _stepBar;

    /// <summary>Builds the static curve/axes/track and the animated markers and step bar.</summary>
    public HillClimbAnimation()
    {
        _bestHalo = new Ellipse
        {
            Fill = BestBrush,
            Width = BestHaloDiameter,
            Height = BestHaloDiameter,
            IsHitTestVisible = false,
        };
        _marker = new Ellipse
        {
            Fill = MarkerBrush,
            Width = MarkerDiameter,
            Height = MarkerDiameter,
            IsHitTestVisible = false,
        };
        _stepBar = new Line
        {
            Stroke = StepBarBrush,
            StrokeThickness = 4,
            StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };

        var canvas = new AnimCanvas { IsHitTestVisible = false };
        canvas.Children.Add(BuildBaseline());
        canvas.Children.Add(BuildCurve());
        canvas.Children.Add(BuildStepTrack());
        canvas.Children.Add(_stepBar);
        canvas.Children.Add(_bestHalo);
        canvas.Children.Add(_marker);
        Content = canvas;
    }

    /// <summary>Objective value at a slider position (0…1): shoulder + local + global peak.</summary>
    internal static double Objective(double t) =>
        BaselineLevel
        + ShoulderAmplitude * Gaussian(t, ShoulderCenterT, ShoulderWidthT)
        + LocalPeakAmplitude * Gaussian(t, LocalPeakT, LocalPeakWidthT)
        + GlobalPeakAmplitude * Gaussian(t, GlobalPeakT, GlobalPeakWidthT);

    /// <summary>Frame state at a loop position (0 = first frame, 1 = last frame).</summary>
    internal static HillClimbFrame FrameAt(double progress)
    {
        double scaled = Math.Clamp(progress, 0.0, 1.0) * Attempts.Length;
        int index = Math.Min((int)scaled, Attempts.Length - 1);
        double local = scaled - index;

        var attempt = Attempts[index];
        bool accepted = Objective(attempt.ToT) > Objective(attempt.FromT);
        double bestT = accepted && local >= TravelEnd ? attempt.ToT : attempt.FromT;

        if (local < TravelEnd)
        {
            double t = Lerp(attempt.FromT, attempt.ToT, Smooth(local / TravelEnd));
            return new HillClimbFrame(t, bestT, StepUsed[index], StepOutcome.Moving);
        }

        if (local < HoldEnd)
        {
            var verdict = accepted ? StepOutcome.Accepted : StepOutcome.Rejected;
            return new HillClimbFrame(attempt.ToT, bestT, StepUsed[index], verdict);
        }

        double settle = Smooth((local - HoldEnd) / (1.0 - HoldEnd));
        double settledT = accepted ? attempt.ToT : Lerp(attempt.ToT, attempt.FromT, settle);
        var outcome = accepted ? StepOutcome.Accepted : StepOutcome.Rejected;
        return new HillClimbFrame(settledT, bestT, StepUsed[index], outcome);
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var frame = FrameAt(progress);

        double markerX = CurveX(frame.CurrentT);
        double markerY = CurveY(Objective(frame.CurrentT));
        AnimCanvas.SetLeft(_marker, markerX - MarkerDiameter / 2);
        AnimCanvas.SetTop(_marker, markerY - MarkerDiameter / 2);
        _marker.Fill = frame.Outcome == StepOutcome.Rejected ? RejectBrush : MarkerBrush;

        double bestX = CurveX(frame.BestT);
        double bestY = CurveY(Objective(frame.BestT));
        AnimCanvas.SetLeft(_bestHalo, bestX - BestHaloDiameter / 2);
        AnimCanvas.SetTop(_bestHalo, bestY - BestHaloDiameter / 2);

        double halfWidth = StepBarMaxHalfWidth * frame.StepFraction / InitialStepFraction;
        _stepBar.StartPoint = new Point(bestX - halfWidth, StepTrackY);
        _stepBar.EndPoint = new Point(bestX + halfWidth, StepTrackY);
    }

    private static double[] ComputeStepSizes()
    {
        var sizes = new double[Attempts.Length];
        double step = InitialStepFraction;
        for (int i = 0; i < Attempts.Length; i++)
        {
            sizes[i] = step;
            bool accepted = Objective(Attempts[i].ToT) > Objective(Attempts[i].FromT);
            step = accepted
                ? InitialStepFraction
                : Math.Max(MinStepFraction, step * StepDecayFactor);
        }
        return sizes;
    }

    private static double SampleMaxLevel()
    {
        double max = 0;
        for (int i = 0; i <= CurveSamples; i++)
            max = Math.Max(max, Objective((double)i / CurveSamples));
        return max;
    }

    private static Line BuildBaseline() => new()
    {
        StartPoint = new Point(AxisStartX, PlotBaselineY),
        EndPoint = new Point(AxisEndX, PlotBaselineY),
        Stroke = AxisBrush,
        StrokeThickness = 1,
        IsHitTestVisible = false,
    };

    private static Line BuildStepTrack() => new()
    {
        StartPoint = new Point(AxisStartX, StepTrackY),
        EndPoint = new Point(AxisEndX, StepTrackY),
        Stroke = AxisBrush,
        StrokeThickness = 1,
        IsHitTestVisible = false,
    };

    private static Polyline BuildCurve()
    {
        var points = new List<Point>();
        for (int i = 0; i <= CurveSamples; i++)
        {
            double t = (double)i / CurveSamples;
            points.Add(new Point(CurveX(t), CurveY(Objective(t))));
        }
        return new Polyline
        {
            Points = points,
            Stroke = GhostCurveBrush,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
    }

    private static double Gaussian(double t, double center, double width)
    {
        double d = (t - center) / width;
        return Math.Exp(-d * d);
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

    private static double CurveX(double t) => AxisStartX + (AxisEndX - AxisStartX) * t;

    private static double CurveY(double level) =>
        PlotBaselineY - (PlotBaselineY - PlotTopY) * (level / MaxLevel);
}
