using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AnimCanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Looping diagram for the Design Checks help's "Blocked wires" section (#1421): why the
/// router "gives up" on a wire, and why the fix is sometimes "move the part" and sometimes
/// "make room". Two vignettes run in lockstep. Left, <b>sealed pin</b>: a pulse leaves a
/// pin, hits a neighbouring component body immediately and turns red; the component then
/// slides up and a fresh pulse goes through (move the component). Right, <b>no free
/// lane</b>: the corridor is already filled by parallel waveguides, the wanted direct
/// route shows as a dashed red fallback; the neighbours then spread apart, a lane opens
/// and the pulse goes through (make room). Every position and opacity is a pure function
/// of <see cref="HelpAnimationBase.Progress"/>; the last frame keeps the freed routes
/// visible. Draws in its own fixed 336×96 coordinate space — captions live in the flyout.
/// </summary>
public class BlockedWireHelpAnimation : HelpAnimationBase
{
    /// <summary>Width of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;

    /// <summary>Height of the coordinate space the control draws in.</summary>
    public const double SceneHeight = 96;

    /// <summary>Loop positions of the showcase phases: approach, fix-in-progress, freed.</summary>
    public static readonly double[] ShowcasePhases = { 0.25, 0.65, 1.0 };

    private const double PanelWidth = 152;
    private const double LeftOriginX = 8;
    private const double RightOriginX = 176;
    private const double PinInsetX = 8;
    private const double AxisY = 52;
    private const double PulseDiameter = 10;
    // Left vignette: a component body initially sealed against the pin's track.
    private const double ComponentWidth = 40;
    private const double ComponentHeight = 44;
    private const double ComponentLeft = LeftOriginX + 62;
    private const double ComponentTopBlocked = AxisY - ComponentHeight / 2;
    private const double ComponentTopFreed = AxisY - ComponentHeight - 4;

    // Right vignette: two parallel waveguides initially crowd the wanted corridor.
    private const double UpperBlockedY = AxisY - 12;
    private const double UpperFreedY = 14;
    private const double LowerBlockedY = AxisY + 12;
    private const double LowerFreedY = SceneHeight - 14;
    // Cue windows: approach, hold blocked, animate the fix, then a fresh pulse crosses.
    private static readonly (double Start, double End) ApproachWindow = (0.05, 0.4);
    private static readonly (double Start, double End) BlockedWindow = (0.4, 0.55);
    private static readonly (double Start, double End) FixWindow = (0.55, 0.75);
    private static readonly (double Start, double End) ThroughWindow = (0.78, 1.0);

    private static readonly IBrush TrackBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush PulseBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush BlockedBrush = new ImmutableSolidColorBrush(0xFFFF8A65);
    private static readonly IBrush FallbackBrush = new ImmutableSolidColorBrush(0xFFFF5252);
    private static readonly IBrush ComponentBrush = new ImmutableSolidColorBrush(0xFF23232B);
    private static readonly IBrush ComponentBorderBrush = new ImmutableSolidColorBrush(0xFF5A5A60);
    private static readonly IBrush PinBrush = new ImmutableSolidColorBrush(0xFFCFCFD6);

    private readonly Border _component;
    private readonly Line _upperNeighbour;
    private readonly Line _lowerNeighbour;
    private readonly Line _fallbackLane;
    private readonly Ellipse _leftBlockedPulse;
    private readonly Ellipse _leftThroughPulse;
    private readonly Ellipse _rightBlockedPulse;
    private readonly Ellipse _rightThroughPulse;

