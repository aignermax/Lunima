using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Self-contained scene of the Monte Carlo help flyout (#1344), in three lanes:
/// a waveguide cross-section whose width wobbles (fabrication variance), a small
/// transmission dip that shifts with the width (effective index → phase/loss), and
/// dots dropping into a histogram that slowly builds a bell shape (virtual chips
/// → yield). Draws in its own fixed 368×150 space; the dot bins come from a
/// fixed-seed generator, so every loop and screenshot builds the same histogram.
/// </summary>
public class MonteCarloScatterAnimation : HelpAnimationBase
{
    /// <summary>Loop positions worth a screenshot: first chips, half-built, full bell.</summary>
    public static double[] ShowcasePhases { get; } = { 0.15, 0.5, 0.95 };

    private const int WobbleCycles = 4;
    private const int DotCount = 32;
    private const int BinCount = 8;
    private const int ScatterSeed = 1344;

    // Cross-section lane.
    private const double CladdingX = 20;
    private const double CladdingY = 40;
    private const double CladdingWidth = 80;
    private const double CladdingHeight = 56;
    private const double CoreCenterX = CladdingX + CladdingWidth / 2;
    private const double CoreCenterY = CladdingY + CladdingHeight / 2;
    private const double CoreHeight = 14;
    private const double CoreMeanWidth = 40;
    private const double CoreWidthAmplitude = 10;

    // Transmission lane: a dip that slides sideways with the width wobble.
    private const double CurveLeft = 152;
    private const double CurveRight = 252;
    private const double CurveTopY = 52;
    private const double CurveHeight = 52;
    private const double CurveDipDepth = 0.8;
    private const double CurveDipWidth = 0.18;
    private const double CurveShiftAmplitude = 8;
    private const double MarkerDiameter = 7;

    // Histogram lane.
    private const double HistAxisX = 264;
    private const double HistBaseY = 136;
    private const double HistTopY = 40;
    private const double HistBinWidth = 12;
    private const double HistStartX = 266;
    private const double DotDiameter = 8;
    private const double DotPitch = 9;
    private const double DotWindowStart = 0.05;
    private const double DotWindowSpan = 0.75;
    private const double DotFade = 0.05;

    private static readonly IBrush CladdingBrush = new ImmutableSolidColorBrush(0xFF23232B);
    private static readonly IBrush CladdingBorderBrush = new ImmutableSolidColorBrush(0xFF5A5A60);
    private static readonly IBrush CoreBrush = new ImmutableSolidColorBrush(0xFFFF8A65);
    private static readonly IBrush AxisBrush = new ImmutableSolidColorBrush(0xFF4D4D4D);
    private static readonly IBrush CurveBrush = new ImmutableSolidColorBrush(0xFF4FC3F7);
    private static readonly IBrush DotBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush NeutralBrush = new ImmutableSolidColorBrush(0xFFCFCFD6);

    private readonly Rectangle _core;
    private readonly Polyline _curve;
    private readonly Ellipse _marker;
    private readonly Ellipse[] _dots;
    private readonly Polyline _bell;

    /// <summary>Builds the cross-section, the transmission dip and the empty histogram.</summary>
    public MonteCarloScatterAnimation()
    {
        _core = new Rectangle
        {
            Fill = CoreBrush,
            Height = CoreHeight,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetTop(_core, CoreCenterY - CoreHeight / 2);

        _curve = BuildDipCurve();
        _marker = new Ellipse
        {
            Fill = CurveBrush,
            Width = MarkerDiameter,
            Height = MarkerDiameter,
            IsHitTestVisible = false,
        };
        (_dots, _bell) = BuildHistogram();

        var cladding = new Rectangle
        {
            Fill = CladdingBrush,
            Stroke = CladdingBorderBrush,
            StrokeThickness = 1,
            Width = CladdingWidth,
            Height = CladdingHeight,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(cladding, CladdingX);
        AnimCanvas.SetTop(cladding, CladdingY);
        var widthLabel = new TextBlock
        {
            Text = "w",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = NeutralBrush,
            IsHitTestVisible = false,
        };
        AnimCanvas.SetLeft(widthLabel, CoreCenterX - 4);
        AnimCanvas.SetTop(widthLabel, CladdingY - 20);

        var canvas = new AnimCanvas { IsHitTestVisible = false };
        canvas.Children.Add(cladding);
        canvas.Children.Add(widthLabel);
        canvas.Children.Add(_core);
        AddAxes(canvas);
        canvas.Children.Add(_curve);
        canvas.Children.Add(_marker);
        canvas.Children.Add(_bell);
        canvas.Children.AddRange(_dots);
        Content = canvas;
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        double wobble = Math.Sin(2.0 * Math.PI * WobbleCycles * progress);
        double coreWidth = CoreMeanWidth + CoreWidthAmplitude * wobble;
        _core.Width = coreWidth;
        AnimCanvas.SetLeft(_core, CoreCenterX - coreWidth / 2);

        double shift = CurveShiftAmplitude * wobble;
        AnimCanvas.SetLeft(_curve, shift);
        double dipY = CurveTopY + CurveHeight * (1.0 - (1.0 - CurveDipDepth));
        AnimCanvas.SetLeft(_marker, (CurveLeft + CurveRight) / 2 + shift - MarkerDiameter / 2);
        AnimCanvas.SetTop(_marker, dipY - MarkerDiameter / 2);

        for (int i = 0; i < _dots.Length; i++)
        {
            double start = DotWindowStart + DotWindowSpan * i / (double)DotCount;
            _dots[i].Opacity = Ramp(progress, start, start + DotFade);
        }
        _bell.Opacity = 0.8 * Ramp(progress, 0.85, 0.97);
    }

    private static Polyline BuildDipCurve()
    {
        var points = new Point[26];
        for (int i = 0; i < points.Length; i++)
        {
            double t = i / (double)(points.Length - 1);
            double level = 1.0 - CurveDipDepth * Math.Exp(-Square((t - 0.5) / CurveDipWidth));
            points[i] = new Point(
                CurveLeft + (CurveRight - CurveLeft) * t,
                CurveTopY + CurveHeight * (1.0 - level));
        }
        return new Polyline
        {
            Points = points,
            Stroke = CurveBrush,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
    }

    private static (Ellipse[] Dots, Polyline Bell) BuildHistogram()
    {
        var rng = new Random(ScatterSeed);
        var stackHeights = new int[BinCount];
        var dots = new Ellipse[DotCount];
        for (int i = 0; i < dots.Length; i++)
        {
            int bin = SampleBin(rng);
            int level = stackHeights[bin]++;
            dots[i] = new Ellipse
            {
                Fill = DotBrush,
                Width = DotDiameter,
                Height = DotDiameter,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            AnimCanvas.SetLeft(dots[i], HistStartX + bin * HistBinWidth + (HistBinWidth - DotDiameter) / 2);
            AnimCanvas.SetTop(dots[i], HistBaseY - DotDiameter - level * DotPitch);
        }

        int maxStack = Math.Max(1, stackHeights.Max());
        return (dots, BuildBellCurve(maxStack));
    }

    // Sum of four uniforms approximates a Gaussian, so the histogram ends bell-shaped.
    private static int SampleBin(Random rng)
    {
        double u = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 2.0) * 0.5;
        return Math.Clamp((int)Math.Round((u * 0.5 + 0.5) * (BinCount - 1)), 0, BinCount - 1);
    }

    private static Polyline BuildBellCurve(int maxStack)
    {
        double center = (BinCount - 1) / 2.0;
        double sigma = BinCount / 4.0;
        var points = new Point[(BinCount - 1) * 4 + 1];
        for (int i = 0; i < points.Length; i++)
        {
            double bin = i / 4.0;
            double height = maxStack * DotPitch * Math.Exp(-Square((bin - center) / sigma));
            points[i] = new Point(
                HistStartX + (bin + 0.5) * HistBinWidth,
                HistBaseY - height);
        }
        return new Polyline
        {
            Points = points,
            Stroke = NeutralBrush,
            StrokeThickness = 1.5,
            Opacity = 0,
            IsHitTestVisible = false,
        };
    }

    private static void AddAxes(AnimCanvas canvas)
    {
        AddLine(canvas, 148, CurveTopY - 4, 148, CurveTopY + CurveHeight);
        AddLine(canvas, 148, CurveTopY + CurveHeight, CurveRight + 4, CurveTopY + CurveHeight);
        AddLine(canvas, HistAxisX, HistTopY, HistAxisX, HistBaseY);
        AddLine(canvas, HistAxisX, HistBaseY, HistStartX + BinCount * HistBinWidth + 4, HistBaseY);
    }

    private static void AddLine(AnimCanvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = AxisBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });
    }

    private static double Square(double value) => value * value;
}
