using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Truth Table help flyout (#1207): the coupler's output pulse
/// and its 0/1 badge, driven by the loop's <see cref="HelpAnimationBase.Progress"/>.
/// First loop half: the two inputs arrive in phase, so a bright sum pulse travels the
/// output track and the badge flips to 1; second half: the inputs arrive in anti-phase
/// (the π marker fades in next to input B), they cancel, the output stays dark and the
/// badge reads 0. The badge state is a pure function of the output level, so a scrubbed
/// frame is always self-consistent. Panel-specific, not a reusable primitive — a
/// transparent overlay placed over the flyout's static diagram (input tracks, coupler
/// box, output track) at the same size; it draws in the diagram's 368×112 coordinate
/// space. The input and bias pulses themselves are the LightPulseAlongPath primitive.
/// The loop's last frame keeps the cancelled (dark) end state visible.
/// </summary>
public class InterferenceOutputAnimation : HelpAnimationBase
{
    // Output-track geometry in the diagram's coordinate space (the static output Line
    // in the flyout spans x 230…306 at y 56); the glow pulse travels its length while
    // the inputs add constructively.
    private const double TrackY = 56;
    private const double TrackStartX = 236;
    private const double TrackEndX = 300;
    private const double GlowMinDiameter = 6;
    private const double GlowMaxDiameter = 16;

    /// <summary>Output level at which the badge flips to 1.</summary>
    private const double BadgeOnLevel = 0.5;

    // Cue windows: in-phase inputs arrive 0…0.3 (LightPulseAlongPath), the bright sum
    // pulse charges 0.32…0.4, holds, and fades 0.48…0.56; the anti-phase inputs arrive
    // 0.58…0.88 with the π marker fading in 0.6…0.66, and cancel — the output stays
    // dark for the rest of the loop.
    private static readonly (double Start, double End) ChargeWindow = (0.32, 0.40);
    private static readonly (double Start, double End) FadeWindow = (0.48, 0.56);
    private static readonly (double Start, double End) PiFadeWindow = (0.60, 0.66);

    private static readonly IBrush GlowBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush PiBrush = new ImmutableSolidColorBrush(0xFF4FC3F7);
    private static readonly IBrush BadgeIdleBrush = new ImmutableSolidColorBrush(0xFF666666);
    private static readonly IBrush BadgeOneBrush = new ImmutableSolidColorBrush(0xFF81C784);

    private readonly Ellipse _glow;
    private readonly Border _badge;
    private readonly TextBlock _badgeZero;
    private readonly TextBlock _badgeOne;
    private readonly TextBlock _piMarker;

    /// <summary>Builds the output glow, the 0/1 badge and the π marker at their fixed diagram positions.</summary>
    public InterferenceOutputAnimation()
    {
        _glow = new Ellipse
        {
            Fill = GlowBrush,
            IsVisible = false,
            IsHitTestVisible = false,
        };

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
        AnimCanvas.SetLeft(_badge, 316);
        AnimCanvas.SetTop(_badge, 42);

        _piMarker = new TextBlock
        {
            Text = "π",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = PiBrush,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_piMarker, 30);
        AnimCanvas.SetTop(_piMarker, 90);

        Content = new AnimCanvas
        {
            IsHitTestVisible = false,
            Children = { _glow, _badge, _piMarker },
        };
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var level = System.Math.Min(
            Ramp(progress, ChargeWindow.Start, ChargeWindow.End),
            1 - Ramp(progress, FadeWindow.Start, FadeWindow.End));
        RenderGlow(progress, level);

        var on = level >= BadgeOnLevel;
        _badge.BorderBrush = on ? BadgeOneBrush : BadgeIdleBrush;
        _badgeOne.IsVisible = on;
        _badgeZero.IsVisible = !on;
        _piMarker.Opacity = Ramp(progress, PiFadeWindow.Start, PiFadeWindow.End);
    }

    private void RenderGlow(double progress, double level)
    {
        if (level <= 0)
        {
            _glow.IsVisible = false;
            return;
        }

        var travel = Ramp(progress, ChargeWindow.Start, FadeWindow.End);
        var diameter = GlowMinDiameter + (GlowMaxDiameter - GlowMinDiameter) * level;
        _glow.IsVisible = true;
        _glow.Width = _glow.Height = diameter;
        _glow.Opacity = 0.35 + 0.65 * level;
        AnimCanvas.SetLeft(_glow, TrackStartX + (TrackEndX - TrackStartX) * travel - diameter / 2);
        AnimCanvas.SetTop(_glow, TrackY - diameter / 2);
    }
}
