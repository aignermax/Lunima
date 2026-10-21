using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>Small cross-sections and plan views explaining layer roles without PDK-specific geometry.</summary>
public sealed class GdsLayerRoleIllustration : Control
{
    public static readonly StyledProperty<GdsLayerRole> RoleProperty =
        AvaloniaProperty.Register<GdsLayerRoleIllustration, GdsLayerRole>(nameof(Role));
    public GdsLayerRole Role { get => GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    private static readonly IBrush Base = new SolidColorBrush(Color.Parse("#536776"));
    private static readonly IBrush Light = new SolidColorBrush(Color.Parse("#5DE0CB"));
    private static readonly IBrush Metal = new SolidColorBrush(Color.Parse("#F2BC68"));
    private static readonly Pen Outline = new(Base, 1.5);
    private static readonly Pen Signal = new(Light, 2);

    static GdsLayerRoleIllustration() => AffectsRender<GdsLayerRoleIllustration>(RoleProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var transform = context.PushTransform(Matrix.CreateScale(Bounds.Width / 100, Bounds.Height / 40));
        if (Role is GdsLayerRole.OpticalCore or GdsLayerRole.Slab or GdsLayerRole.Etch or GdsLayerRole.Heater)
        {
            context.DrawRectangle(Base, null, new Rect(12, 29, 76, 8), 2, 2);
            context.DrawRectangle(Role == GdsLayerRole.Slab ? Light : Base, null, new Rect(18, 23, 64, 6));
            context.DrawRectangle(Role == GdsLayerRole.OpticalCore ? Light : Base, null, new Rect(39, 11, 22, 12));
            if (Role == GdsLayerRole.Etch)
            {
                context.DrawLine(new Pen(Metal, 3), new Point(23, 6), new Point(23, 19));
                context.DrawLine(new Pen(Metal, 3), new Point(77, 6), new Point(77, 19));
                context.DrawLine(new Pen(Metal, 2), new Point(18, 14), new Point(23, 19));
                context.DrawLine(new Pen(Metal, 2), new Point(82, 14), new Point(77, 19));
            }
            if (Role == GdsLayerRole.Heater)
                context.DrawRectangle(Metal, null, new Rect(34, 3, 32, 4), 2, 2);
        }
        else if (Role == GdsLayerRole.Electrical)
        {
            context.DrawRectangle(Metal, null, new Rect(12, 10, 20, 20), 3, 3);
            context.DrawRectangle(Metal, null, new Rect(68, 10, 20, 20), 3, 3);
            context.DrawLine(new Pen(Metal, 5), new Point(32, 20), new Point(68, 20));
        }
        else if ((Role & (GdsLayerRole.PortMarker | GdsLayerRole.PortLabel)) != 0)
        {
            context.DrawLine(Outline, new Point(10, 25), new Point(90, 25));
            if (Role.HasFlag(GdsLayerRole.PortMarker))
            {
                context.DrawLine(Signal, new Point(20, 25), new Point(55, 25));
                context.DrawLine(Signal, new Point(45, 16), new Point(55, 25));
                context.DrawLine(Signal, new Point(45, 34), new Point(55, 25));
            }
            if (Role.HasFlag(GdsLayerRole.PortLabel))
            {
                var label = new FormattedText("o1", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 14, Light);
                context.DrawText(label, new Point(57, 2));
            }
            context.DrawEllipse(Light, null, new Point(60, 25), 3, 3);
        }
        else
        {
            context.DrawRectangle(null, Outline, new Rect(16, 5, 68, 30), 3, 3);
            context.DrawRectangle(Base, null, new Rect(26, 13, 18, 14));
            context.DrawLine(Outline, new Point(51, 15), new Point(74, 15));
            context.DrawLine(Outline, new Point(51, 23), new Point(68, 23));
        }
    }
}
