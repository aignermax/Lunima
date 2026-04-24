using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.PdkOffset;

namespace CAP.Avalonia.Views;

/// <summary>
/// Code-behind for the PDK Component Offset Editor window.
/// Renders the visual pin overlay (and optional Nazca GDS polygon overlay) on the Canvas.
/// </summary>
public partial class PdkOffsetEditorWindow : Window
{
    private const double PinDotRadius = 5.0;
    private const double CrosshairLength = 12.0;

    private static readonly IBrush ComponentBoxBrush = new SolidColorBrush(Color.Parse("#1a3a6a"));
    private static readonly IBrush ComponentBorderBrush = new SolidColorBrush(Color.Parse("#4080c0"));
    private static readonly IBrush PinBrush = new SolidColorBrush(Colors.Cyan);
    private static readonly IBrush OriginBrush = new SolidColorBrush(Colors.Orange);
    private static readonly IBrush NazcaPolygonBrush = new SolidColorBrush(Color.FromArgb(100, 0, 100, 50));
    private static readonly IBrush NazcaPolygonBorderBrush = new SolidColorBrush(Color.Parse("#00c060"));
    private static readonly IBrush NazcaStubBrush = new SolidColorBrush(Color.Parse("#00ff80"));

    /// <summary>
    /// Initializes the window and subscribes to ViewModel property changes for canvas rendering.
    /// </summary>
    public PdkOffsetEditorWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is PdkOffsetEditorViewModel vm)
                SubscribeToViewModel(vm);
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
                nameof(vm.CanvasComponentHeight) or
                nameof(vm.CanvasComponentLeft) or
                nameof(vm.CanvasComponentTop) or
                nameof(vm.HasNazcaOverlay))
            {
                RedrawOverlay(vm);
            }
        };

        vm.PinMarkers.CollectionChanged += (_, _) => RedrawOverlay(vm);
        vm.NazcaPolygons.CollectionChanged += (_, _) => RedrawOverlay(vm);
        vm.NazcaPinStubs.CollectionChanged += (_, _) => RedrawOverlay(vm);
    }

    private void RedrawOverlay(PdkOffsetEditorViewModel vm)
    {
        if (OverlayCanvas == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "PdkOffsetEditorWindow.RedrawOverlay: OverlayCanvas not initialized — skipping redraw.");
            return;
        }
        OverlayCanvas.Children.Clear();

        if (vm.SelectedComponent == null) return;

        OverlayCanvas.Width  = vm.CanvasTotalWidth;
        OverlayCanvas.Height = vm.CanvasTotalHeight;

        // Draw Nazca GDS polygons behind the Lunima box
        if (vm.HasNazcaOverlay)
        {
            foreach (var poly in vm.NazcaPolygons)
            {
                var polygon = new Polygon
                {
                    Points = new AvaloniaList<global::Avalonia.Point>(
                        poly.CanvasPoints.Select(p => new global::Avalonia.Point(p.X, p.Y))),
                    Fill = NazcaPolygonBrush,
                    Stroke = NazcaPolygonBorderBrush,
                    StrokeThickness = 0.5,
                };
                OverlayCanvas.Children.Add(polygon);
            }

            foreach (var stub in vm.NazcaPinStubs)
            {
                var line = new Line
                {
                    StartPoint = new global::Avalonia.Point(stub.X0, stub.Y0),
                    EndPoint   = new global::Avalonia.Point(stub.X1, stub.Y1),
                    Stroke     = NazcaStubBrush,
                    StrokeThickness = 2.0,
                };
                OverlayCanvas.Children.Add(line);
            }
        }

        // Draw Lunima component bounding box
        var box = new Rectangle
        {
            Width  = vm.CanvasComponentWidth,
            Height = vm.CanvasComponentHeight,
            Fill   = ComponentBoxBrush,
            Stroke = ComponentBorderBrush,
            StrokeThickness = 1.5,
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
                Fill   = PinBrush,
            };
            ToolTip.SetTip(dot, pin.Name);
            Canvas.SetLeft(dot, pin.CanvasX - PinDotRadius);
            Canvas.SetTop(dot, pin.CanvasY - PinDotRadius);
            OverlayCanvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text       = pin.Name,
                Foreground = PinBrush,
                FontSize   = 9,
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
            FontSize   = 9,
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
            StrokeThickness = 1.5,
        };
        var vLine = new Line
        {
            StartPoint = new global::Avalonia.Point(cx, cy - CrosshairLength),
            EndPoint   = new global::Avalonia.Point(cx, cy + CrosshairLength),
            Stroke     = OriginBrush,
            StrokeThickness = 1.5,
        };
        OverlayCanvas.Children.Add(hLine);
        OverlayCanvas.Children.Add(vLine);
    }
}
