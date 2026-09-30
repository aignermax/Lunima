using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations.Isa;

/// <summary>
/// Looping diagram of the ISA playground's "Why light adds" help section (#1237):
/// the carry-ripple of <c>0111 + 0001</c> through the four full adders FA0…FA3 of
/// the photonic 4-bit adder. A light pulse enters FA0 and the carry hops
/// FA0→FA1→FA2→FA3 (each box lights up while the carry is inside it); at the end
/// the sum bits flip to 1000. A small time bar at the bottom grows with every hop —
/// it fills in proportion to the light path the pulse has travelled, which is exactly
/// the picosecond number the playground's status line shows. Panel-specific, not a
/// reusable primitive: it draws in its own fixed 336×100 coordinate space. Every
/// position and opacity is a pure function of <see cref="HelpAnimationBase.Progress"/>;
/// the last frame keeps the finished state (sum 1000, FA3 lit, bar full) visible.
/// </summary>
public class CarryRippleAnimation : HelpAnimationBase
{
    /// <summary>Width of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;

    /// <summary>Height of the coordinate space the control draws in.</summary>
    public const double SceneHeight = 100;

    private const double BoxWidth = 62;
    private const double BoxHeight = 32;
    private const double BoxTop = 22;
    private const double SumSquareSize = 20;
    private const double SumSquareTop = 58;
    private const double PulseDiameter = 10;
    private const double BarHeight = 6;
    private const double BarTop = 88;
    private const double BarLeft = 11;
    private const double BarMaxWidth = 314;
    private const double MaxGlowOpacity = 0.45;

    private static readonly double[] BoxLefts = { 11, 95, 179, 263 };

    // Pulse travel: off-canvas left → FA0…FA3 centers → off-canvas right.
    private const double PulseEntryX = -8;
    private const double PulseExitX = 344;

    // Cue windows: the pulse enters FA0 (0.02…0.14), then hops to the next box
    // every 0.2 of the loop (0.24…0.36, 0.44…0.56, 0.64…0.76) and exits to the
    // right once the sum is complete (0.86…0.96). Between the windows the pulse
    // rests inside a box — the carry "is" in that full adder then.
    private static readonly (double Start, double End) EnterWindow = (0.02, 0.14);
    private static readonly (double Start, double End) HopToFa1 = (0.24, 0.36);
    private static readonly (double Start, double End) HopToFa2 = (0.44, 0.56);
    private static readonly (double Start, double End) HopToFa3 = (0.64, 0.76);
    private static readonly (double Start, double End) ExitWindow = (0.86, 0.96);

    // Each box glows while the carry is inside it; FA3 stays lit as the end state.
    private static readonly (double InStart, double InEnd)[] GlowFadeIns =
        { (0.08, 0.14), (0.30, 0.38), (0.50, 0.58), (0.70, 0.78) };
    private static readonly (double OutStart, double OutEnd)[] GlowFadeOuts =
        { (0.26, 0.36), (0.46, 0.56), (0.66, 0.76), (2.0, 2.1) };

    private const double SumFlipAt = 0.88;
    private static readonly string[] SumFinalBits = { "0", "0", "0", "1" };

    private static readonly IBrush BoxBrush = new SolidColorBrush(0xFF23232B);
    private static readonly IBrush BoxBorderBrush = new SolidColorBrush(0xFF5A5A60);
    private static readonly IBrush PulseBrush = new SolidColorBrush(0xFFFFF176);
    private static readonly IBrush BarBrush = new SolidColorBrush(0xFF7EC87E);
    private static readonly IBrush SumDoneBrush = new SolidColorBrush(0xFF3D5D3D);
    private static readonly IBrush LabelBrush = new SolidColorBrush(0xFFCFCFD6);

    private readonly Border[] _glows = new Border[4];
    private readonly Border[] _sumSquares = new Border[4];
    private readonly TextBlock[] _sumTexts = new TextBlock[4];
    private readonly Ellipse _pulse;
    private readonly Rectangle _timeBar;

