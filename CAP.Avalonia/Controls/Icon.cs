using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Draws one of the app's line icons (<c>Styles/Icons.axaml</c>, Lucide 24×24 stroke
/// geometry) at <see cref="Size"/> pixels in the inherited foreground color, so an icon
/// inside a button follows the button's hover, checked and disabled states.
/// </summary>
/// <remarks>
/// The geometry is authored for a 24 unit box with a 2 unit stroke; scaling the whole
/// drawing keeps the stroke proportional (1.33 px at 16 px), which is what makes the icon
/// set read as one family.
/// </remarks>
public class Icon : Control
{
    /// <summary>Side length of the authored icon box.</summary>
    private const double SourceBoxSize = 24;

    /// <summary>Stroke width in source units.</summary>
    private const double SourceStrokeThickness = 2;

    /// <summary>Defines the <see cref="Data"/> property.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    /// <summary>Defines the <see cref="Size"/> property.</summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 16);

    /// <summary>Defines the <see cref="Foreground"/> property (inherited, like text).</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    /// <summary>Defines the <see cref="StrokeScale"/> property.</summary>
    public static readonly StyledProperty<double> StrokeScaleProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeScale), 1.0);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty, StrokeScaleProperty);
        AffectsMeasure<Icon>(SizeProperty);
    }

    /// <summary>The icon geometry, typically <c>{StaticResource Icon.Name}</c>.</summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Rendered side length in device-independent pixels.</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Stroke color; inherits the surrounding text foreground by default.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Multiplier on the stroke width (e.g. 0.85 for a lighter look at large sizes).</summary>
    public double StrokeScale
    {
        get => GetValue(StrokeScaleProperty);
        set => SetValue(StrokeScaleProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var geometry = Data;
        var brush = Foreground;
        if (geometry == null || brush == null || Size <= 0)
            return;

        double scale = Size / SourceBoxSize;
        double offsetX = (Bounds.Width - Size) / 2;
        double offsetY = (Bounds.Height - Size) / 2;
        var pen = new Pen(brush, SourceStrokeThickness * StrokeScale,
            lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
            context.DrawGeometry(null, pen, geometry);
    }
}
