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
        if (OverlayCanvas == null)
        {
            // Can happen when the DataContext is assigned before the XAML
            // is fully loaded. The next redraw (triggered by a ViewModel
            // change once the canvas exists) will render correctly.
            System.Diagnostics.Debug.WriteLine(
                "PdkOffsetEditorWindow.RedrawOverlay: OverlayCanvas not initialized — skipping redraw.");
            return;
        }
        OverlayCanvas.Children.Clear();

        if (vm.SelectedComponent == null) return;

        // All geometry comes from the ViewModel — the view only maps these
        // pre-computed canvas pixel values onto Avalonia shapes.
        OverlayCanvas.Width  = vm.CanvasTotalWidth;
        OverlayCanvas.Height = vm.CanvasTotalHeight;

        var box = new Rectangle
        {
            Width  = vm.CanvasComponentWidth,
            Height = vm.CanvasComponentHeight,
            Fill   = ComponentBoxBrush,
            Stroke = ComponentBorderBrush,
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(box, vm.CanvasComponentLeft);
        Canvas.SetTop(box, vm.CanvasComponentTop);
        OverlayCanvas.Children.Add(box);

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

        DrawCrosshair(vm.CanvasOriginX, vm.CanvasOriginY);

        var originLabel = new TextBlock
        {
            Text       = "origin",
            Foreground = OriginBrush,
            FontSize   = 9
        };
        Canvas.SetLeft(originLabel, vm.CanvasOriginX + CrosshairLength + 2);
        Canvas.SetTop(originLabel, vm.CanvasOriginY - 6);
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
