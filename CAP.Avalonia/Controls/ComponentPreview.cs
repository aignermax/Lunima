using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Miniature preview control that draws a component schematic (rectangle + pins)
/// for the component library panel.
/// </summary>
public class ComponentPreview : Control
{
    public static readonly StyledProperty<double> WidthMicrometersProperty =
        AvaloniaProperty.Register<ComponentPreview, double>(nameof(WidthMicrometers));

    public static readonly StyledProperty<double> HeightMicrometersProperty =
        AvaloniaProperty.Register<ComponentPreview, double>(nameof(HeightMicrometers));

    public static readonly StyledProperty<PinDefinition[]?> PinDefinitionsProperty =
        AvaloniaProperty.Register<ComponentPreview, PinDefinition[]?>(nameof(PinDefinitions));

    public double WidthMicrometers
    {
        get => GetValue(WidthMicrometersProperty);
        set => SetValue(WidthMicrometersProperty, value);
    }

    public double HeightMicrometers
    {
        get => GetValue(HeightMicrometersProperty);
        set => SetValue(HeightMicrometersProperty, value);
    }

    public PinDefinition[]? PinDefinitions
    {
        get => GetValue(PinDefinitionsProperty);
        set => SetValue(PinDefinitionsProperty, value);
    }

    static ComponentPreview()
    {
        AffectsRender<ComponentPreview>(
            WidthMicrometersProperty,
            HeightMicrometersProperty,
            PinDefinitionsProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        double compW = WidthMicrometers;
        double compH = HeightMicrometers;
        if (compW <= 0 || compH <= 0) return;

        // Scale to fit with padding
        double pad = 4;
        double availW = bounds.Width - pad * 2;
        double availH = bounds.Height - pad * 2;
        double scale = Math.Min(availW / compW, availH / compH);

        double drawW = compW * scale;
        double drawH = compH * scale;
        double offsetX = pad + (availW - drawW) / 2;
        double offsetY = pad + (availH - drawH) / 2;

        // Component body
        var rect = new Rect(offsetX, offsetY, drawW, drawH);
        var fillBrush = new SolidColorBrush(Color.FromRgb(40, 50, 70));
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 110, 130)), 1);
        context.FillRectangle(fillBrush, rect);
        context.DrawRectangle(borderPen, rect);

        // Pins
        var pins = PinDefinitions;
        if (pins == null || pins.Length == 0) return;

        double pinRadius = Math.Max(2, Math.Min(4, scale * 3));
        double dirLen = pinRadius * 2.5;
        var pinBrush = new SolidColorBrush(Color.FromRgb(100, 200, 100));
        var dirPen = new Pen(new SolidColorBrush(Color.FromRgb(180, 220, 180)), 1);

        foreach (var pin in pins)
        {
            double px = offsetX + pin.OffsetX * scale;
            double py = offsetY + pin.OffsetY * scale;

            // Pin dot
            context.DrawEllipse(pinBrush, null, new Point(px, py), pinRadius, pinRadius);

            // Direction indicator
            double angle = pin.AngleDegrees * Math.PI / 180;
            context.DrawLine(dirPen,
                new Point(px, py),
                new Point(px + Math.Cos(angle) * dirLen, py + Math.Sin(angle) * dirLen));
        }
    }
}
