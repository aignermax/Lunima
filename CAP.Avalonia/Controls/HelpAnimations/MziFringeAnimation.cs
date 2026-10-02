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
/// Animated layer of the Wavelength Spectrum coherent-mode help flyout (#1333):
/// a stream of wavefronts travels both MZI arms — the meander arm is longer, so its
/// stream arrives later (the wave lags) — while the loop sweeps λ, the output glow
/// brightens and dims with the changing phase difference, and a marker rides the
/// fringe curve below. Brightness is cos² over <see cref="FringeCycles"/> cycles of
/// the loop, so peak and null frames are exact; the curve polyline is generated from
/// the same formula at construction, so drawing and physics can never drift apart.
/// Panel-specific overlay, not a reusable primitive: it draws in the flyout's fixed
/// 368×150 coordinate space over the static diagram (arms, splitter, labels).
/// </summary>
public class MziFringeAnimation : HelpAnimationBase
{
    /// <summary>Number of bright→dark→bright fringe cycles per loop (the λ sweep).</summary>
    public const int FringeCycles = 2;

    /// <summary>Wavefronts in flight per arm (spaced evenly along the path).</summary>
    private const int WavefrontCount = 3;

    private const double PulseDiameter = 8;
    private const double GlowMinDiameter = 6;
    private const double GlowMaxDiameter = 18;

    /// <summary>Straight upper arm of the flyout diagram.</summary>
    private static readonly IReadOnlyList<Point> UpperArm =
        new Point[] { new(76, 44), new(244, 44) };

    /// <summary>Meander lower arm — deliberately longer than the upper arm.</summary>
    private static readonly IReadOnlyList<Point> LowerArm = new Point[]
    {
        new(76, 92), new(100, 92), new(100, 116), new(124, 116), new(124, 92),
        new(148, 92), new(148, 116), new(172, 116), new(172, 92), new(196, 92),
        new(196, 116), new(220, 116), new(220, 92), new(244, 92),
    };

    // Fringe plot strip at the bottom of the diagram.
    private const double CurveStartX = 40;
    private const double CurveEndX = 330;
    private const double CurveTopY = 126;
    private const double CurveHeight = 16;

    // Output glow position (end of the static output waveguide).
    private const double GlowX = 336;
    private const double GlowY = 68;

    // Both streams move at the same spatial speed; the upper arm is shorter, so its
    // wavefronts complete more trips per loop — at any instant the meander wave lags.
    private const double UpperTripsPerLoop = 3.0;

    private static readonly IBrush PulseBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush GlowBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush CurveBrush = new ImmutableSolidColorBrush(0xFF4FC3F7);
    private static readonly IBrush MarkerBrush = new ImmutableSolidColorBrush(0xFFFF8A65);

    private readonly Ellipse[] _upperPulses;
    private readonly Ellipse[] _lowerPulses;
    private readonly Ellipse _glow;
    private readonly Ellipse _marker;

    /// <summary>Loop positions worth a screenshot: bright in-flight, deep null, bright peak.</summary>
    public static double[] ShowcasePhases { get; } = { 0.05, 0.25, 0.5 };

    /// <summary>Builds the pulses, the output glow, the fringe curve and its marker.</summary>
    public MziFringeAnimation()
    {
        _upperPulses = CreatePulses();
        _lowerPulses = CreatePulses();
        _glow = new Ellipse { Fill = GlowBrush, IsHitTestVisible = false };
        _marker = new Ellipse
        {
            Fill = MarkerBrush,
            Width = 7,
            Height = 7,
            IsHitTestVisible = false,
        };

        var canvas = new AnimCanvas { IsHitTestVisible = false };
        canvas.Children.Add(BuildFringeCurve());
        canvas.Children.AddRange(_upperPulses);
        canvas.Children.AddRange(_lowerPulses);
        canvas.Children.Add(_glow);
        canvas.Children.Add(_marker);
        Content = canvas;
    }

    /// <summary>Interference level at a loop position: 1 = constructive, 0 = cancelled.</summary>
    internal static double OutputLevel(double progress) =>
        0.5 + 0.5 * Math.Cos(2.0 * Math.PI * FringeCycles * progress);

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        double lowerTrips = LowerTripsPerLoop();
        for (int i = 0; i < WavefrontCount; i++)
        {
            double offset = (double)i / WavefrontCount;
            PlacePulse(_upperPulses[i], UpperArm, (progress * UpperTripsPerLoop + offset) % 1.0);
            PlacePulse(_lowerPulses[i], LowerArm, (progress * lowerTrips + offset) % 1.0);
        }

        double level = OutputLevel(progress);
        RenderGlow(level);
        RenderMarker(progress, level);
    }

    private static double LowerTripsPerLoop()
    {
        double upper = Length(UpperArm);
        double lower = Length(LowerArm);
        return UpperTripsPerLoop * upper / lower;
    }

    private void RenderGlow(double level)
    {
        var diameter = GlowMinDiameter + (GlowMaxDiameter - GlowMinDiameter) * level;
        _glow.Width = _glow.Height = diameter;
        _glow.Opacity = 0.25 + 0.75 * level;
        AnimCanvas.SetLeft(_glow, GlowX - diameter / 2);
        AnimCanvas.SetTop(_glow, GlowY - diameter / 2);
    }

    private void RenderMarker(double progress, double level)
    {
        AnimCanvas.SetLeft(_marker, CurveX(progress) - _marker.Width / 2);
        AnimCanvas.SetTop(_marker, CurveY(level) - _marker.Height / 2);
    }

    private Polyline BuildFringeCurve()
    {
        var points = new List<Point>();
        const int samples = 64;
        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples;
            points.Add(new Point(CurveX(t), CurveY(OutputLevel(t))));
        }
        return new Polyline
        {
            Points = points,
            Stroke = CurveBrush,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
    }

    private static double CurveX(double t) => CurveStartX + (CurveEndX - CurveStartX) * t;

    private static double CurveY(double level) => CurveTopY + CurveHeight * (1.0 - level);

    private static Ellipse[] CreatePulses()
    {
        var pulses = new Ellipse[WavefrontCount];
        for (int i = 0; i < pulses.Length; i++)
        {
            pulses[i] = new Ellipse
            {
                Fill = PulseBrush,
                Width = PulseDiameter,
                Height = PulseDiameter,
                Opacity = 0.85,
                IsHitTestVisible = false,
            };
        }
        return pulses;
    }

    private static void PlacePulse(Ellipse pulse, IReadOnlyList<Point> path, double t)
    {
        var position = PolylineInterpolation.PointAt(path, t);
        AnimCanvas.SetLeft(pulse, position.X - PulseDiameter / 2);
        AnimCanvas.SetTop(pulse, position.Y - PulseDiameter / 2);
    }

    private static double Length(IReadOnlyList<Point> points)
    {
        double total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            total += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        return total;
    }
}
