using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Logic panel's fan-out help flyout (#1216): the badge and
/// threshold gauge of each receiving gate, driven by the loop's
/// <see cref="HelpAnimationBase.Progress"/>. The flyout's static diagram shows one
/// gate feeding a 1×2 splitter whose two branches each feed a receiving gate and
/// split again into four leaves; LightPulseAlongPath pulses (declared in the flyout)
/// dim per split — brightness stands for optical power (1, 1/2, 1/4). Next to every
/// receiving gate this overlay draws a dashed threshold line with the badge above it
/// (half-power branches) or below it (quarter-power branches): when the pulse
/// arrives, a half-power badge flips to a green 1 (bit survives), a quarter-power
/// badge flips to a dim "?" (below the switching threshold — the bit is lost).
/// Badge states are pure functions of <see cref="HelpAnimationBase.Progress"/>, so a
/// scrubbed frame is always self-consistent. Panel-specific, not a reusable
/// primitive — a transparent overlay placed over the flyout's static diagram at the
/// same size; it draws in the diagram's 368×168 coordinate space. The loop's last
/// frame keeps the all-arrived end state visible.
/// </summary>
public class PowerSplitAnimation : HelpAnimationBase
{
    // Geometry inside the diagram's coordinate space. Must match the flyout's static
    // AXAML and the pulse cue windows: half-power pulses arrive at WindowEnd 0.55,
    // quarter-power pulses at WindowEnd 0.85.
    private const double Level1Arrival = 0.55;
    private const double Level2Arrival = 0.85;

    private const double BadgeSize = 16;
    private const double BadgeOffsetAboveLine = 11;
    private const double BadgeOffsetBelowLine = 15;
    private const double Level1BadgeCenterX = 156;
    private const double Level2BadgeCenterX = 302;
    private const double Level1LineLeft = 146;
    private const double Level1LineRight = 166;
    private const double Level2LineLeft = 290;
    private const double Level2LineRight = 314;

    // Level-1 gauges sit above their gate (the space beside them is the second
    // splitter); level-2 gauges sit right of their gate, badge below the line.
    private static readonly double[] Level1LineY = { 20, 100 };
    private static readonly double[] Level2LineY = { 14, 62, 98, 144 };

    private static readonly IBrush ThresholdBrush = new SolidColorBrush(0xFF8A8A8A);
    private static readonly IBrush BadgeIdleBrush = new SolidColorBrush(0xFF666666);
    private static readonly IBrush BadgeOneBrush = new SolidColorBrush(0xFF81C784);
    private static readonly IBrush BadgeUnknownBrush = new SolidColorBrush(0xFF9A8A4A);

    private readonly Border[] _level1Badges = new Border[Level1LineY.Length];
    private readonly TextBlock[] _level1Texts = new TextBlock[Level1LineY.Length];
    private readonly Border[] _level2Badges = new Border[Level2LineY.Length];
    private readonly TextBlock[] _level2Texts = new TextBlock[Level2LineY.Length];

    /// <summary>Builds the threshold lines and badges at their fixed diagram positions.</summary>
    public PowerSplitAnimation()
    {
        var canvas = new AnimCanvas { IsHitTestVisible = false };

        for (var i = 0; i < Level1LineY.Length; i++)
        {
            canvas.Children.Add(CreateThresholdLine(Level1LineLeft, Level1LineRight, Level1LineY[i]));
            var (badge, text) = CreateBadge(Level1BadgeCenterX, Level1LineY[i] - BadgeOffsetAboveLine);
            _level1Badges[i] = badge;
            _level1Texts[i] = text;
            canvas.Children.Add(badge);
        }

        for (var i = 0; i < Level2LineY.Length; i++)
        {
            canvas.Children.Add(CreateThresholdLine(Level2LineLeft, Level2LineRight, Level2LineY[i]));
            var (badge, text) = CreateBadge(Level2BadgeCenterX, Level2LineY[i] + BadgeOffsetBelowLine);
            _level2Badges[i] = badge;
            _level2Texts[i] = text;
            canvas.Children.Add(badge);
        }

        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var level1Arrived = progress >= Level1Arrival;
        for (var i = 0; i < _level1Badges.Length; i++)
            SetBadge(_level1Badges[i], _level1Texts[i], level1Arrived, "1", BadgeOneBrush);

        var level2Arrived = progress >= Level2Arrival;
        for (var i = 0; i < _level2Badges.Length; i++)
            SetBadge(_level2Badges[i], _level2Texts[i], level2Arrived, "?", BadgeUnknownBrush);
    }

    private static void SetBadge(Border badge, TextBlock text, bool arrived, string arrivedText, IBrush arrivedBrush)
    {
        badge.BorderBrush = arrived ? arrivedBrush : BadgeIdleBrush;
        text.Text = arrived ? arrivedText : "0";
        text.Foreground = arrived ? arrivedBrush : Brushes.Gray;
    }

    private static Line CreateThresholdLine(double left, double right, double y) =>
        new()
        {
            StartPoint = new Point(left, y),
            EndPoint = new Point(right, y),
            Stroke = ThresholdBrush,
            StrokeThickness = 1,
            StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 3, 2 },
            IsHitTestVisible = false,
        };

    private static (Border Badge, TextBlock Text) CreateBadge(double centerX, double centerY)
    {
        var text = new TextBlock
        {
            Text = "0",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Gray,
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
            Child = text,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(badge, centerX - BadgeSize / 2);
        AnimCanvas.SetTop(badge, centerY - BadgeSize / 2);
        return (badge, text);
    }
}
