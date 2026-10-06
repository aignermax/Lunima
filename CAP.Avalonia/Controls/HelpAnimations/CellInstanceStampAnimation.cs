using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Animated layer of the Logic panel's cell-instance help flyout (#1411): one cell
/// template box is "stamped" twice — a translucent copy of the template slides from
/// the template onto the CELL0 slot, then onto the CELL1 slot — and finally one
/// register bit lights up inside CELL0 only while CELL1 stays dark: same template,
/// independent state per instance. Driven by the loop's
/// <see cref="HelpAnimationBase.Progress"/>; the stamp positions and the lit bit are
/// pure functions of it, so a scrubbed frame is always self-consistent.
/// Panel-specific, not a reusable primitive — a transparent overlay placed over the
/// flyout's static diagram (template, arrow, two instance boxes, register dots) at
/// the same size; it draws in the diagram's 368×120 coordinate space. The loop's
/// last frame keeps the lit-bit end state visible.
/// </summary>
public class CellInstanceStampAnimation : HelpAnimationBase
{
    // Geometry inside the diagram's coordinate space. Must match the flyout's static
    // AXAML: template box at (14, 34) size 96×52, instance slots at (176, 8) and
    // (176, 66) size 120×46, CELL0's first register dot at (196, 30) size 10×10.
    private const double TemplateLeft = 14;
    private const double TemplateTop = 34;
    private const double TemplateWidth = 96;
    private const double TemplateHeight = 52;
    private const double InstanceLeft = 176;
    private const double Cell0Top = 8;
    private const double Cell1Top = 66;
    private const double LitBitLeft = 196;
    private const double LitBitTop = 30;
    private const double LitBitSize = 10;

    // Cue windows: first stamp 0.05…0.30, second stamp 0.30…0.55, the CELL0 bit
    // lights 0.65…0.80 and holds to the loop end.
    private static readonly (double Start, double End) FirstStampWindow = (0.05, 0.30);
    private static readonly (double Start, double End) SecondStampWindow = (0.30, 0.55);
    private static readonly (double Start, double End) BitLightsWindow = (0.65, 0.80);

    private static readonly IBrush StampFill = new ImmutableSolidColorBrush(0x305D5D8D);
    private static readonly IBrush StampBorder = new ImmutableSolidColorBrush(0xA08D8DBD);
    private static readonly IBrush LitBitBrush = new ImmutableSolidColorBrush(0xFF81C784);

    private readonly Border _firstStamp;
    private readonly Border _secondStamp;
    private readonly Border _litBit;

    /// <summary>Builds the two stamp ghosts and the lit bit at their start positions.</summary>
    public CellInstanceStampAnimation()
    {
        _firstStamp = CreateStamp();
        _secondStamp = CreateStamp();
        _litBit = new Border
        {
            Width = LitBitSize,
            Height = LitBitSize,
            CornerRadius = new CornerRadius(2),
            Background = LitBitBrush,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(_litBit, LitBitLeft);
        AnimCanvas.SetTop(_litBit, LitBitTop);

        Content = new AnimCanvas
        {
            IsHitTestVisible = false,
            Children = { _firstStamp, _secondStamp, _litBit },
        };
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        MoveStamp(_firstStamp, progress, FirstStampWindow, Cell0Top);
        MoveStamp(_secondStamp, progress, SecondStampWindow, Cell1Top);
        _litBit.Opacity = Ramp(progress, BitLightsWindow.Start, BitLightsWindow.End);
    }

    /// <summary>
    /// Slides one stamp ghost from the template slot to its instance slot across the
    /// cue window; it is invisible before the window starts and fades out on arrival
    /// (the static instance box underneath takes over visually).
    /// </summary>
    private static void MoveStamp(
        Border stamp, double progress, (double Start, double End) window, double targetTop)
    {
        var move = Ramp(progress, window.Start, window.End);
        AnimCanvas.SetLeft(stamp, TemplateLeft + (InstanceLeft - TemplateLeft) * move);
        AnimCanvas.SetTop(stamp, TemplateTop + (targetTop - TemplateTop) * move);
        stamp.Opacity = move >= 1 ? 0 : 1;
    }

    private static Border CreateStamp()
    {
        var stamp = new Border
        {
            Width = TemplateWidth,
            Height = TemplateHeight,
            CornerRadius = new CornerRadius(4),
            Background = StampFill,
            BorderBrush = StampBorder,
            BorderThickness = new Thickness(1.5),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(stamp, TemplateLeft);
        AnimCanvas.SetTop(stamp, TemplateTop);
        return stamp;
    }
}
