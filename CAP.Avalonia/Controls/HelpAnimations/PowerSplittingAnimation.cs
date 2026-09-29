using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Logic panel's fan-out help flyout (#1216): one gate drives a
/// cascade of two 1×2 splitters — light does not multiply, so every split divides the
/// power. The travelling pulses (LightPulseAlongPath, declared in the flyout) dim with
/// each split (full → ½ → ¼ brightness); this overlay owns the verdicts: next to each
/// receiving gate a small power meter fills to the arriving power (½ or ¼) against a
/// dashed threshold line, and the gate's badge flips when the pulse arrives — "1" where
/// the meter stays above the threshold, "?" where a second split pushed the branch below
/// it and the bit is lost. Meter fill and badge state are pure functions of
/// <see cref="HelpAnimationBase.Progress"/>, so a scrubbed frame is always
/// self-consistent. Panel-specific, not a reusable primitive — a transparent overlay
/// placed over the flyout's static diagram (gate boxes, pulse tracks) at the same size;
/// it draws in the diagram's 368×122 coordinate space. The loop's last frame keeps the
/// all-arrived end state visible.
/// </summary>
public class PowerSplittingAnimation : HelpAnimationBase
{
    // Geometry inside the diagram's coordinate space. Must match the flyout's static
    // AXAML: the receiving gate boxes (40×28) sit at left 196 with row centers
    // y = 26, 65, 104, and the pulse tracks end at their left edges.
    private static readonly double[] GateCenterY = { 26, 65, 104 };

    // Received power per gate: the upper branch took one split (½), the two lower
    // branches took a second split (¼). A gate needs ThresholdPower to read a clean bit.
    private static readonly double[] GatePower = { 0.5, 0.25, 0.25 };
    private const double ThresholdPower = 0.35;

    // Loop fractions at which the pulses reach the gates — the WindowEnd values of the
    // LightPulseAlongPath instances in the flyout (½ branch: 0.42, ¼ branches: 0.58).
    private static readonly double[] ArrivalFraction = { 0.42, 0.58, 0.58 };
    private const double FillDuration = 0.08;
    private const double BadgeDelay = 0.06;

    private const double GateHalfHeight = 14;
    private const double BadgeSize = 20;
    private const double BadgeLeft = 246;

    private const double MeterLeft = 272;
    private const double MeterWidth = 6;
    private const double MeterHeight = 28;

    private static readonly Point[] SplitterCenters = { new(104, 65), new(150, 92) };
    private const double SplitterDiameter = 8;

    private static readonly IBrush TrackBrush = new SolidColorBrush(0xFF333340);
    private static readonly IBrush FillBrush = new SolidColorBrush(0xFFFFF176);
    private static readonly IBrush ThresholdBrush = new SolidColorBrush(0xFFAAAAAA);
    private static readonly IBrush SplitterBrush = new SolidColorBrush(0xFF5D5D8D);
    private static readonly IBrush BadgeIdleBrush = new SolidColorBrush(0xFF666666);
    private static readonly IBrush BadgeOkBrush = new SolidColorBrush(0xFF81C784);
    private static readonly IBrush BadgeLostBrush = new SolidColorBrush(0xFF8A6D1F);

    private readonly Rectangle[] _meterFills;
    private readonly Border[] _badges;
    private readonly TextBlock[] _badgeIdles;
    private readonly TextBlock[] _badgeResults;

    /// <summary>Builds the splitter markers, power meters, threshold lines, and badges.</summary>
    public PowerSplittingAnimation()
    {
        var canvas = new AnimCanvas { IsHitTestVisible = false };
        _meterFills = new Rectangle[GateCenterY.Length];
        _badges = new Border[GateCenterY.Length];
        _badgeIdles = new TextBlock[GateCenterY.Length];
        _badgeResults = new TextBlock[GateCenterY.Length];

        foreach (var center in SplitterCenters)
        {
            var dot = new Ellipse
            {
                Width = SplitterDiameter,
                Height = SplitterDiameter,
                Fill = SplitterBrush,
                IsHitTestVisible = false,
            };
            AnimCanvas.SetLeft(dot, center.X - SplitterDiameter / 2);
            AnimCanvas.SetTop(dot, center.Y - SplitterDiameter / 2);
            canvas.Children.Add(dot);
        }

        for (var i = 0; i < GateCenterY.Length; i++)
        {
            AddMeter(canvas, i);
            var (badge, idle, result) = CreateBadge(i);
            _badges[i] = badge;
            _badgeIdles[i] = idle;
            _badgeResults[i] = result;
            canvas.Children.Add(badge);
        }

        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        for (var i = 0; i < GateCenterY.Length; i++)
        {
            var arrived = progress >= ArrivalFraction[i] + BadgeDelay;
            _badgeIdles[i].IsVisible = !arrived;
            _badgeResults[i].IsVisible = arrived;
            _badges[i].BorderBrush = arrived ? ResultBrush(i) : BadgeIdleBrush;

            var fill = Ramp(progress, ArrivalFraction[i], ArrivalFraction[i] + FillDuration)
                       * GatePower[i] * MeterHeight;
            _meterFills[i].Height = fill;
            AnimCanvas.SetTop(_meterFills[i], MeterBottom(i) - fill);
        }
    }

    private static IBrush ResultBrush(int gateIndex) =>
        GatePower[gateIndex] >= ThresholdPower ? BadgeOkBrush : BadgeLostBrush;

    private static double MeterBottom(int gateIndex) => GateCenterY[gateIndex] + GateHalfHeight;

    private void AddMeter(AnimCanvas canvas, int gateIndex)
    {
        var top = GateCenterY[gateIndex] - GateHalfHeight;
        var track = new Rectangle
        {
            Width = MeterWidth,
            Height = MeterHeight,
            Fill = TrackBrush,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(track, MeterLeft);
        AnimCanvas.SetTop(track, top);
        canvas.Children.Add(track);

        _meterFills[gateIndex] = new Rectangle
        {
            Width = MeterWidth,
            Height = 0,
            Fill = FillBrush,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_meterFills[gateIndex], MeterLeft);
        AnimCanvas.SetTop(_meterFills[gateIndex], MeterBottom(gateIndex));
        canvas.Children.Add(_meterFills[gateIndex]);

        var thresholdY = MeterBottom(gateIndex) - ThresholdPower * MeterHeight;
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(MeterLeft - 3, thresholdY),
            EndPoint = new Point(MeterLeft + MeterWidth + 3, thresholdY),
            Stroke = ThresholdBrush,
            StrokeThickness = 1,
            StrokeDashArray = new AvaloniaList<double> { 3, 2 },
            IsHitTestVisible = false,
        });
    }

    private static (Border Badge, TextBlock Idle, TextBlock Result) CreateBadge(int gateIndex)
    {
        var keepsBit = GatePower[gateIndex] >= ThresholdPower;
        var idle = new TextBlock
        {
            Text = "0",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var result = new TextBlock
        {
            Text = keepsBit ? "1" : "?",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = keepsBit
                ? new SolidColorBrush(0xFF90EE90)
                : new SolidColorBrush(0xFFE6C860),
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
            Child = new Panel { Children = { idle, result } },
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(badge, BadgeLeft);
        AnimCanvas.SetTop(badge, GateCenterY[gateIndex] - BadgeSize / 2);
        return (badge, idle, result);
    }
}
