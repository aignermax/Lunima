using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Illustrative "sweeping laser" animation for the ONA Analyzer help flyout (#1432).
/// Renders a stylised transmission curve with a handful of resonance dips and sweeps a
/// laser-coloured marker along the wavelength axis; whenever the marker hits a resonance
/// the small ring glyph next to the plot lights up.
/// The drawing is a pure function of <see cref="Progress"/> (0..1) — the built-in timer
/// only advances that property, and tests can set it directly to capture a specific phase.
/// </summary>
public class OnaSweepAnimation : Control
{
    /// <summary>Normalised animation phase (0 = left edge of the sweep, 1 = right edge).</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<OnaSweepAnimation, double>(nameof(Progress), 0d, coerce: CoerceProgress);

    /// <summary>
    /// True (default) to advance <see cref="Progress"/> automatically on a timer. Tests
    /// set this to <c>false</c> so a fixed <see cref="Progress"/> stays put across renders.
    /// </summary>
    public static readonly StyledProperty<bool> AutoAdvanceProperty =
        AvaloniaProperty.Register<OnaSweepAnimation, bool>(nameof(AutoAdvance), true);

    private const double SweepPeriodSeconds = 4.0;
    private const double TimerIntervalMs = 33.0;
    private const double ResonanceGlowRadius = 0.045;

    // Resonance positions along the [0,1] sweep axis — picked so phase 0.55 sits inside
    // the glow window of the middle dip for the screenshot test.
    private static readonly double[] Resonances = { 0.22, 0.55, 0.83 };
    private static readonly double[] ResonanceDepths = { 0.78, 0.95, 0.62 };
    private static readonly double[] ResonanceWidths = { 0.030, 0.024, 0.035 };

    private static readonly ImmutableSolidColorBrush BackgroundBrush = new(Color.FromRgb(0x15, 0x15, 0x15));
    private static readonly ImmutableSolidColorBrush AxisBrush = new(Color.FromRgb(0x44, 0x44, 0x4a));
    private static readonly ImmutableSolidColorBrush TargetCurveBrush = new(Color.FromRgb(0x3a, 0x3a, 0x44));
    private static readonly ImmutableSolidColorBrush SweepCurveBrush = new(Color.FromRgb(0x4f, 0xc3, 0xf7));
    private static readonly ImmutableSolidColorBrush LaserBrush = new(Color.FromRgb(0xff, 0xd5, 0x4f));
    private static readonly ImmutableSolidColorBrush RingDimBrush = new(Color.FromRgb(0x4a, 0x4a, 0x50));
    private static readonly ImmutableSolidColorBrush RingLitBrush = new(Color.FromRgb(0xff, 0xd5, 0x4f));

    private static readonly Pen AxisPen = new(AxisBrush, 1);
    private static readonly Pen TargetCurvePen = new(TargetCurveBrush, 1);
    private static readonly Pen SweepCurvePen = new(SweepCurveBrush, 2);
    private static readonly Pen LaserPen = new(LaserBrush, 1.5);
    private static readonly Pen RingDimPen = new(RingDimBrush, 1.5);
    private static readonly Pen RingLitPen = new(RingLitBrush, 2);

    private DispatcherTimer? _timer;

    static OnaSweepAnimation()
    {
        AffectsRender<OnaSweepAnimation>(ProgressProperty);
    }

    /// <summary>Normalised animation phase, clamped to [0,1].</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Whether the built-in timer advances <see cref="Progress"/> automatically.</summary>
    public bool AutoAdvance
    {
        get => GetValue(AutoAdvanceProperty);
        set => SetValue(AutoAdvanceProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimerIntervalMs) };
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width < 4 || bounds.Height < 4)
            return;

        context.FillRectangle(BackgroundBrush, bounds);

        var plot = ComputePlotRect(bounds);
        DrawAxes(context, plot);
        DrawCurve(context, plot, upTo: 1.0, TargetCurvePen);

        var phase = Progress;
        if (phase > 0)
            DrawCurve(context, plot, upTo: phase, SweepCurvePen);

        DrawLaserMarker(context, plot, phase);
        DrawResonanceRing(context, plot, phase);
    }

    private static double CoerceProgress(AvaloniaObject sender, double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!AutoAdvance)
            return;

        var step = TimerIntervalMs / 1000.0 / SweepPeriodSeconds;
        var next = Progress + step;
        Progress = next >= 1.0 ? 0 : next;
    }

    private static Rect ComputePlotRect(Rect bounds)
    {
        const double padX = 12;
        const double padTop = 10;
        const double padBottom = 14;
        return new Rect(
            bounds.X + padX,
            bounds.Y + padTop,
            Math.Max(1, bounds.Width - padX * 2),
            Math.Max(1, bounds.Height - padTop - padBottom));
    }

    private static void DrawAxes(DrawingContext context, Rect plot)
    {
        context.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
        context.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);
    }

    /// <summary>Draws the transmission curve up to <paramref name="upTo"/> fraction of the sweep.</summary>
    private static void DrawCurve(DrawingContext context, Rect plot, double upTo, IPen pen)
    {
        const int segments = 64;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var start = PointForPhase(plot, 0);
            ctx.BeginFigure(start, isFilled: false);
            for (int i = 1; i <= segments; i++)
            {
                var phase = upTo * i / segments;
                ctx.LineTo(PointForPhase(plot, phase));
            }
            ctx.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawLaserMarker(DrawingContext context, Rect plot, double phase)
    {
        var tip = PointForPhase(plot, phase);
        context.DrawLine(LaserPen, new Point(tip.X, plot.Bottom), tip);
        context.DrawEllipse(LaserBrush, null, tip, 3.5, 3.5);
    }

    /// <summary>Small ring next to the plot: lights up while the marker is on a resonance.</summary>
    private static void DrawResonanceRing(DrawingContext context, Rect plot, double phase)
    {
        const double ringRadius = 6;
        var centre = new Point(plot.Right + ringRadius / 2, plot.Top + ringRadius / 2);

        var lit = IsNearResonance(phase);
        var pen = lit ? RingLitPen : RingDimPen;
        var fill = lit ? RingLitBrush : null;

        context.DrawEllipse(fill, pen, centre, ringRadius, ringRadius);
    }

    private static bool IsNearResonance(double phase)
    {
        foreach (var resonance in Resonances)
        {
            if (Math.Abs(phase - resonance) < ResonanceGlowRadius)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Stylised transmission (1.0 at the top, ~0 at the deepest dip) — a sum of Lorentzian
    /// dips, purely illustrative (no simulation call).
    /// </summary>
    private static double Transmission(double phase)
    {
        var value = 1.0;
        for (int i = 0; i < Resonances.Length; i++)
        {
            var dx = (phase - Resonances[i]) / ResonanceWidths[i];
            value -= ResonanceDepths[i] / (1 + dx * dx);
        }
        return Math.Clamp(value, 0.04, 1.0);
    }

    private static Point PointForPhase(Rect plot, double phase)
    {
        var x = plot.X + plot.Width * phase;
        var y = plot.Y + plot.Height * (1 - Transmission(phase));
        return new Point(x, y);
    }
}
