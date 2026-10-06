using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AnimCanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Looping diagram for the Mode Solver help flyout (#1431): a waveguide cross-section
/// whose core widens and narrows. The Gaussian-like mode profile drawn across it
/// tightens when the core is wide and spreads into the cladding when it is narrow,
/// while a marker on the n_eff track below slides between the "n_clad" and "n_core"
/// ticks — a wider core pulls n_eff towards the core index. Illustration only, not a
/// solver call. Every position and width is a pure function of
/// <see cref="HelpAnimationBase.Progress"/> (core width follows sin²(π·p), so the loop
/// is continuous); tests scrub <see cref="HelpAnimationBase.Progress"/> with AutoPlay
/// off for deterministic frames. Draws in its own fixed 336×140 coordinate space —
/// the tick captions are settable so the flyout can localize them.
/// </summary>
public class ModeConfinementAnimation : HelpAnimationBase
{
    /// <summary>Width of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;

    /// <summary>Height of the coordinate space the control draws in.</summary>
    public const double SceneHeight = 140;

    /// <summary>Loop positions of the showcase phases: narrow core, mid, wide core.</summary>
    public static readonly double[] ShowcasePhases = { 0.0, 0.25, 0.5 };

    /// <summary>Caption under the left n_eff tick (low end, the cladding index).</summary>
    public static readonly StyledProperty<string> CladTickLabelProperty =
        AvaloniaProperty.Register<ModeConfinementAnimation, string>(nameof(CladTickLabel), "n_clad");

    /// <summary>Caption under the right n_eff tick (high end, the core index).</summary>
    public static readonly StyledProperty<string> CoreTickLabelProperty =
        AvaloniaProperty.Register<ModeConfinementAnimation, string>(nameof(CoreTickLabel), "n_core");

    // Cross-section geometry.
    private const double SectionLeft = 8;
    private const double SectionTop = 8;
    private const double SectionWidth = 320;
    private const double SectionHeight = 96;
    private const double SectionCenterX = SectionLeft + SectionWidth / 2;
    private const double CoreHeight = 40;
    private const double CoreTop = SectionTop + (SectionHeight - CoreHeight) / 2;
    private const double MinCoreWidth = 24;
    private const double MaxCoreWidth = 120;

    // Mode profile: Gaussian across the section, spreading as the core narrows.
    private const double ProfileBaselineY = CoreTop + CoreHeight / 2;
    private const double ProfileAmplitude = 44;
    private const double SigmaTight = 26;
    private const double SigmaSpread = 70;
    private const double ProfileStepX = 4;

    // n_eff track below the cross-section.
    private const double TrackLeft = 40;
    private const double TrackRight = SceneWidth - 40;
    private const double TrackY = 118;
    private const double MarkerDiameter = 10;

    private static readonly IBrush CladdingBrush = new ImmutableSolidColorBrush(0xFF1F2A3D);
    private static readonly IBrush CladdingBorderBrush = new ImmutableSolidColorBrush(0xFF3D4A63);
    private static readonly IBrush CoreBrush = new ImmutableSolidColorBrush(0x554FC3F7);
    private static readonly IBrush CoreBorderBrush = new ImmutableSolidColorBrush(0xFF4FC3F7);
    private static readonly IBrush ProfileBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush TrackBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush TickLabelBrush = new ImmutableSolidColorBrush(0xFF888888);

    private readonly Rectangle _core;
    private readonly Polyline _profile;
    private readonly Ellipse _marker;

    /// <summary>Builds the cross-section, the mode profile and the n_eff track.</summary>
    public ModeConfinementAnimation()
    {
        var canvas = new AnimCanvas { Width = SceneWidth, Height = SceneHeight, IsHitTestVisible = false };

        canvas.Children.Add(new Rectangle
        {
            Width = SectionWidth, Height = SectionHeight,
            Fill = CladdingBrush, Stroke = CladdingBorderBrush, StrokeThickness = 1,
            IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = SectionLeft,
            [AnimCanvas.TopProperty] = SectionTop,
        });

        _core = new Rectangle
        {
            Height = CoreHeight,
            Fill = CoreBrush, Stroke = CoreBorderBrush, StrokeThickness = 1.5,
            IsHitTestVisible = false,
            [AnimCanvas.TopProperty] = CoreTop,
        };
        canvas.Children.Add(_core);

        _profile = new Polyline
        {
            Stroke = ProfileBrush, StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(_profile);

        canvas.Children.Add(new Line
        {
            StartPoint = new Point(TrackLeft, TrackY), EndPoint = new Point(TrackRight, TrackY),
            Stroke = TrackBrush, StrokeThickness = 1.5, IsHitTestVisible = false,
        });
        canvas.Children.Add(CreateTick(TrackLeft));
        canvas.Children.Add(CreateTick(TrackRight));
        canvas.Children.Add(CreateTickLabel(CladTickLabelProperty, TrackLeft));
        canvas.Children.Add(CreateTickLabel(CoreTickLabelProperty, TrackRight));

        _marker = new Ellipse
        {
            Width = MarkerDiameter, Height = MarkerDiameter,
            Fill = ProfileBrush, IsHitTestVisible = false,
            [AnimCanvas.TopProperty] = TrackY - MarkerDiameter / 2,
        };
        canvas.Children.Add(_marker);

        Content = canvas;
        Width = SceneWidth;
        Height = SceneHeight;
    }

    /// <summary>Caption under the left n_eff tick (low end, the cladding index).</summary>
    public string CladTickLabel
    {
        get => GetValue(CladTickLabelProperty);
        set => SetValue(CladTickLabelProperty, value);
    }

    /// <summary>Caption under the right n_eff tick (high end, the core index).</summary>
    public string CoreTickLabel
    {
        get => GetValue(CoreTickLabelProperty);
        set => SetValue(CoreTickLabelProperty, value);
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        // sin²(π·p): narrow at both loop ends, wide in the middle — a seamless loop.
        double t = Math.Sin(Math.PI * progress);
        t *= t;

        double coreWidth = MinCoreWidth + (MaxCoreWidth - MinCoreWidth) * t;
        _core.Width = coreWidth;
        _core.SetValue(AnimCanvas.LeftProperty, SectionCenterX - coreWidth / 2);

        double sigma = SigmaSpread + (SigmaTight - SigmaSpread) * t;
        _profile.Points = BuildGaussianPoints(sigma);

        double markerX = TrackLeft + (TrackRight - TrackLeft) * t;
        _marker.SetValue(AnimCanvas.LeftProperty, markerX - MarkerDiameter / 2);
    }

    /// <summary>Samples a Gaussian centred on the core across the whole cross-section.</summary>
    private static Point[] BuildGaussianPoints(double sigma)
    {
        int count = (int)((SectionWidth + ProfileStepX - 1) / ProfileStepX) + 1;
        var points = new Point[count];
        for (int i = 0; i < count; i++)
        {
            double x = SectionLeft + i * ProfileStepX;
            double dx = (x - SectionCenterX) / sigma;
            double y = ProfileBaselineY - ProfileAmplitude * Math.Exp(-0.5 * dx * dx);
            points[i] = new Point(x, y);
        }
        return points;
    }

    private static Line CreateTick(double x) => new()
    {
        StartPoint = new Point(x, TrackY - 4), EndPoint = new Point(x, TrackY + 4),
        Stroke = TrackBrush, StrokeThickness = 1.5, IsHitTestVisible = false,
    };

    private TextBlock CreateTickLabel(StyledProperty<string> label, double tickX)
    {
        var text = new TextBlock
        {
            FontSize = 9, Foreground = TickLabelBrush, IsHitTestVisible = false,
            [!TextBlock.TextProperty] = this[!label],
            [AnimCanvas.TopProperty] = TrackY + 8,
            [AnimCanvas.LeftProperty] = tickX,
        };
        // Centre the label under its tick once its width is known.
        text.PropertyChanged += (_, args) =>
        {
            if (args.Property == BoundsProperty && text.Bounds.Width > 0)
                text.SetValue(AnimCanvas.LeftProperty, tickX - text.Bounds.Width / 2);
        };
        return text;
    }
}
