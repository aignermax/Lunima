using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Logic panel help flyout (#1196): the gate's output power
/// bar and its 0/1 badge, driven by the loop's <see cref="HelpAnimationBase.Progress"/>.
/// First loop half: a strong pulse lifts the bar past the dashed threshold line and
/// the badge flips to 1; second half: a weak pulse keeps the bar below the threshold
/// and the badge stays 0. The badge state is a pure function of the bar level, so a
/// scrubbed frame is always self-consistent. Panel-specific, not a reusable primitive —
/// a transparent overlay placed over the flyout's static diagram (gate, tracks, bar
/// outline, threshold line) at the same size; it draws in the diagram's 368×96
/// coordinate space. The light pulses themselves are the LightPulseAlongPath primitive.
/// The loop's last frame keeps the below-threshold end state visible.
/// </summary>
public class LogicThresholdAnimation : HelpAnimationBase
{
    // Bar geometry inside the diagram's coordinate space (the static bar outline in
    // the flyout spans x 244…268, y 8…72; the fill sits inside it, growing upward).
    private const double BarLeft = 247;
    private const double BarWidth = 18;
    private const double BarBottom = 70;
    private const double BarMaxHeight = 60;

    /// <summary>Bar level of the strong pulse (above <see cref="ThresholdFraction"/>).</summary>
    private const double HighFraction = 0.85;

    /// <summary>Bar level of the weak pulse (below <see cref="ThresholdFraction"/>).</summary>
    private const double LowFraction = 0.40;

    /// <summary>Bar fraction at which the dashed threshold line sits and the badge flips.</summary>
    private const double ThresholdFraction = 0.70;

    // Cue windows: strong pulse arrives 0…0.2, bar charges 0.2…0.35 and holds to 0.5,
    // discharges 0.5…0.56, weak pulse arrives 0.55…0.7, bar charges 0.7…0.85, holds to 1.
    private static readonly (double Start, double End) HighChargeWindow = (0.20, 0.35);
    private static readonly (double Start, double End) DischargeWindow = (0.50, 0.56);
    private static readonly (double Start, double End) LowChargeWindow = (0.70, 0.85);
    private const double HalfLoop = 0.50;
    private const double LowPulseArrival = 0.70;

    private static readonly IBrush BarLowBrush = new SolidColorBrush(0xFF7FAAFF);
    private static readonly IBrush BarHighBrush = new SolidColorBrush(0xFF81C784);
    private static readonly IBrush BadgeIdleBrush = new SolidColorBrush(0xFF666666);
    private static readonly IBrush BadgeOneBrush = new SolidColorBrush(0xFF81C784);

    private readonly Rectangle _barFill;
    private readonly Border _badge;
    private readonly TextBlock _badgeZero;
    private readonly TextBlock _badgeOne;

    /// <summary>Builds the bar fill and the 0/1 badge at their fixed diagram positions.</summary>
    public LogicThresholdAnimation()
    {
        _barFill = new Rectangle
        {
            Width = BarWidth,
            Fill = BarLowBrush,
            RadiusX = 1,
            RadiusY = 1,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_barFill, BarLeft);

        _badgeZero = new TextBlock
        {
            Text = "0",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _badgeOne = new TextBlock
        {
            Text = "1",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(0xFF90EE90),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _badge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(0xFF101018),
            BorderBrush = BadgeIdleBrush,
            BorderThickness = new Thickness(1.5),
            Child = new Panel { Children = { _badgeZero, _badgeOne } },
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_badge, 310);
        AnimCanvas.SetTop(_badge, 26);

        Content = new AnimCanvas
        {
            IsHitTestVisible = false,
            Children = { _barFill, _badge },
        };
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var fraction = BarFraction(progress);
        var height = fraction * BarMaxHeight;
        _barFill.Height = height;
        AnimCanvas.SetTop(_barFill, BarBottom - height);

        var above = fraction >= ThresholdFraction;
        _barFill.Fill = above ? BarHighBrush : BarLowBrush;
        _badge.BorderBrush = above ? BadgeOneBrush : BadgeIdleBrush;
        _badgeOne.IsVisible = above;
        _badgeZero.IsVisible = !above;
    }

    private static double BarFraction(double progress)
    {
        if (progress < HalfLoop)
            return HighFraction * Ramp(progress, HighChargeWindow.Start, HighChargeWindow.End);
        if (progress < LowPulseArrival)
            return HighFraction * (1 - Ramp(progress, DischargeWindow.Start, DischargeWindow.End));
        return LowFraction * Ramp(progress, LowChargeWindow.Start, LowChargeWindow.End);
    }
}
