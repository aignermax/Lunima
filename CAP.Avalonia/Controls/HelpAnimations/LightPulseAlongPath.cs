using Avalonia;
using Avalonia.Controls;
using AnimCanvas = global::Avalonia.Controls.Canvas;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Reusable help-flyout primitive (#1152): a light pulse that travels along a
/// waveguide path once per loop, then loops. The path is drawn as a faint track so
/// the pulse reads as "light inside a waveguide". Compose several instances with
/// staggered <see cref="WindowStart"/>/<see cref="WindowEnd"/> cue windows to show
/// hand-offs — e.g. one pulse into a coupler (window 0…0.5) and two split pulses
/// leaving it (window 0.5…1), as in the Transient help flyout.
/// Set <c>PathPoints="x,y x,y …"</c> in the parent Canvas's coordinate space and
/// size the control to the region it may draw in. Compose in AXAML:
/// <code>&lt;ha:LightPulseAlongPath Width="380" Height="80" PathPoints="36,40 195,40"
///     PulseBrush="#FFF176" WindowStart="0" WindowEnd="0.5" IsHitTestVisible="False"/&gt;</code>
/// </summary>
public class LightPulseAlongPath : HelpAnimationBase
{
    /// <summary>Polyline the pulse follows ("x,y x,y …" in canvas coordinates).</summary>
    public static readonly StyledProperty<Points> PathPointsProperty =
        AvaloniaProperty.Register<LightPulseAlongPath, Points>(nameof(PathPoints), new Points());

    /// <summary>Brush of the faint waveguide track under the pulse.</summary>
    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<LightPulseAlongPath, IBrush>(nameof(TrackBrush), new SolidColorBrush(0xFF4D4D4D));

    /// <summary>Brush of the travelling light pulse.</summary>
    public static readonly StyledProperty<IBrush> PulseBrushProperty =
        AvaloniaProperty.Register<LightPulseAlongPath, IBrush>(nameof(PulseBrush), new SolidColorBrush(0xFFFFF176));

    /// <summary>Edge length of the (circular) pulse, in canvas units.</summary>
    public static readonly StyledProperty<double> PulseDiameterProperty =
        AvaloniaProperty.Register<LightPulseAlongPath, double>(nameof(PulseDiameter), 10.0);

    /// <summary>Loop fraction at which the pulse appears at the path start (0…1).</summary>
    public static readonly StyledProperty<double> WindowStartProperty =
        AvaloniaProperty.Register<LightPulseAlongPath, double>(nameof(WindowStart));

    /// <summary>Loop fraction at which the pulse reaches the path end and disappears (0…1).</summary>
    public static readonly StyledProperty<double> WindowEndProperty =
        AvaloniaProperty.Register<LightPulseAlongPath, double>(nameof(WindowEnd), 1.0);

    private readonly Polyline _track;
    private readonly Ellipse _pulse;

    /// <summary>Builds the track and pulse shapes; property changes re-render the current frame.</summary>
    public LightPulseAlongPath()
    {
        _track = new Polyline
        {
            Points = PathPoints,
            Stroke = TrackBrush,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        _pulse = new Ellipse
        {
            Fill = PulseBrush,
            Width = PulseDiameter,
            Height = PulseDiameter,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        Content = new AnimCanvas { Children = { _track, _pulse }, IsHitTestVisible = false };
    }

    /// <summary>Polyline the pulse follows ("x,y x,y …" in canvas coordinates).</summary>
    public Points PathPoints
    {
        get => GetValue(PathPointsProperty);
        set => SetValue(PathPointsProperty, value);
    }

    /// <summary>Brush of the faint waveguide track under the pulse.</summary>
    public IBrush TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Brush of the travelling light pulse.</summary>
    public IBrush PulseBrush
    {
        get => GetValue(PulseBrushProperty);
        set => SetValue(PulseBrushProperty, value);
    }

    /// <summary>Edge length of the (circular) pulse, in canvas units.</summary>
    public double PulseDiameter
    {
        get => GetValue(PulseDiameterProperty);
        set => SetValue(PulseDiameterProperty, value);
    }

    /// <summary>Loop fraction at which the pulse appears at the path start (0…1).</summary>
    public double WindowStart
    {
        get => GetValue(WindowStartProperty);
        set => SetValue(WindowStartProperty, value);
    }

    /// <summary>Loop fraction at which the pulse reaches the path end and disappears (0…1).</summary>
    public double WindowEnd
    {
        get => GetValue(WindowEndProperty);
        set => SetValue(WindowEndProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PathPointsProperty)
        {
            _track.Points = change.GetNewValue<Points>();
            RenderFrame(Progress);
        }
        else if (change.Property == TrackBrushProperty)
        {
            _track.Stroke = change.GetNewValue<IBrush>();
        }
        else if (change.Property == PulseBrushProperty)
        {
            _pulse.Fill = change.GetNewValue<IBrush>();
        }
        else if (change.Property == PulseDiameterProperty)
        {
            _pulse.Width = _pulse.Height = change.GetNewValue<double>();
            RenderFrame(Progress);
        }
        else if (change.Property == WindowStartProperty || change.Property == WindowEndProperty)
        {
            RenderFrame(Progress);
        }
    }

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        var window = WindowEnd - WindowStart;
        if (PathPoints.Count < 2 || window <= 0 || progress < WindowStart || progress > WindowEnd)
        {
            _pulse.IsVisible = false;
            return;
        }

        var position = PolylineInterpolation.PointAt(PathPoints, (progress - WindowStart) / window);
        _pulse.IsVisible = true;
        AnimCanvas.SetLeft(_pulse, position.X - _pulse.Width / 2);
        AnimCanvas.SetTop(_pulse, position.Y - _pulse.Height / 2);
    }
}
