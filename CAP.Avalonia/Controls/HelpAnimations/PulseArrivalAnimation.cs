using System;
using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Logic timeline help flyout (#1206): three gate badges and a
/// waveform lane, driven by the loop's <see cref="HelpAnimationBase.Progress"/>. A
/// light pulse (the <see cref="LightPulseAlongPath"/> primitive in the flyout's AXAML)
/// travels a chain of three gates; each gate's badge flips 0 → 1 the moment the pulse
/// arrives, and the lane below draws a step at the same moment — switch events have
/// arrival times, and the arrival time is the propagation delay. Panel-specific overlay
/// (<see cref="LogicThresholdAnimation"/> style), not a reusable primitive: it draws in
/// the flyout diagram's 368×110 coordinate space, and badge state plus the step trace
/// are pure functions of Progress, so a scrubbed frame is always self-consistent.
/// </summary>
public class PulseArrivalAnimation : HelpAnimationBase
{
    // Diagram geometry (mirrored in the flyout's AXAML): the pulse track runs from
    // (12,40) to (356,40) inside cue window 0.05…0.80; the gate boxes are centered at
    // x 88 / 188 / 288; the badges sit above the gates; the waveform lane spans the
    // same x range, baseline y = 100, stepping up 5 px per arrived gate.
    private const double PulseWindowStart = 0.05;
    private const double PulseWindowEnd = 0.80;
    private const double TrackStartX = 12;
    private const double TrackEndX = 356;
    private const double BadgeTop = 2;
    private const double BadgeSize = 22;
    private const double LaneBaseY = 100;
    private const double LaneStepHeight = 5;

    private static readonly double[] GateCenterX = { 88, 188, 288 };

    private static readonly IBrush BadgeIdleBrush = new SolidColorBrush(0xFF666666);
    private static readonly IBrush BadgeOneBrush = new SolidColorBrush(0xFF81C784);
    private static readonly IBrush BadgeZeroForeground = new SolidColorBrush(0xFF888888);
    private static readonly IBrush BadgeOneForeground = new SolidColorBrush(0xFF90EE90);
    private static readonly IBrush LaneAxisBrush = new SolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush StepTraceBrush = new SolidColorBrush(0xFFFFF176);

    private readonly Border[] _badges = new Border[GateCenterX.Length];
    private readonly TextBlock[] _badgeTexts = new TextBlock[GateCenterX.Length];
    private readonly Polyline _stepTrace;

    /// <summary>Builds the badges, the lane axis, and the step trace at their fixed positions.</summary>
    public PulseArrivalAnimation()
    {
        var canvas = new AnimCanvas { IsHitTestVisible = false };

        canvas.Children.Add(new Line
        {
            StartPoint = new Point(TrackStartX, LaneBaseY),
            EndPoint = new Point(TrackEndX, LaneBaseY),
            Stroke = LaneAxisBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });

        _stepTrace = new Polyline
        {
            Stroke = StepTraceBrush,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(_stepTrace);

        for (var i = 0; i < GateCenterX.Length; i++)
        {
            _badgeTexts[i] = new TextBlock
            {
                Text = "0",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = BadgeZeroForeground,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            _badges[i] = new Border
            {
                Width = BadgeSize,
                Height = BadgeSize,
                CornerRadius = new CornerRadius(BadgeSize / 2),
                Background = new SolidColorBrush(0xFF101018),
                BorderBrush = BadgeIdleBrush,
                BorderThickness = new Thickness(1.5),
                Child = _badgeTexts[i],
                IsHitTestVisible = false,
            };
            AnimCanvas.SetLeft(_badges[i], GateCenterX[i] - BadgeSize / 2);
            AnimCanvas.SetTop(_badges[i], BadgeTop);
            canvas.Children.Add(_badges[i]);
        }

        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var laneX = TrackStartX + (TrackEndX - TrackStartX) * Ramp(progress, PulseWindowStart, PulseWindowEnd);
        var points = new Points { new Point(TrackStartX, LaneBaseY) };
        var y = LaneBaseY;

        for (var i = 0; i < GateCenterX.Length; i++)
        {
            var arrived = progress >= ArrivalProgress(i);
            _badgeTexts[i].Text = arrived ? "1" : "0";
            _badgeTexts[i].Foreground = arrived ? BadgeOneForeground : BadgeZeroForeground;
            _badges[i].BorderBrush = arrived ? BadgeOneBrush : BadgeIdleBrush;
            if (!arrived)
                break;
            var stepX = Math.Min(GateCenterX[i], laneX);
            points.Add(new Point(stepX, y));
            y -= LaneStepHeight;
            points.Add(new Point(stepX, y));
        }

        points.Add(new Point(laneX, y));
        _stepTrace.Points = points;
    }

    /// <summary>Loop fraction at which the pulse reaches gate <paramref name="index"/>'s center.</summary>
    private static double ArrivalProgress(int index)
    {
        var alongTrack = (GateCenterX[index] - TrackStartX) / (TrackEndX - TrackStartX);
        return PulseWindowStart + (PulseWindowEnd - PulseWindowStart) * alongTrack;
    }
}
