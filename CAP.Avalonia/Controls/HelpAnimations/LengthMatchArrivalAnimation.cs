using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CAP_Core.Analysis.LogicAnalysis;
using AnimCanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Looping diagram of the Length Matching help (#1256): two light pulses leave a splitter
/// together; the short straight arm delivers its pulse to the combiner first, the longer
/// detour arm later — the arrival-time gap is the phase slip. Then the short arm grows a
/// meander (length matched) and a second pair arrives together. The ΔL / Δt readout uses
/// the real group-delay relation Δt = ΔL·n_g/c with the core's
/// <see cref="GateDelayCalculator.DefaultGroupIndex"/> and
/// <see cref="GateDelayCalculator.SpeedOfLightMicrometersPerPicosecond"/> — never a
/// hard-coded number, so the help can never drift from the simulation. Panel-specific,
/// not a reusable primitive: a pure function of <see cref="HelpAnimationBase.Progress"/>;
/// the last frame keeps the matched end state visible. Both pulses travel at the same
/// pixel speed in both runs — light speed does not change when the meander appears.
/// </summary>
public class LengthMatchArrivalAnimation : HelpAnimationBase
{
    /// <summary>Size of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;
    public const double SceneHeight = 96;

    /// <summary>Length difference between the arms before matching, in micrometers.</summary>
    public const double MaxLengthMismatchMicrometers = 200;

    // Geometry: the straight (short) arm spans 288 px, the detour (long) arm 332 px; the
    // grown meander adds exactly the 44 px difference — the drawing itself conserves length.
    private const double ArmStartX = 20;
    private const double ArmEndX = 308;
    private const double ShortArmY = 30;
    private const double LongArmY = 66;

    private static readonly Points StraightArmPoints = new() { new(ArmStartX, ShortArmY), new(ArmEndX, ShortArmY) };

    private static readonly Points LongArmPoints = new()
    {
        new(ArmStartX, LongArmY), new(60, LongArmY), new(60, 88),
        new(268, 88), new(268, LongArmY), new(ArmEndX, LongArmY),
    };

    private static readonly Points MeanderArmPoints = new()
    {
        new(ArmStartX, ShortArmY), new(90, ShortArmY), new(90, 41), new(140, 41), new(140, ShortArmY),
        new(188, ShortArmY), new(188, 41), new(238, 41), new(238, ShortArmY), new(ArmEndX, ShortArmY),
    };

    private const double StraightArmLengthPx = ArmEndX - ArmStartX;
    private const double LongArmLengthPx = 332;
    private const double PulseSpeedPxPerLoop = 1040;

    // Cue windows: run 1 (mismatched) — grow the meander — run 2 (matched), then hold.
    private const double Run1Start = 0.02;
    private static readonly (double Start, double End) GrowWindow = (0.36, 0.52);
    private const double Run2Start = 0.56;

    /// <summary>Loop positions of the showcase phases: in flight / phase slip / matched.</summary>
    public static readonly double[] ShowcasePhases = { 0.15, 0.32, 1.0 };

    private const double SplitterWidth = 12;
    private const double SplitterTop = 24;
    private const double SplitterHeight = 48;
    private const double PulseDiameter = 8;
    private const double FlashDiameter = 12;
    private const double ReadoutLeft = 130;
    private const double ReadoutTop = 4;

    private static readonly IBrush TrackBrush = new SolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush MeanderTrackBrush = new SolidColorBrush(0xFF6D6D5A);
    private static readonly IBrush PulseBrush = new SolidColorBrush(0xFFFFF176);
    private static readonly IBrush FlashBrush = new SolidColorBrush(0xFF81C784);
    private static readonly IBrush SplitterBrush = new SolidColorBrush(0xFF23232B);
    private static readonly IBrush SplitterBorderBrush = new SolidColorBrush(0xFF5A5A60);
    private static readonly IBrush LabelBrush = new SolidColorBrush(0xFFCFCFD6);

    private readonly Polyline _straightTrack;
    private readonly Polyline _meanderTrack;
    private readonly Ellipse _pulseShort;
    private readonly Ellipse _pulseLong;
    private readonly Ellipse _flashShort;
    private readonly Ellipse _flashLong;
    private readonly TextBlock _readout;

    /// <summary>Builds the splitter/combiner, both arm tracks, the pulses and the readout.</summary>
    public LengthMatchArrivalAnimation()
    {
        var canvas = new AnimCanvas
        {
            Width = SceneWidth,
            Height = SceneHeight,
            IsHitTestVisible = false,
        };

        _straightTrack = CreateTrack(StraightArmPoints, TrackBrush);
        _meanderTrack = CreateTrack(new Points(), MeanderTrackBrush);
        _flashShort = CreateDot(FlashDiameter, FlashBrush, ShortArmY);
        _flashLong = CreateDot(FlashDiameter, FlashBrush, LongArmY);
        _pulseShort = CreateDot(PulseDiameter, PulseBrush, null);
        _pulseLong = CreateDot(PulseDiameter, PulseBrush, null);
        _readout = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = LabelBrush,
            IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = ReadoutLeft,
            [AnimCanvas.TopProperty] = ReadoutTop,
        };
        canvas.Children.AddRange(new Control[]
        {
            CreateSplitter(ArmStartX - SplitterWidth), CreateSplitter(ArmEndX),
            _straightTrack, _meanderTrack, CreateTrack(LongArmPoints, TrackBrush),
            _flashShort, _flashLong, _pulseShort, _pulseLong, _readout,
        });

        Content = canvas;
        RenderFrame(0);
    }

    /// <summary>Grown fraction of the meander (0 = straight short arm, 1 = length matched).</summary>
    public static double MeanderFillAt(double progress) =>
        Ramp(progress, GrowWindow.Start, GrowWindow.End);

    /// <summary>Arm length difference (µm) at a loop position: shrinks as the meander grows.</summary>
    public static double LengthMismatchMicrometersAt(double progress) =>
        MaxLengthMismatchMicrometers * (1 - MeanderFillAt(progress));

    /// <summary>
    /// Arrival-time gap (ps) for a length mismatch: Δt = ΔL·n_g/c with the core's default
    /// silicon-strip group index — the convention <see cref="WireDelayCalculator"/> applies.
    /// </summary>
    public static double GroupDelayPicoseconds(double lengthMismatchMicrometers) =>
        lengthMismatchMicrometers * GateDelayCalculator.DefaultGroupIndex
        / GateDelayCalculator.SpeedOfLightMicrometersPerPicosecond;

    /// <summary>Arrival-time gap (ps) between the arms at a loop position.</summary>
    public static double DelayPicosecondsAt(double progress) =>
        GroupDelayPicoseconds(LengthMismatchMicrometersAt(progress));

    /// <summary>The readout text shown above the diagram (invariant culture).</summary>
    public static string ReadoutText(double progress) =>
        string.Create(CultureInfo.InvariantCulture,
            $"ΔL = {LengthMismatchMicrometersAt(progress):0} µm · Δt = {DelayPicosecondsAt(progress):0.00} ps");

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var fill = MeanderFillAt(progress);
        _straightTrack.Opacity = 1 - fill;
        _meanderTrack.Points = PartialPolyline(MeanderArmPoints, fill);

        var run2 = progress >= Run2Start;
        var travelled = (progress - (run2 ? Run2Start : Run1Start)) * PulseSpeedPxPerLoop;
        var topLength = run2 ? LongArmLengthPx : StraightArmLengthPx;
        var topPath = run2 ? MeanderArmPoints : StraightArmPoints;

        PositionPulse(_pulseShort, topPath, travelled, topLength);
        PositionPulse(_pulseLong, LongArmPoints, travelled, LongArmLengthPx);
        _flashShort.IsVisible = travelled >= topLength;
        _flashLong.IsVisible = travelled >= LongArmLengthPx;

        _readout.Text = ReadoutText(progress);
    }

    private static void PositionPulse(Ellipse pulse, Points path, double travelledPx, double pathLengthPx)
    {
        if (travelledPx < 0)
        {
            pulse.IsVisible = false;
            return;
        }
        var position = PolylineInterpolation.PointAt(path, Math.Min(1, travelledPx / pathLengthPx));
        pulse.IsVisible = true;
        AnimCanvas.SetLeft(pulse, position.X - PulseDiameter / 2);
        AnimCanvas.SetTop(pulse, position.Y - PulseDiameter / 2);
    }

    /// <summary>The first <paramref name="fraction"/> of a polyline, measured by length (meander grows).</summary>
    private static Points PartialPolyline(Points full, double fraction)
    {
        if (fraction <= 0)
            return new Points();
        if (fraction >= 1)
            return new Points(full);

        var total = 0.0;
        for (var i = 1; i < full.Count; i++)
            total += Distance(full[i - 1], full[i]);

        var grown = new Points { full[0] };
        var covered = 0.0;
        for (var i = 1; i < full.Count && covered < total * fraction; i++)
        {
            covered += Distance(full[i - 1], full[i]);
            if (covered < total * fraction)
                grown.Add(full[i]);
        }
        grown.Add(PolylineInterpolation.PointAt(full, fraction));
        return grown;
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static Polyline CreateTrack(Points points, IBrush brush) => new()
        { Points = points, Stroke = brush, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false };

    /// <summary>A pulse (moved per frame) or an arrival flash pinned to the combiner port at <paramref name="armY"/>.</summary>
    private static Ellipse CreateDot(double diameter, IBrush brush, double? armY)
    {
        var dot = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        if (armY.HasValue)
        {
            dot[AnimCanvas.LeftProperty] = ArmEndX - diameter / 2;
            dot[AnimCanvas.TopProperty] = armY.Value - diameter / 2;
        }
        return dot;
    }

    private static Border CreateSplitter(double left) => new()
    {
        Width = SplitterWidth,
        Height = SplitterHeight,
        Background = SplitterBrush,
        BorderBrush = SplitterBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        IsHitTestVisible = false,
        [AnimCanvas.LeftProperty] = left,
        [AnimCanvas.TopProperty] = SplitterTop,
    };
}
