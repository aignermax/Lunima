using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.PdkOffset;

namespace CAP.Avalonia.Views;

/// <summary>
/// Code-behind for the PDK Component Offset Editor window.
/// Renders the visual pin overlay on the Canvas when the selected component changes.
/// </summary>
public partial class PdkOffsetEditorWindow : Window
{
    private const double CanvasPadding = 20.0;
    private const double CanvasScale = 2.0;
    private const double PinDotRadius = 5.0;
    private const double CrosshairLength = 12.0;

    private static readonly IBrush ComponentBoxBrush = new SolidColorBrush(Color.Parse("#1a3a6a"));
    private static readonly IBrush ComponentBorderBrush = new SolidColorBrush(Color.Parse("#4080c0"));
    private static readonly IBrush PinBrush = new SolidColorBrush(Colors.Cyan);
    private static readonly IBrush OriginBrush = new SolidColorBrush(Colors.Orange);

    /// <summary>
    /// Initializes the window and subscribes to ViewModel property changes for canvas rendering.
    /// </summary>
    public PdkOffsetEditorWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is PdkOffsetEditorViewModel vm)
            {
                SubscribeToViewModel(vm);
            }
        };
    }

    private void SubscribeToViewModel(PdkOffsetEditorViewModel vm)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is
                nameof(vm.SelectedComponent) or
                nameof(vm.OffsetX) or
                nameof(vm.OffsetY) or
                nameof(vm.CanvasComponentWidth) or
                nameof(vm.CanvasComponentHeight))
            {
                RedrawOverlay(vm);
            }
        };

        vm.PinMarkers.CollectionChanged += (_, _) => RedrawOverlay(vm);
    }

    private void RedrawOverlay(PdkOffsetEditorViewModel vm)
    {
        if (OverlayCanvas == null) return;
        OverlayCanvas.Children.Clear();

        if (vm.SelectedComponent == null) return;

        var draft = vm.SelectedComponent.Draft;
        double compW = draft.WidthMicrometers  * CanvasScale;
        double compH = draft.HeightMicrometers * CanvasScale;

        // Expand canvas if component is large
        OverlayCanvas.Width  = compW + CanvasPadding * 2;
        OverlayCanvas.Height = compH + CanvasPadding * 2;

        // Component bounding box
        var box = new Rectangle
        {
            Width  = compW,
            Height = compH,
            Fill   = ComponentBoxBrush,
            Stroke = ComponentBorderBrush,
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(box, CanvasPadding);
        Canvas.SetTop(box, CanvasPadding);
        OverlayCanvas.Children.Add(box);

        // Pin dots
        foreach (var pin in vm.PinMarkers)
        {
            var dot = new Ellipse
            {
                Width  = PinDotRadius * 2,
                Height = PinDotRadius * 2,
                Fill   = PinBrush
            };
            ToolTip.SetTip(dot, pin.Name);
            Canvas.SetLeft(dot, pin.CanvasX - PinDotRadius);
            Canvas.SetTop(dot, pin.CanvasY - PinDotRadius);
            OverlayCanvas.Children.Add(dot);

            // Pin label
            var label = new TextBlock
            {
                Text       = pin.Name,
                Foreground = PinBrush,
                FontSize   = 9
            };
            Canvas.SetLeft(label, pin.CanvasX + PinDotRadius + 1);
            Canvas.SetTop(label, pin.CanvasY - 6);
            OverlayCanvas.Children.Add(label);
        }

        // Nazca origin crosshair
        double originX = CanvasPadding + vm.OffsetX * CanvasScale;
        double originY = CanvasPadding + (draft.HeightMicrometers - vm.OffsetY) * CanvasScale;
        DrawCrosshair(originX, originY);

        var originLabel = new TextBlock
        {
            Text       = "origin",
            Foreground = OriginBrush,
            FontSize   = 9
        };
        Canvas.SetLeft(originLabel, originX + CrosshairLength + 2);
        Canvas.SetTop(originLabel, originY - 6);
        OverlayCanvas.Children.Add(originLabel);
    }

    private void DrawCrosshair(double cx, double cy)
    {
        var hLine = new Line
        {
            StartPoint = new global::Avalonia.Point(cx - CrosshairLength, cy),
            EndPoint   = new global::Avalonia.Point(cx + CrosshairLength, cy),
            Stroke     = OriginBrush,
            StrokeThickness = 1.5
        };
        var vLine = new Line
        {
            StartPoint = new global::Avalonia.Point(cx, cy - CrosshairLength),
            EndPoint   = new global::Avalonia.Point(cx, cy + CrosshairLength),
            Stroke     = OriginBrush,
            StrokeThickness = 1.5
        };
        OverlayCanvas.Children.Add(hLine);
        OverlayCanvas.Children.Add(vLine);
    }
}