    /// <summary>Builds both vignettes, the obstacle actors and the four pulses.</summary>
    public BlockedWireHelpAnimation()
    {
        var canvas = new AnimCanvas { Width = SceneWidth, Height = SceneHeight, IsHitTestVisible = false };
        double leftPinX = LeftOriginX + PinInsetX;
        double rightPinX = RightOriginX + PinInsetX;
        double leftEndX = LeftOriginX + PanelWidth;
        double rightEndX = RightOriginX + PanelWidth;
        AddTrack(canvas, leftPinX, AxisY, leftEndX, AxisY);
        AddTrack(canvas, rightPinX, AxisY, rightEndX, AxisY);
        canvas.Children.Add(CreatePin(leftPinX));
        canvas.Children.Add(CreatePin(rightPinX));

        _component = new Border
        {
            Width = ComponentWidth, Height = ComponentHeight,
            Background = ComponentBrush, BorderBrush = ComponentBorderBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = ComponentLeft,
        };
        canvas.Children.Add(_component);

        _upperNeighbour = CreateNeighbour(rightPinX + 12, rightEndX);
        _lowerNeighbour = CreateNeighbour(rightPinX + 12, rightEndX);
        canvas.Children.Add(_upperNeighbour);
        canvas.Children.Add(_lowerNeighbour);

        _fallbackLane = new Line
        {
            StartPoint = new Point(rightPinX + 4, AxisY), EndPoint = new Point(rightEndX, AxisY),
            Stroke = FallbackBrush, StrokeThickness = 1.5,
            StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 4, 3 },
            IsHitTestVisible = false,
        };
        canvas.Children.Add(_fallbackLane);

        _leftBlockedPulse = CreatePulse();
        _leftThroughPulse = CreatePulse();
        _rightBlockedPulse = CreatePulse();
        _rightThroughPulse = CreatePulse();
        canvas.Children.Add(_leftBlockedPulse);
        canvas.Children.Add(_leftThroughPulse);
        canvas.Children.Add(_rightBlockedPulse);
        canvas.Children.Add(_rightThroughPulse);

