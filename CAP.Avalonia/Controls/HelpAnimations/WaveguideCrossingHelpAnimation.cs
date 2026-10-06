using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AnimCanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Looping diagram for the crossing-finding (?) help (#1391): why two waveguides may
/// not simply cross. Two mini scenes run in lockstep — left a bare X junction, right a
/// crossing component (tapered multimode centre) at the same junction. A light pulse
/// enters from the left in both scenes; at the bare X it splits and leaks into the
/// crossing arm (the main pulse dims by the leaked fraction), while through the
/// component it continues straight at full brightness. Every position and opacity is a
/// pure function of <see cref="HelpAnimationBase.Progress"/>; the last frame keeps the
/// leaked end state visible. Draws in its own fixed 336×96 coordinate space — localized
/// captions live in the flyout, not in this control.
/// </summary>
public class WaveguideCrossingHelpAnimation : HelpAnimationBase
{
    /// <summary>Width of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;

    /// <summary>Height of the coordinate space the control draws in.</summary>
    public const double SceneHeight = 96;

    /// <summary>Loop positions of the showcase phases: pulse approaching, splitting/passing, end state.</summary>
    public static readonly double[] ShowcasePhases = { 0.25, 0.7, 1.0 };

    // Geometry: two panels side by side, junction at each panel's horizontal centre.
    private const double PanelWidth = 160;
    private const double LeftOriginX = 8;
    private const double RightOriginX = 176;
    private const double AxisY = 52;
    private const double WaveguideTop = 12;
    private const double WaveguideBottom = 92;
    private const double ComponentHalfWidth = 12;
    private const double ComponentHalfHeight = 10;
    private const double PulseDiameter = 10;
    private const double LeakPulseDiameter = 7;

    // Cue windows: the pulse approaches the junction first, then crosses it; both pulses
    // hold afterwards so the last frame keeps the end state visible (HELP-ANIMATIONS.md).
    private static readonly (double Start, double End) ApproachWindow = (0.05, 0.45);
    private static readonly (double Start, double End) TraverseWindow = (0.5, 0.9);

    /// <summary>Brightness fraction the bare-X main pulse loses to the crossing arm.</summary>
    public const double LeakFraction = 0.45;

    private static readonly IBrush TrackBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush PulseBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush LeakBrush = new ImmutableSolidColorBrush(0xFFFF8A65);
    private static readonly IBrush ComponentBrush = new ImmutableSolidColorBrush(0xFF23232B);
    private static readonly IBrush ComponentBorderBrush = new ImmutableSolidColorBrush(0xFF5A5A60);

    private readonly Ellipse _leftPulse;
    private readonly Ellipse _leakPulse;
    private readonly Ellipse _rightPulse;

    /// <summary>Builds both junction scenes and the three pulses.</summary>
    public WaveguideCrossingHelpAnimation()
    {
        var canvas = new AnimCanvas
        {
            Width = SceneWidth,
            Height = SceneHeight,
            IsHitTestVisible = false,
        };

        AddTrack(canvas, LeftOriginX, AxisY, LeftOriginX + PanelWidth, AxisY);
        AddTrack(canvas, LeftJunctionX, WaveguideTop, LeftJunctionX, WaveguideBottom);

        AddTrack(canvas, RightOriginX, AxisY, RightOriginX + PanelWidth, AxisY);
        AddTrack(canvas, RightJunctionX, WaveguideTop, RightJunctionX, AxisY - ComponentHalfHeight);
        AddTrack(canvas, RightJunctionX, AxisY + ComponentHalfHeight, RightJunctionX, WaveguideBottom);
        canvas.Children.Add(CreateComponent());

        _leftPulse = CreatePulse(PulseDiameter, PulseBrush);
        _leakPulse = CreatePulse(LeakPulseDiameter, LeakBrush);
        _rightPulse = CreatePulse(PulseDiameter, PulseBrush);
        canvas.Children.Add(_leftPulse);
        canvas.Children.Add(_leakPulse);
        canvas.Children.Add(_rightPulse);

        Content = canvas;
        RenderFrame(0);
    }

    /// <summary>Horizontal centre of the bare-X junction.</summary>
    public const double LeftJunctionX = LeftOriginX + PanelWidth / 2;

    /// <summary>Horizontal centre of the crossing-component junction.</summary>
    public const double RightJunctionX = RightOriginX + PanelWidth / 2;

    /// <summary>
    /// X position of a main pulse at a loop position: travels from its panel's left edge
    /// to the junction during the approach window, then on to the right edge.
    /// </summary>
    public static double MainPulseXAt(double progress, double panelOriginX)
    {
        double approach = Ramp(progress, ApproachWindow.Start, ApproachWindow.End);
        double traverse = Ramp(progress, TraverseWindow.Start, TraverseWindow.End);
        double junctionX = panelOriginX + PanelWidth / 2;
        double x = panelOriginX + approach * (junctionX - panelOriginX);
        return x + traverse * (panelOriginX + PanelWidth - junctionX);
    }

    /// <summary>
    /// Opacity of the bare-X main pulse: full before the junction, then drops by
    /// <see cref="LeakFraction"/> as the leak pulse peels off into the crossing arm.
    /// </summary>
    public static double BareXMainOpacityAt(double progress) =>
        1 - LeakFraction * Ramp(progress, TraverseWindow.Start, TraverseWindow.End);

    /// <summary>
    /// Opacity of the leak pulse climbing the crossing arm of the bare X: zero until the
    /// main pulse reaches the junction, then ramps up and holds.
    /// </summary>
    public static double LeakOpacityAt(double progress) =>
        Ramp(progress, TraverseWindow.Start, TraverseWindow.End);

    /// <summary>Y position of the leak pulse: from the junction up the crossing arm.</summary>
    public static double LeakPulseYAt(double progress) =>
        AxisY - Ramp(progress, TraverseWindow.Start, TraverseWindow.End) * (AxisY - WaveguideTop);

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        bool approaching = progress >= ApproachWindow.Start;

        _leftPulse.IsVisible = approaching;
        AnimCanvas.SetLeft(_leftPulse, MainPulseXAt(progress, LeftOriginX) - PulseDiameter / 2);
        AnimCanvas.SetTop(_leftPulse, AxisY - PulseDiameter / 2);
        _leftPulse.Opacity = BareXMainOpacityAt(progress);

        double leakOpacity = LeakOpacityAt(progress);
        _leakPulse.IsVisible = leakOpacity > 0;
        _leakPulse.Opacity = leakOpacity;
        AnimCanvas.SetLeft(_leakPulse, LeftJunctionX - LeakPulseDiameter / 2);
        AnimCanvas.SetTop(_leakPulse, LeakPulseYAt(progress) - LeakPulseDiameter / 2);

        _rightPulse.IsVisible = approaching;
        AnimCanvas.SetLeft(_rightPulse, MainPulseXAt(progress, RightOriginX) - PulseDiameter / 2);
        AnimCanvas.SetTop(_rightPulse, AxisY - PulseDiameter / 2);
        _rightPulse.Opacity = 1;
    }

    private static void AddTrack(AnimCanvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = TrackBrush,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
    }

    /// <summary>The crossing component: a tapered (elongated-hexagon) multimode centre.</summary>
    private static Polygon CreateComponent() => new()
    {
        Points =
        {
            new Point(RightJunctionX - ComponentHalfWidth, AxisY),
            new Point(RightJunctionX - ComponentHalfWidth / 2, AxisY - ComponentHalfHeight),
            new Point(RightJunctionX + ComponentHalfWidth / 2, AxisY - ComponentHalfHeight),
            new Point(RightJunctionX + ComponentHalfWidth, AxisY),
            new Point(RightJunctionX + ComponentHalfWidth / 2, AxisY + ComponentHalfHeight),
            new Point(RightJunctionX - ComponentHalfWidth / 2, AxisY + ComponentHalfHeight),
        },
        Fill = ComponentBrush,
        Stroke = ComponentBorderBrush,
        StrokeThickness = 1,
        IsHitTestVisible = false,
    };

    private static Ellipse CreatePulse(double diameter, IBrush brush) => new()
    {
        Width = diameter,
        Height = diameter,
        Fill = brush,
        IsVisible = false,
        IsHitTestVisible = false,
    };
}