    /// <summary>Builds the four adder boxes, the sum row, the pulse and the time bar.</summary>
    public CarryRippleAnimation()
    {
        var canvas = new AnimCanvas
        {
            Width = SceneWidth,
            Height = SceneHeight,
            IsHitTestVisible = false,
        };

        canvas.Children.Add(new TextBlock
        {
            Text = "0111 + 0001",
            FontSize = 11,
            Foreground = LabelBrush,
            [AnimCanvas.LeftProperty] = BoxLefts[0],
            [AnimCanvas.TopProperty] = 2.0,
        });

        for (var i = 0; i < 4; i++)
        {
            canvas.Children.Add(CreateBox(i));
            _glows[i] = CreateGlow(i);
            canvas.Children.Add(_glows[i]);
            (_sumSquares[i], _sumTexts[i]) = CreateSumSquare(i);
            canvas.Children.Add(_sumSquares[i]);
        }

        _timeBar = new Rectangle
        {
            Height = BarHeight,
            Width = 0,
            Fill = BarBrush,
            RadiusX = 2,
            RadiusY = 2,
            IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = BarLeft,
            [AnimCanvas.TopProperty] = BarTop,
        };
        canvas.Children.Add(_timeBar);

        _pulse = new Ellipse
        {
            Width = PulseDiameter,
            Height = PulseDiameter,
            Fill = PulseBrush,
            IsHitTestVisible = false,
            [AnimCanvas.TopProperty] = BoxTop + (BoxHeight - PulseDiameter) / 2,
        };
        canvas.Children.Add(_pulse);

        Content = canvas;
        RenderFrame(0);
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var pulseX = PulsePosition(progress);
        AnimCanvas.SetLeft(_pulse, pulseX - PulseDiameter / 2);

        for (var i = 0; i < 4; i++)
        {
            var glow = Ramp(progress, GlowFadeIns[i].InStart, GlowFadeIns[i].InEnd)
                * (1 - Ramp(progress, GlowFadeOuts[i].OutStart, GlowFadeOuts[i].OutEnd));
            _glows[i].Opacity = glow * MaxGlowOpacity;

            var flipped = progress >= SumFlipAt;
            _sumTexts[i].Text = flipped ? SumFinalBits[i] : "0";
            _sumSquares[i].Background = flipped && SumFinalBits[i] == "1" ? SumDoneBrush : BoxBrush;
        }

        // The bar fills in proportion to the light path travelled so far — it only
        // grows while the pulse moves, i.e. with every carry hop.
        var travelled = (pulseX - PulseEntryX) / (PulseExitX - PulseEntryX);
        _timeBar.Width = BarMaxWidth * Math.Clamp(travelled, 0, 1);
        _timeBar.IsVisible = _timeBar.Width > 0.5;
    }

    private static double PulsePosition(double progress)
    {
        var x = Lerp(PulseEntryX, BoxCenterX(0), Ramp(progress, EnterWindow.Start, EnterWindow.End));
        x = Lerp(x, BoxCenterX(1), Ramp(progress, HopToFa1.Start, HopToFa1.End));
        x = Lerp(x, BoxCenterX(2), Ramp(progress, HopToFa2.Start, HopToFa2.End));
        x = Lerp(x, BoxCenterX(3), Ramp(progress, HopToFa3.Start, HopToFa3.End));
        return Lerp(x, PulseExitX, Ramp(progress, ExitWindow.Start, ExitWindow.End));
    }

    private static double BoxCenterX(int index) => BoxLefts[index] + BoxWidth / 2;

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static Border CreateBox(int index) => new()
    {
        Width = BoxWidth,
        Height = BoxHeight,
        Background = BoxBrush,
        BorderBrush = BoxBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        IsHitTestVisible = false,
        [AnimCanvas.LeftProperty] = BoxLefts[index],
        [AnimCanvas.TopProperty] = BoxTop,
        Child = new TextBlock
        {
            Text = $"FA{index}",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        },
    };

    private static Border CreateGlow(int index) => new()
    {
        Width = BoxWidth,
        Height = BoxHeight,
        Background = PulseBrush,
        CornerRadius = new CornerRadius(4),
        Opacity = 0,
        IsHitTestVisible = false,
        [AnimCanvas.LeftProperty] = BoxLefts[index],
        [AnimCanvas.TopProperty] = BoxTop,
    };

    private static (Border Square, TextBlock Text) CreateSumSquare(int index)
    {
        var text = new TextBlock
        {
            Text = "0",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = LabelBrush,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        var square = new Border
        {
            Width = SumSquareSize,
            Height = SumSquareSize,
            Background = BoxBrush,
            BorderBrush = BoxBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = BoxLefts[index] + (BoxWidth - SumSquareSize) / 2,
            [AnimCanvas.TopProperty] = SumSquareTop,
            Child = text,
        };
        return (square, text);
    }
}