        Content = canvas;
        RenderFrame(0);
    }

    /// <summary>Top of the sealing component: slides up during the fix window.</summary>
    public static double ComponentTopAt(double progress) =>
        ComponentTopBlocked
        + Ramp(progress, FixWindow.Start, FixWindow.End) * (ComponentTopFreed - ComponentTopBlocked);

    /// <summary>Y of the upper corridor neighbour: spreads up during the fix.</summary>
    public static double UpperNeighbourYAt(double progress) =>
        UpperBlockedY + Ramp(progress, FixWindow.Start, FixWindow.End) * (UpperFreedY - UpperBlockedY);

    /// <summary>Y of the lower corridor neighbour: spreads down during the fix.</summary>
    public static double LowerNeighbourYAt(double progress) =>
        LowerBlockedY + Ramp(progress, FixWindow.Start, FixWindow.End) * (LowerFreedY - LowerBlockedY);

    /// <summary>Opacity of the dashed red blocked-fallback lane.</summary>
    public static double FallbackOpacityAt(double progress) =>
        Ramp(progress, BlockedWindow.Start, BlockedWindow.End)
        * (1 - Ramp(progress, FixWindow.Start, FixWindow.End));

    /// <summary>Opacity of either blocked pulse: full while approaching, fades once the fix starts.</summary>
    public static double BlockedPulseOpacityAt(double progress) =>
        Ramp(progress, ApproachWindow.Start, ApproachWindow.End)
        * (1 - Ramp(progress, FixWindow.Start, FixWindow.End));

    /// <summary>Opacity of either through pulse: appears once the fix has opened the route.</summary>
    public static double ThroughPulseOpacityAt(double progress) =>
        Ramp(progress, ThroughWindow.Start, ThroughWindow.End);

    /// <summary>X of the left blocked pulse: from the pin to the component face, then stalls.</summary>
    public static double LeftBlockedPulseXAt(double progress)
    {
        double approach = Ramp(progress, ApproachWindow.Start, ApproachWindow.End);
        double pinX = LeftOriginX + PinInsetX;
        return pinX + approach * (ComponentLeft - pinX);
    }

    /// <summary>X of the right blocked pulse: from the pin to the pinched corridor, then stalls.</summary>
    public static double RightBlockedPulseXAt(double progress)
    {
        double approach = Ramp(progress, ApproachWindow.Start, ApproachWindow.End);
        double pinX = RightOriginX + PinInsetX;
        double stallX = pinX + (RightOriginX + PanelWidth - pinX) * 0.45;
        return pinX + approach * (stallX - pinX);
    }

    /// <summary>X of either through pulse: crosses the freed route during the through window.</summary>
    public static double ThroughPulseXAt(double progress, double panelOriginX)
    {
        double through = Ramp(progress, ThroughWindow.Start, ThroughWindow.End);
        double pinX = panelOriginX + PinInsetX;
        return pinX + through * (panelOriginX + PanelWidth - pinX);
    }

    /// <summary>True once the approach has stalled: the blocked pulses show red.</summary>
    public static bool IsBlockedPhase(double progress) => progress >= ApproachWindow.End;

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        AnimCanvas.SetTop(_component, ComponentTopAt(progress));

        double upperY = UpperNeighbourYAt(progress);
        double lowerY = LowerNeighbourYAt(progress);
        _upperNeighbour.StartPoint = new Point(_upperNeighbour.StartPoint.X, upperY);
        _upperNeighbour.EndPoint = new Point(_upperNeighbour.EndPoint.X, upperY);
        _lowerNeighbour.StartPoint = new Point(_lowerNeighbour.StartPoint.X, lowerY);
        _lowerNeighbour.EndPoint = new Point(_lowerNeighbour.EndPoint.X, lowerY);

        _fallbackLane.Opacity = FallbackOpacityAt(progress);
        _fallbackLane.IsVisible = _fallbackLane.Opacity > 0;

        double blockedOpacity = BlockedPulseOpacityAt(progress);
        bool blocked = IsBlockedPhase(progress);
        RenderBlockedPulse(_leftBlockedPulse, LeftBlockedPulseXAt(progress), blockedOpacity, blocked);
        RenderBlockedPulse(_rightBlockedPulse, RightBlockedPulseXAt(progress), blockedOpacity, blocked);

        double throughOpacity = ThroughPulseOpacityAt(progress);
        RenderThroughPulse(_leftThroughPulse, ThroughPulseXAt(progress, LeftOriginX), throughOpacity);
        RenderThroughPulse(_rightThroughPulse, ThroughPulseXAt(progress, RightOriginX), throughOpacity);
    }

    private static void RenderBlockedPulse(Ellipse pulse, double x, double opacity, bool blocked)
    {
        pulse.IsVisible = opacity > 0;
        pulse.Opacity = opacity;
        pulse.Fill = blocked ? BlockedBrush : PulseBrush;
        AnimCanvas.SetLeft(pulse, x - PulseDiameter / 2);
        AnimCanvas.SetTop(pulse, AxisY - PulseDiameter / 2);
    }

    private static void RenderThroughPulse(Ellipse pulse, double x, double opacity)
    {
        pulse.IsVisible = opacity > 0;
        pulse.Opacity = opacity;
        AnimCanvas.SetLeft(pulse, x - PulseDiameter / 2);
        AnimCanvas.SetTop(pulse, AxisY - PulseDiameter / 2);
    }
    private static void AddTrack(AnimCanvas canvas, double x1, double y1, double x2, double y2) =>
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2),
            Stroke = TrackBrush, StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false,
        });

    private static Line CreateNeighbour(double x1, double x2) => new()
    {
        StartPoint = new Point(x1, 0), EndPoint = new Point(x2, 0),
        Stroke = TrackBrush, StrokeThickness = 2,
        StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false,
    };

    private static Border CreatePin(double x) => new()
    {
        Width = 6, Height = 6, Background = PinBrush, CornerRadius = new CornerRadius(3),
        IsHitTestVisible = false,
        [AnimCanvas.LeftProperty] = x - 3,
        [AnimCanvas.TopProperty] = AxisY - 3,
    };

    private static Ellipse CreatePulse() => new()
    {
        Width = PulseDiameter, Height = PulseDiameter, Fill = PulseBrush,
        IsVisible = false, IsHitTestVisible = false,
    };
}
