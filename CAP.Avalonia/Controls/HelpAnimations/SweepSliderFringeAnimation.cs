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
/// Self-contained scene of the Sweep tab help flyout (#1352): a slider knob travels
/// from Start to End while a dot traces the output-power curve point by point — for
/// an MZI that curve is a cos² fringe, so the dot falls from full power through the
/// quadrature point (half power) into the null and rises again. Knob and dot share
/// the same x axis, so the link "slider value → point on the curve" is read directly.
/// The faint full fringe stays in the background; the bright trace grows with the
/// sweep and keeps the finished curve visible on the last frame. The curve polyline
/// is generated from the same formula as the dot, so drawing and physics never drift.
/// Draws in the flyout's fixed 368×150 coordinate space like <see cref="MziFringeAnimation"/>.
/// </summary>
public class SweepSliderFringeAnimation : HelpAnimationBase
{
    /// <summary>Polyline samples of the fringe curve (and the traced prefix).</summary>
    private const int CurveSamples = 64;

    // Shared x axis: the slider track sits directly above the plot, so the knob is
    // always exactly over the curve point the current slider value produces.
    private const double AxisStartX = 40;
    private const double AxisEndX = 328;

    // Slider track row at the top of the scene.
    private const double TrackY = 30;
    private const double KnobWidth = 12;
    private const double KnobHeight = 18;

    // Plot strip: baseline = zero power, top = full power.
    private const double PlotTopY = 62;
    private const double PlotBaselineY = 128;

    private const double MarkerDiameter = 8;

    private static readonly IBrush TrackBrush = new ImmutableSolidColorBrush(0xFF8D8D8D);
    private static readonly IBrush KnobBrush = new ImmutableSolidColorBrush(0xFFCFCFD6);
    private static readonly IBrush AxisBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush GhostCurveBrush = new ImmutableSolidColorBrush(0x554FC3F7);
    private static readonly IBrush TraceBrush = new ImmutableSolidColorBrush(0xFF4FC3F7);
    private static readonly IBrush MarkerBrush = new ImmutableSolidColorBrush(0xFFFF8A65);
    private static readonly IBrush LinkBrush = new ImmutableSolidColorBrush(0x66CFCFD6);

    private readonly Rectangle _knob;
    private readonly Polyline _trace;
    private readonly Ellipse _marker;
    private readonly Line _link;

    /// <summary>Loop positions worth a screenshot: half power on the falling slope, rising past the null.</summary>
    public static double[] ShowcasePhases { get; } = { 0.25, 0.75 };

    /// <summary>Builds the static track/axes/ghost curve and the animated knob, trace and marker.</summary>
    public SweepSliderFringeAnimation()
    {
        _knob = new Rectangle
        {
            Fill = KnobBrush,
            Width = KnobWidth,
            Height = KnobHeight,
            RadiusX = 3,
            RadiusY = 3,
            IsHitTestVisible = false,
        };
        _trace = new Polyline
        {
            Stroke = TraceBrush,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _marker = new Ellipse
        {
            Fill = MarkerBrush,
            Width = MarkerDiameter,
            Height = MarkerDiameter,
            IsHitTestVisible = false,
        };
        _link = new Line
        {
            Stroke = LinkBrush,
            StrokeThickness = 1,
            StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 3, 3 },
            IsHitTestVisible = false,
        };

        var canvas = new AnimCanvas { IsHitTestVisible = false };
        canvas.Children.Add(BuildTrack());
        canvas.Children.Add(BuildBaseline());
        canvas.Children.Add(BuildFringeCurve(GhostCurveBrush, 1.5));
        canvas.Children.Add(_link);
        canvas.Children.Add(_trace);
        canvas.Children.Add(_knob);
        canvas.Children.Add(_marker);
        Content = canvas;
    }

    /// <summary>Output level at a sweep position: one cos² fringe, 1 = full power, 0 = null.</summary>
    internal static double OutputLevel(double progress) =>
        0.5 + 0.5 * Math.Cos(2.0 * Math.PI * progress);

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        double x = CurveX(progress);
        double y = CurveY(OutputLevel(progress));

        AnimCanvas.SetLeft(_knob, x - KnobWidth / 2);
        AnimCanvas.SetTop(_knob, TrackY - KnobHeight / 2);

        _link.StartPoint = new Point(x, TrackY + KnobHeight / 2);
        _link.EndPoint = new Point(x, y);

        _trace.Points = TracePoints(progress);
        AnimCanvas.SetLeft(_marker, x - MarkerDiameter / 2);
        AnimCanvas.SetTop(_marker, y - MarkerDiameter / 2);
    }

    private static Points TracePoints(double progress)
    {
        var points = new List<Point>();
        for (int i = 0; i <= CurveSamples; i++)
        {
            double t = (double)i / CurveSamples;
            if (t > progress)
                break;
            points.Add(new Point(CurveX(t), CurveY(OutputLevel(t))));
        }
        points.Add(new Point(CurveX(progress), CurveY(OutputLevel(progress))));
        return new Points(points);
    }

    private static Line BuildTrack() => new()
    {
        StartPoint = new Point(AxisStartX, TrackY),
        EndPoint = new Point(AxisEndX, TrackY),
        Stroke = TrackBrush,
        StrokeThickness = 3,
        StrokeLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
    };

    private static Line BuildBaseline() => new()
    {
        StartPoint = new Point(AxisStartX, PlotBaselineY),
        EndPoint = new Point(AxisEndX, PlotBaselineY),
        Stroke = AxisBrush,
        StrokeThickness = 1,
        IsHitTestVisible = false,
    };

    private static Polyline BuildFringeCurve(IBrush stroke, double thickness)
    {
        var points = new List<Point>();
        for (int i = 0; i <= CurveSamples; i++)
        {
            double t = (double)i / CurveSamples;
            points.Add(new Point(CurveX(t), CurveY(OutputLevel(t))));
        }
        return new Polyline
        {
            Points = points,
            Stroke = stroke,
            StrokeThickness = thickness,
            IsHitTestVisible = false,
        };
    }

    private static double CurveX(double t) => AxisStartX + (AxisEndX - AxisStartX) * t;

    private static double CurveY(double level) => PlotBaselineY - (PlotBaselineY - PlotTopY) * level;
}
