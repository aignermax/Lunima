using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Reusable help-flyout primitive (#1152): a mini plot — a fixed curve with a marker
/// dot that sweeps along it once per loop. Use it wherever help text would otherwise
/// describe a curve in words: a laser line shape (marker sweeps the spectrum peak),
/// a noisy power-over-time trace (RIN), an MZI output swinging with phase, …
/// Set <c>CurvePoints="x,y x,y …"</c> in the control's coordinate space.
/// <see cref="PingPong"/> = true sweeps there-and-back (good for spectra), false
/// wraps to the start (good for time traces). Compose in AXAML:
/// <code>&lt;ha:ParameterSweepMiniPlot Width="240" Height="44" CurvePoints="0,40 120,4 240,40"
///     MarkerBrush="#FFD54F" PingPong="True" IsHitTestVisible="False"/&gt;</code>
/// </summary>
public class ParameterSweepMiniPlot : HelpAnimationBase
{
    /// <summary>Polyline of the plotted curve ("x,y x,y …" in canvas coordinates).</summary>
    public static readonly StyledProperty<Points> CurvePointsProperty =
        AvaloniaProperty.Register<ParameterSweepMiniPlot, Points>(nameof(CurvePoints), new Points());

    /// <summary>Brush of the plotted curve.</summary>
    public static readonly StyledProperty<IBrush> CurveBrushProperty =
        AvaloniaProperty.Register<ParameterSweepMiniPlot, IBrush>(nameof(CurveBrush), new SolidColorBrush(0xFF9CDCFE));

    /// <summary>Brush of the sweeping marker dot.</summary>
    public static readonly StyledProperty<IBrush> MarkerBrushProperty =
        AvaloniaProperty.Register<ParameterSweepMiniPlot, IBrush>(nameof(MarkerBrush), new SolidColorBrush(0xFFFFD54F));

    /// <summary>Edge length of the (circular) marker dot, in canvas units.</summary>
    public static readonly StyledProperty<double> MarkerDiameterProperty =
        AvaloniaProperty.Register<ParameterSweepMiniPlot, double>(nameof(MarkerDiameter), 8.0);

    /// <summary>True: sweep there-and-back; false: wrap to the start after each sweep.</summary>
    public static readonly StyledProperty<bool> PingPongProperty =
        AvaloniaProperty.Register<ParameterSweepMiniPlot, bool>(nameof(PingPong));

    private readonly Polyline _curve;
    private readonly Ellipse _marker;

    /// <summary>Builds the curve and marker shapes; property changes re-render the current frame.</summary>
    public ParameterSweepMiniPlot()
    {
        _curve = new Polyline
        {
            Points = CurvePoints,
            Stroke = CurveBrush,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _marker = new Ellipse
        {
            Fill = MarkerBrush,
            Width = MarkerDiameter,
            Height = MarkerDiameter,
            IsHitTestVisible = false,
        };
        Content = new AnimCanvas { Children = { _curve, _marker }, IsHitTestVisible = false };
    }

    /// <summary>Polyline of the plotted curve ("x,y x,y …" in canvas coordinates).</summary>
    public Points CurvePoints
    {
        get => GetValue(CurvePointsProperty);
        set => SetValue(CurvePointsProperty, value);
    }

    /// <summary>Brush of the plotted curve.</summary>
    public IBrush CurveBrush
    {
        get => GetValue(CurveBrushProperty);
        set => SetValue(CurveBrushProperty, value);
    }

    /// <summary>Brush of the sweeping marker dot.</summary>
    public IBrush MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>Edge length of the (circular) marker dot, in canvas units.</summary>
    public double MarkerDiameter
    {
        get => GetValue(MarkerDiameterProperty);
        set => SetValue(MarkerDiameterProperty, value);
    }

    /// <summary>True: sweep there-and-back; false: wrap to the start after each sweep.</summary>
    public bool PingPong
    {
        get => GetValue(PingPongProperty);
        set => SetValue(PingPongProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurvePointsProperty)
        {
            _curve.Points = change.GetNewValue<Points>();
            RenderFrame(Progress);
        }
        else if (change.Property == CurveBrushProperty)
        {
            _curve.Stroke = change.GetNewValue<IBrush>();
        }
        else if (change.Property == MarkerBrushProperty)
        {
            _marker.Fill = change.GetNewValue<IBrush>();
        }
        else if (change.Property == MarkerDiameterProperty)
        {
            _marker.Width = _marker.Height = change.GetNewValue<double>();
            RenderFrame(Progress);
        }
        else if (change.Property == PingPongProperty)
        {
            RenderFrame(Progress);
        }
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        if (CurvePoints.Count < 2)
        {
            _marker.IsVisible = false;
            return;
        }

        double t = PingPong && progress > 0.5 ? 2.0 - progress * 2.0 : progress * (PingPong ? 2.0 : 1.0);
        var position = PolylineInterpolation.PointAt(CurvePoints, t);
        _marker.IsVisible = true;
        AnimCanvas.SetLeft(_marker, position.X - _marker.Width / 2);
        AnimCanvas.SetTop(_marker, position.Y - _marker.Height / 2);
    }
}
