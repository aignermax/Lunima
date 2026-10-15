using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CAP_Core.LightCalculation.BendLoss;
using AnimCanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Looping diagram of the insertion-loss help: two light pulses enter a 90° bend at the same
/// time — a tight one (left) and a gentle one (right). At the tight bend light keeps going
/// straight and leaks out of the curve (red sparks) and the pulse leaves dimmer; the gentle
/// bend guides the pulse around unharmed. The readouts are computed from the real
/// <see cref="BendLossPresets.Generic"/> model (<see cref="BendLossModel.CornerLossDb"/>), never
/// hard-coded, so the help cannot drift from the simulation. A pure function of
/// <see cref="HelpAnimationBase.Progress"/>; the dimming is exaggerated for visibility while
/// the numbers stay exact.
/// </summary>
public class BendLeakAnimation : HelpAnimationBase
{
    /// <summary>Size of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;
    public const double SceneHeight = 120;

    /// <summary>Radius (µm) the tight bend stands for in the readout.</summary>
    public const double TightRadiusMicrometers = 5;

    /// <summary>Radius (µm) the gentle bend stands for in the readout.</summary>
    public const double GentleRadiusMicrometers = 20;

    /// <summary>Loop positions of the showcase phases: approaching / leaking / arrived.</summary>
    public static readonly double[] ShowcasePhases = { 0.25, 0.7, 0.95 };

    private const double QuarterTurnDegrees = 90;
    private const double PulseDiameter = 9;
    private const double SparkDiameter = 5;
    private const double SparkTravelPx = 44;
    private const double DimmedOpacity = 0.35;
    private const int ArcSamples = 12;

    // Both tracks: straight in along y = 24, a 90° bend down, straight out to the bottom.
    private static readonly Points TightTrack = BuildTrack(16, 120, 6);
    private static readonly Points GentleTrack = BuildTrack(184, 250, 44);
    private static readonly double TightBendFraction = FractionAtCorner(TightTrack, 120, 6);
    private static readonly double GentleBendFraction = FractionAtCorner(GentleTrack, 250, 44);

    private static readonly IBrush TrackBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush PulseBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush SparkBrush = new ImmutableSolidColorBrush(0xFFE57373);
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(0xFFCFCFD6);

    private readonly Ellipse _tightPulse;
    private readonly Ellipse _gentlePulse;
    private readonly Ellipse[] _sparks;

    /// <summary>Builds both tracks, pulses, leak sparks and the model-driven readouts.</summary>
    public BendLeakAnimation()
    {
        var canvas = new AnimCanvas { Width = SceneWidth, Height = SceneHeight, IsHitTestVisible = false };
        _tightPulse = CreateDot(PulseDiameter, PulseBrush);
        _gentlePulse = CreateDot(PulseDiameter, PulseBrush);
        _sparks = Enumerable.Range(0, 3).Select(_ => CreateDot(SparkDiameter, SparkBrush)).ToArray();

        canvas.Children.Add(CreateTrack(TightTrack));
        canvas.Children.Add(CreateTrack(GentleTrack));
        canvas.Children.AddRange(_sparks);
        canvas.Children.Add(_tightPulse);
        canvas.Children.Add(_gentlePulse);
        canvas.Children.Add(CreateLabel(ReadoutText(TightRadiusMicrometers), 4, 100));
        canvas.Children.Add(CreateLabel(ReadoutText(GentleRadiusMicrometers), 176, 100));

        Content = canvas;
        RenderFrame(0);
    }

    /// <summary>Loss (dB) of one 90° corner at <paramref name="radiusMicrometers"/> in the generic model.</summary>
    public static double CornerLossDb(double radiusMicrometers) =>
        BendLossPresets.Generic.CornerLossDb(radiusMicrometers, QuarterTurnDegrees);

    /// <summary>The readout under a bend, e.g. "R = 5 µm → 0.44 dB" (invariant culture).</summary>
    public static string ReadoutText(double radiusMicrometers) =>
        string.Create(CultureInfo.InvariantCulture,
            $"R = {radiusMicrometers:0} µm → {CornerLossDb(radiusMicrometers):0.###} dB");

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        PlacePulse(_tightPulse, TightTrack, progress, TightBendFraction, DimmedOpacity);
        PlacePulse(_gentlePulse, GentleTrack, progress, GentleBendFraction, 1.0);
        PlaceSparks(progress);
    }

    private static void PlacePulse(Ellipse pulse, Points track, double progress, double bendFraction, double opacityAfterBend)
    {
        var position = PolylineInterpolation.PointAt(track, progress);
        AnimCanvas.SetLeft(pulse, position.X - PulseDiameter / 2);
        AnimCanvas.SetTop(pulse, position.Y - PulseDiameter / 2);
        pulse.Opacity = progress < bendFraction ? 1.0 : opacityAfterBend;
    }

    /// <summary>Sparks fly out of the tight corner along the straight-ahead direction, fading as they go.</summary>
    private void PlaceSparks(double progress)
    {
        double travel = Ramp(progress, TightBendFraction, TightBendFraction + 0.16);
        var corner = PolylineInterpolation.PointAt(TightTrack, TightBendFraction);
        for (int i = 0; i < _sparks.Length; i++)
        {
            double angle = -0.3 * i;
            var spark = _sparks[i];
            spark.IsVisible = travel > 0 && travel < 1;
            spark.Opacity = 1 - 0.6 * travel;
            AnimCanvas.SetLeft(spark, corner.X + Math.Cos(angle) * SparkTravelPx * travel - SparkDiameter / 2);
            AnimCanvas.SetTop(spark, corner.Y + Math.Sin(angle) * SparkTravelPx * travel - SparkDiameter / 2);
        }
    }

    /// <summary>Straight in at y = 24 to the corner, a quarter arc of <paramref name="radiusPx"/>, straight down.</summary>
    private static Points BuildTrack(double startX, double cornerX, double radiusPx)
    {
        const double y = 24;
        const double bottom = 92;
        var points = new Points { new(startX, y), new(cornerX - radiusPx, y) };
        for (int i = 1; i <= ArcSamples; i++)
        {
            double a = Math.PI / 2 * i / ArcSamples;
            points.Add(new Point(cornerX - radiusPx + radiusPx * Math.Sin(a), y + radiusPx - radiusPx * Math.Cos(a)));
        }
        points.Add(new Point(cornerX, bottom));
        return points;
    }

    /// <summary>Length fraction of the track at the middle of its bend.</summary>
    private static double FractionAtCorner(Points track, double cornerX, double radiusPx)
    {
        double straight = cornerX - radiusPx - track[0].X;
        double arc = Math.PI / 2 * radiusPx;
        double total = straight + arc + (track[^1].Y - (track[0].Y + radiusPx));
        return (straight + arc / 2) / total;
    }

    private static Polyline CreateTrack(Points points) => new()
    {
        Points = points, Stroke = TrackBrush, StrokeThickness = 3,
        StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false,
    };

    private static Ellipse CreateDot(double diameter, IBrush brush) => new()
    {
        Width = diameter, Height = diameter, Fill = brush, IsHitTestVisible = false,
    };

    private static TextBlock CreateLabel(string text, double left, double top) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.Bold,
        Foreground = LabelBrush,
        IsHitTestVisible = false,
        [AnimCanvas.LeftProperty] = left,
        [AnimCanvas.TopProperty] = top,
    };
}
