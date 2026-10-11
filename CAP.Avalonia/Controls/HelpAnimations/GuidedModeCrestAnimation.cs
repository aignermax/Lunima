using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AnimCanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Looping diagram for the Mode Probe help flyout (#1486). Left: a waveguide
/// cross-section with the mode-intensity spot confined in the core. Right: the same
/// guide from the side — a traveling wave whose marker rides one crest along the
/// guide, while a fainter reference crest in the free-space lane above outruns it
/// (phase moves at c/n_eff, slower than c). Illustration only, not a solver call; every
/// position is a pure function of <see cref="HelpAnimationBase.Progress"/>, so tests
/// scrub Progress with AutoPlay off for deterministic frames. Draws in a fixed 336×140
/// coordinate space; the lane captions are settable for localization.
/// </summary>
public class GuidedModeCrestAnimation : HelpAnimationBase
{
    /// <summary>Width of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;
    /// <summary>Height of the coordinate space the control draws in.</summary>
    public const double SceneHeight = 140;

    /// <summary>Loop positions of the showcase phases: crest at start / mid / far.</summary>
    public static readonly double[] ShowcasePhases = { 0.0, 0.33, 0.66 };

    /// <summary>Caption under the side-view guide lane (the slower, guided crest).</summary>
    public static readonly StyledProperty<string> GuidedLabelProperty =
        AvaloniaProperty.Register<GuidedModeCrestAnimation, string>(nameof(GuidedLabel), "crest in the guide");

    /// <summary>Caption above the free-space lane (the faster reference crest).</summary>
    public static readonly StyledProperty<string> FreeSpaceLabelProperty =
        AvaloniaProperty.Register<GuidedModeCrestAnimation, string>(nameof(FreeSpaceLabel), "crest in free space");

    /// <summary>Caption under the cross-section (the confined mode spot).</summary>
    public static readonly StyledProperty<string> SpotLabelProperty =
        AvaloniaProperty.Register<GuidedModeCrestAnimation, string>(nameof(SpotLabel), "mode spot");

    // Cross-section geometry (left).
    private const double SectionLeft = 8;
    private const double SectionTop = 8;
    private const double SectionSize = 100;
    private const double SectionCenterX = SectionLeft + SectionSize / 2;
    private const double SectionCenterY = SectionTop + SectionSize / 2;
    private const double CoreWidth = 56;
    private const double CoreHeight = 36;
    private const double BlobMinDiameter = 26;
    private const double BlobMaxDiameter = 36;

    // Side-view lanes (right): guide band below, free-space lane above.
    private const double LaneLeft = 148;
    private const double LaneWidth = 180;
    private const double GuideTop = 52;
    private const double GuideHeight = 32;
    private const double GuideCenterY = GuideTop + GuideHeight / 2;
    private const double FreeLaneY = 26;
    private const double WaveAmplitude = 11;
    private const double WaveStepX = 3;
    private const double GuidedCrestDiameter = 9;
    private const double FreeCrestDiameter = 7;

    // Arrow from the cross-section to the side view.
    private const double ArrowLeft = 114;
    private const double ArrowRight = 138;
    private const double ArrowY = 58;
    private const double ArrowHeadSize = 5;

    // Crest speed = WavePhaseCyclesPerLoop / WaveCyclesAcrossGuide = one guide width
    // per loop, so the guided marker (offset 1/8 lane) rides one crest the whole way
    // and returns to its start at Progress = 1. The free-space crest wraps three times
    // per loop and visibly outruns it; both loops stay seamless.
    private const double WaveCyclesAcrossGuide = 2.0;
    private const double WavePhaseCyclesPerLoop = 2.0;
    private const double CrestLaneOffset = 0.125;
    private const int FreeTraversalsPerLoop = 3;

    private static readonly IBrush CladdingBrush = new ImmutableSolidColorBrush(0xFF1F2A3D);
    private static readonly IBrush CladdingBorderBrush = new ImmutableSolidColorBrush(0xFF3D4A63);
    private static readonly IBrush CoreFillBrush = new ImmutableSolidColorBrush(0x554FC3F7);
    private static readonly IBrush CoreBorderBrush = new ImmutableSolidColorBrush(0xFF4FC3F7);
    private static readonly IBrush CrestBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush FreeCrestBrush = new ImmutableSolidColorBrush(0xFF90A4C0);
    private static readonly IBrush TrackBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(0xFF888888);

    private readonly Ellipse _blob;
    private readonly Polyline _wave;
    private readonly Ellipse _guidedCrest;
    private readonly Ellipse _freeCrest;

    /// <summary>Builds the cross-section, the side-view lanes and the crest markers.</summary>
    public GuidedModeCrestAnimation()
    {
        var canvas = new AnimCanvas { Width = SceneWidth, Height = SceneHeight, IsHitTestVisible = false };

        canvas.Children.Add(new Rectangle
        {
            Width = SectionSize, Height = SectionSize,
            Fill = CladdingBrush, Stroke = CladdingBorderBrush, StrokeThickness = 1, IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = SectionLeft, [AnimCanvas.TopProperty] = SectionTop,
        });
        canvas.Children.Add(new Rectangle
        {
            Width = CoreWidth, Height = CoreHeight,
            Fill = CoreFillBrush, Stroke = CoreBorderBrush, StrokeThickness = 1.5, IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = SectionCenterX - CoreWidth / 2,
            [AnimCanvas.TopProperty] = SectionCenterY - CoreHeight / 2,
        });

        _blob = new Ellipse { Fill = CreateBlobBrush(), IsHitTestVisible = false };
        canvas.Children.Add(_blob);
        canvas.Children.Add(CreateArrow());

        canvas.Children.Add(new Line
        {
            StartPoint = new Point(LaneLeft, FreeLaneY), EndPoint = new Point(LaneLeft + LaneWidth, FreeLaneY),
            Stroke = TrackBrush, StrokeThickness = 1.5,
            StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 3, 3 },
            IsHitTestVisible = false,
        });
        canvas.Children.Add(new Rectangle
        {
            Width = LaneWidth, Height = GuideHeight,
            Fill = CoreFillBrush, Stroke = CoreBorderBrush, StrokeThickness = 1, IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = LaneLeft, [AnimCanvas.TopProperty] = GuideTop,
        });

        _wave = new Polyline { Stroke = CrestBrush, StrokeThickness = 1.5, IsHitTestVisible = false };
        _guidedCrest = CreateDot(GuidedCrestDiameter, CrestBrush);
        _freeCrest = CreateDot(FreeCrestDiameter, FreeCrestBrush);
        canvas.Children.Add(_wave);
        canvas.Children.Add(_guidedCrest);
        canvas.Children.Add(_freeCrest);

        canvas.Children.Add(CreateCaption(SpotLabelProperty, SectionCenterX, SectionTop + SectionSize + 4));
        canvas.Children.Add(CreateCaption(GuidedLabelProperty, LaneLeft + LaneWidth / 2, GuideTop + GuideHeight + 8));
        canvas.Children.Add(CreateCaption(FreeSpaceLabelProperty, LaneLeft + LaneWidth / 2, FreeLaneY - 18));

        Content = canvas;
        Width = SceneWidth;
        Height = SceneHeight;
    }

    /// <summary>Caption under the side-view guide lane (the slower, guided crest).</summary>
    public string GuidedLabel
    {
        get => GetValue(GuidedLabelProperty);
        set => SetValue(GuidedLabelProperty, value);
    }

    /// <summary>Caption above the free-space lane (the faster reference crest).</summary>
    public string FreeSpaceLabel
    {
        get => GetValue(FreeSpaceLabelProperty);
        set => SetValue(FreeSpaceLabelProperty, value);
    }

    /// <summary>Caption under the cross-section (the confined mode spot).</summary>
    public string SpotLabel
    {
        get => GetValue(SpotLabelProperty);
        set => SetValue(SpotLabelProperty, value);
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        // sin²(π·p): the spot breathes gently and returns to its start size — a seamless loop.
        double pulse = Math.Sin(Math.PI * progress);
        pulse *= pulse;
        double blobDiameter = BlobMinDiameter + (BlobMaxDiameter - BlobMinDiameter) * pulse;
        _blob.Width = blobDiameter;
        _blob.Height = blobDiameter;
        _blob.SetValue(AnimCanvas.LeftProperty, SectionCenterX - blobDiameter / 2);
        _blob.SetValue(AnimCanvas.TopProperty, SectionCenterY - blobDiameter / 2);

        _wave.Points = BuildTravelingWavePoints(progress);

        // Rides the top of one wave crest along the guide (one traversal per loop).
        double guidedX = LaneLeft + LaneWidth * ((progress + CrestLaneOffset) % 1.0);
        _guidedCrest.SetValue(AnimCanvas.LeftProperty, guidedX - GuidedCrestDiameter / 2);
        _guidedCrest.SetValue(AnimCanvas.TopProperty, GuideCenterY - WaveAmplitude - GuidedCrestDiameter / 2);

        double freeX = LaneLeft + LaneWidth * ((FreeTraversalsPerLoop * progress) % 1.0);
        _freeCrest.SetValue(AnimCanvas.LeftProperty, freeX - FreeCrestDiameter / 2);
        _freeCrest.SetValue(AnimCanvas.TopProperty, FreeLaneY - FreeCrestDiameter / 2);
    }

    /// <summary>A sine wave inside the guide band whose phase advances with the loop.</summary>
    private static Point[] BuildTravelingWavePoints(double progress)
    {
        double phase = 2 * Math.PI * WavePhaseCyclesPerLoop * progress;
        int count = (int)(LaneWidth / WaveStepX) + 1;
        var points = new Point[count];
        for (int i = 0; i < count; i++)
        {
            double x = LaneLeft + i * WaveStepX;
            double u = (x - LaneLeft) / LaneWidth;
            double y = GuideCenterY - WaveAmplitude * Math.Sin(2 * Math.PI * WaveCyclesAcrossGuide * u - phase);
            points[i] = new Point(x, y);
        }
        return points;
    }

    private static IBrush CreateBlobBrush() => new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromUInt32(0xE6FFF176), 0),
            new GradientStop(Color.FromUInt32(0x00FFF176), 1),
        },
    };

    private static Ellipse CreateDot(double diameter, IBrush fill) => new()
    {
        Width = diameter, Height = diameter, Fill = fill, IsHitTestVisible = false,
    };

    private static Polyline CreateArrow() => new()
    {
        Points = new Points
        {
            new(ArrowLeft, ArrowY), new(ArrowRight, ArrowY),
            new(ArrowRight - ArrowHeadSize, ArrowY - ArrowHeadSize), new(ArrowRight, ArrowY),
            new(ArrowRight - ArrowHeadSize, ArrowY + ArrowHeadSize),
        },
        Stroke = TrackBrush, StrokeThickness = 2, IsHitTestVisible = false,
    };

    private TextBlock CreateCaption(StyledProperty<string> label, double centerX, double top)
    {
        var text = new TextBlock
        {
            FontSize = 9, Foreground = LabelBrush, IsHitTestVisible = false,
            [!TextBlock.TextProperty] = this[!label],
            [AnimCanvas.TopProperty] = top,
        };
        // Centre the caption on its lane once its width is known.
        text.PropertyChanged += (_, args) =>
        {
            if (args.Property == BoundsProperty && text.Bounds.Width > 0)
                text.SetValue(AnimCanvas.LeftProperty, centerX - text.Bounds.Width / 2);
        };
        return text;
    }
}
