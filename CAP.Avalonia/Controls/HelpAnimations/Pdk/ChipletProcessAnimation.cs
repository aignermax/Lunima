using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations.Pdk;

/// <summary>
/// Animated layer of the toolbar PDK help flyout (#1229): "a process belongs to its
/// chiplet". A component of process A (green) drops onto chiplet A and seats; once it
/// sits, a light pulse (the LightPulseAlongPath instances in the flyout) travels the
/// chiplet's waveguide. A component of process B (orange) then tries chiplet A, is
/// rejected (a small "✗" and a red flash on the chiplet frame) and bounces back,
/// finally seating on chiplet B, whose waveguide lights up at the end of the loop.
/// Panel-specific, not a reusable primitive — a transparent overlay placed over the
/// flyout's static diagram at the same size; it draws in the diagram's 368×124
/// coordinate space. Every position/opacity is a pure function of
/// <see cref="HelpAnimationBase.Progress"/>; the loop's last frame keeps both
/// components seated on their own chiplets.
/// </summary>
public class ChipletProcessAnimation : HelpAnimationBase
{
    // Component geometry in the diagram's coordinate space (24×24 squares).
    private const double ComponentSize = 24;
    private static readonly Point ComponentAStart = new(61, 8);
    private static readonly Point ComponentASeat = new(61, 67);
    private static readonly Point ComponentBStart = new(172, 8);
    private static readonly Point ComponentBAttempt = new(61, 30);
    private static readonly Point ComponentBBounce = new(150, 12);
    private static readonly Point ComponentBSeat = new(267, 67);

    // Chiplet A frame (matches the static AXAML) for the red rejection flash.
    private const double ChipletALeft = 16;
    private const double ChipletATop = 52;
    private const double ChipletAWidth = 140;
    private const double ChipletAHeight = 52;

    // The "✗" sits just right of chiplet A's slot while the rejection shows.
    private static readonly Point CrossPosition = new(88, 54);

    // Cue windows: A seats 0.05…0.28 (its pulse travels 0.34…0.58 via
    // LightPulseAlongPath in the flyout), B approaches 0.42…0.60, the rejection shows
    // 0.58…0.78, B bounces back 0.62…0.72 and seats on chiplet B 0.72…0.90 (pulse
    // 0.90…1.0). The last frame holds the seated end state.
    private static readonly (double Start, double End) ASeatWindow = (0.05, 0.28);
    private static readonly (double Start, double End) BApproachWindow = (0.42, 0.60);
    private static readonly (double Start, double End) BBounceWindow = (0.62, 0.72);
    private static readonly (double Start, double End) BSeatWindow = (0.72, 0.90);
    private static readonly (double Start, double End) CrossFadeIn = (0.58, 0.64);
    private static readonly (double Start, double End) CrossFadeOut = (0.70, 0.78);

    private static readonly IBrush ProcessABrush = new SolidColorBrush(0xFF4CAF50);
    private static readonly IBrush ProcessBBrush = new SolidColorBrush(0xFFFF9800);
    private static readonly IBrush RejectBrush = new SolidColorBrush(0xFFFF8A65);

    private readonly Border _componentA;
    private readonly Border _componentB;
    private readonly TextBlock _cross;
    private readonly Border _rejectFlash;

    /// <summary>Builds the two components, the rejection cross and the flash at their start positions.</summary>
    public ChipletProcessAnimation()
    {
        _componentA = CreateComponent(ProcessABrush, "A");
        _componentB = CreateComponent(ProcessBBrush, "B");

        _cross = new TextBlock
        {
            Text = "✗",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = RejectBrush,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_cross, CrossPosition.X);
        AnimCanvas.SetTop(_cross, CrossPosition.Y);

        _rejectFlash = new Border
        {
            Width = ChipletAWidth,
            Height = ChipletAHeight,
            BorderBrush = RejectBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_rejectFlash, ChipletALeft);
        AnimCanvas.SetTop(_rejectFlash, ChipletATop);

        Content = new AnimCanvas
        {
            IsHitTestVisible = false,
            Children = { _rejectFlash, _componentA, _componentB, _cross },
        };
        RenderFrame(0);
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var seatA = Ramp(progress, ASeatWindow.Start, ASeatWindow.End);
        SetPosition(_componentA, Lerp(ComponentAStart, ComponentASeat, seatA));

        // Sequential cue windows compose: each Ramp stays 0 before its window and 1
        // after, so the nested lerps hand B off from approach to bounce to seating.
        var approach = Ramp(progress, BApproachWindow.Start, BApproachWindow.End);
        var bounce = Ramp(progress, BBounceWindow.Start, BBounceWindow.End);
        var seatB = Ramp(progress, BSeatWindow.Start, BSeatWindow.End);
        var positionB = Lerp(
            Lerp(
                Lerp(ComponentBStart, ComponentBAttempt, approach),
                ComponentBBounce, bounce),
            ComponentBSeat, seatB);
        SetPosition(_componentB, positionB);

        var rejection = Ramp(progress, CrossFadeIn.Start, CrossFadeIn.End)
            * (1 - Ramp(progress, CrossFadeOut.Start, CrossFadeOut.End));
        _cross.Opacity = rejection;
        _rejectFlash.Opacity = rejection;
    }

    private static Border CreateComponent(IBrush fill, string label) => new()
    {
        Width = ComponentSize,
        Height = ComponentSize,
        Background = fill,
        CornerRadius = new CornerRadius(3),
        IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        },
    };

    private static Point Lerp(Point from, Point to, double t) =>
        new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

    private static void SetPosition(Border component, Point position)
    {
        AnimCanvas.SetLeft(component, position.X);
        AnimCanvas.SetTop(component, position.Y);
    }
}
