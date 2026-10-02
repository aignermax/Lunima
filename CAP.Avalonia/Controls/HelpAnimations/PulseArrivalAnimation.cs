using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Logic panel's timeline help flyout (#1206): a chain of
/// three gates, each with a 0/1 badge, plus a waveform lane with one step trace per
/// gate, all driven by the loop's <see cref="HelpAnimationBase.Progress"/>. A light
/// pulse (the LightPulseAlongPath primitive, declared in the flyout) travels the
/// track above the gates; when it reaches a gate, that gate's badge flips to 1 and
/// its trace draws the rising step at the same instant — switch events have arrival
/// times, and the arrival time is the propagation delay. A white time cursor sweeps
/// the lane to tie the two views to the same moment. Badge and trace states are pure
/// functions of <see cref="HelpAnimationBase.Progress"/>, so a scrubbed frame is
/// always self-consistent. Panel-specific, not a reusable primitive — a transparent
/// overlay placed over the flyout's static diagram (track, gate boxes, lane border)
/// at the same size; it draws in the diagram's 368×124 coordinate space. The loop's
/// last frame keeps the all-arrived end state visible.
/// </summary>
public class PulseArrivalAnimation : HelpAnimationBase
{
    // Geometry inside the diagram's coordinate space. Must match the flyout's static
    // AXAML: the pulse track runs "8,40 344,40" inside window 0…0.75 and the three
    // gate boxes (40×28 at top 26) sit centered on x = 94, 184, 274.
    private const double TrackStartX = 8;
    private const double TrackEndX = 344;
    private const double PulseWindowEnd = 0.75;

    private static readonly double[] GateCenterX = { 94, 184, 274 };

    // Waveform lane: the static border in the flyout spans x 8…344, y 64…120; the
    // traces and the time cursor live inside it and map the loop fraction onto x.
    private const double LaneLeft = 16;
    private const double LaneWidth = 320;
    private const double LaneTop = 66;
    private const double LaneBottom = 118;
    private const double TraceStepHeight = 10;

    private static readonly double[] TraceBaseY = { 82, 98, 114 };

    private const double BadgeSize = 20;
    private const double BadgeTop = 2;

    private static readonly IBrush TraceBrush = new ImmutableSolidColorBrush(0xFF7FAAFF);
    private static readonly IBrush BadgeIdleBrush = new ImmutableSolidColorBrush(0xFF666666);
    private static readonly IBrush BadgeOneBrush = new ImmutableSolidColorBrush(0xFF81C784);

    private readonly Border[] _badges;
    private readonly TextBlock[] _badgeZeros;
    private readonly TextBlock[] _badgeOnes;
    private readonly Polyline[] _traces;
    private readonly Rectangle _cursor;

    /// <summary>Builds the badges, step traces, and time cursor at their fixed diagram positions.</summary>
    public PulseArrivalAnimation()
    {
        var canvas = new AnimCanvas { IsHitTestVisible = false };
        _badges = new Border[GateCenterX.Length];
        _badgeZeros = new TextBlock[GateCenterX.Length];
        _badgeOnes = new TextBlock[GateCenterX.Length];
        _traces = new Polyline[GateCenterX.Length];

        for (var i = 0; i < GateCenterX.Length; i++)
        {
            var (badge, zero, one) = CreateBadge(GateCenterX[i]);
            _badges[i] = badge;
            _badgeZeros[i] = zero;
            _badgeOnes[i] = one;
            canvas.Children.Add(badge);

            _traces[i] = new Polyline
            {
                Stroke = TraceBrush,
                StrokeThickness = 1.5,
                StrokeLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            canvas.Children.Add(_traces[i]);
        }

        _cursor = new Rectangle
        {
            Width = 1.5,
            Height = LaneBottom - LaneTop,
            Fill = Brushes.White,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetTop(_cursor, LaneTop);
        canvas.Children.Add(_cursor);

        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var now = LaneLeft + LaneWidth * progress;
        AnimCanvas.SetLeft(_cursor, now);

        for (var i = 0; i < GateCenterX.Length; i++)
        {
            var arrival = ArrivalFraction(GateCenterX[i]);
            var arrived = progress >= arrival;

            _badges[i].BorderBrush = arrived ? BadgeOneBrush : BadgeIdleBrush;
            _badgeOnes[i].IsVisible = arrived;
            _badgeZeros[i].IsVisible = !arrived;

            _traces[i].Points = TracePoints(i, arrival, arrived, now);
        }
    }

    /// <summary>Loop fraction at which the pulse reaches the gate at <paramref name="centerX"/>.</summary>
    private static double ArrivalFraction(double centerX) =>
        (centerX - TrackStartX) / (TrackEndX - TrackStartX) * PulseWindowEnd;

    private static Points TracePoints(int gateIndex, double arrival, bool arrived, double now)
    {
        var low = TraceBaseY[gateIndex];
        var high = low - TraceStepHeight;
        if (!arrived)
            return new Points { new Point(LaneLeft, low), new Point(now, low) };

        var flipX = LaneLeft + LaneWidth * arrival;
        return new Points
        {
            new Point(LaneLeft, low),
            new Point(flipX, low),
            new Point(flipX, high),
            new Point(now, high),
        };
    }

    private static (Border Badge, TextBlock Zero, TextBlock One) CreateBadge(double centerX)
    {
        var zero = new TextBlock
        {
            Text = "0",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var one = new TextBlock
        {
            Text = "1",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(0xFF90EE90),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var badge = new Border
        {
            Width = BadgeSize,
            Height = BadgeSize,
            CornerRadius = new CornerRadius(BadgeSize / 2),
            Background = new SolidColorBrush(0xFF101018),
            BorderBrush = BadgeIdleBrush,
            BorderThickness = new Thickness(1.5),
            Child = new Panel { Children = { zero, one } },
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(badge, centerX - BadgeSize / 2);
        AnimCanvas.SetTop(badge, BadgeTop);
        return (badge, zero, one);
    }
}
