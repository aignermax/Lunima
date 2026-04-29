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

    // When the GDS overlay is OFF the box is the only thing the user sees,
    // so it gets a fill. When the overlay is on the box becomes a dashed
    // outline so the actual chip geometry shows through unobstructed.
    private static readonly IBrush ComponentBoxBrush =
        new SolidColorBrush(Color.FromArgb(0x4D, 0x1a, 0x3a, 0x6a));   // ~30% alpha
    private static readonly IBrush ComponentBorderBrush = new SolidColorBrush(Color.Parse("#4080c0"));
    private static readonly IBrush PinBrush = new SolidColorBrush(Colors.Cyan);
    private static readonly IBrush OriginBrush = new SolidColorBrush(Colors.Orange);
    private static readonly IBrush NazcaStubBrush = new SolidColorBrush(Color.Parse("#00ff80"));

    /// <summary>
    /// Per-GDS-layer colour. Picks readable colours for the layers SiEPIC and
    /// Nazca demofab actually use, so silicon, doping, metals and grating
    /// teeth are immediately distinguishable instead of all-green blob soup.
    /// Unknown layers fall back to a neutral gray so they still render but
    /// don't claim more attention than the known ones.
    /// </summary>
    private static IBrush BrushForLayer(int layer)
    {
        // Alpha 180/255 ≈ 70% — high enough that geometry reads as solid,
        // low enough that overlapping shapes / pin dots still peek through.
        Color c = layer switch
        {
            1   => Color.FromArgb(180, 0x40, 0x90, 0xd0),  // silicon waveguide — blue
            2   => Color.FromArgb(180, 0x80, 0xc0, 0xff),  // shallow etch — light blue
            3   => Color.FromArgb(180, 0xa0, 0x60, 0xc0),  // deep etch — purple
            12 or 13 => Color.FromArgb(180, 0xff, 0x90, 0x40),  // N / N+ doped — orange
            14 or 15 => Color.FromArgb(180, 0xd0, 0x40, 0x80),  // P / P+ doped — magenta
            21  => Color.FromArgb(180, 0xb0, 0xb0, 0xb0),  // silicide — gray
            23 or 24 => Color.FromArgb(180, 0xff, 0xd0, 0x20),  // metal 1 / via 1 — gold
            41 or 42 => Color.FromArgb(180, 0xff, 0x80, 0xc0),  // metal 2 — pink
            998 => Color.FromArgb(190, 0x20, 0xc0, 0x60),  // grating teeth — green
            _   => Color.FromArgb(140, 0x70, 0x70, 0x80),  // unknown — dim neutral
        };
        return new SolidColorBrush(c);
    }

    private static IBrush BorderForLayer(int layer)
    {
        // Same hue family as fill but fully opaque so polygon edges stay crisp
        // even where two same-layer shapes overlap.
        Color c = layer switch
        {
            1   => Color.FromArgb(255, 0x60, 0xb0, 0xf0),
            2   => Color.FromArgb(255, 0xa0, 0xd0, 0xff),
            3   => Color.FromArgb(255, 0xc0, 0x80, 0xe0),
            12 or 13 => Color.FromArgb(255, 0xff, 0xb0, 0x60),
            14 or 15 => Color.FromArgb(255, 0xf0, 0x60, 0xa0),
            21  => Color.FromArgb(255, 0xd0, 0xd0, 0xd0),
            23 or 24 => Color.FromArgb(255, 0xff, 0xe0, 0x40),
            41 or 42 => Color.FromArgb(255, 0xff, 0xa0, 0xd0),
            998 => Color.FromArgb(255, 0x40, 0xff, 0x80),
            _   => Color.FromArgb(255, 0x90, 0x90, 0xa0),
        };
        return new SolidColorBrush(c);
    }

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
                vm.CopyToClipboard = async text =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(text);
                };
                vm.ResetZoomHook = () =>
                {
                    if (ZoomSlider != null) ZoomSlider.Value = 1.0;
                };
            }
        };

        WireOverlayInteractions();
    }

    private void WireOverlayInteractions()
    {
        var scrollViewer = this.FindControl<global::Avalonia.Controls.ScrollViewer>("OverlayScrollViewer");
        if (scrollViewer == null) return;

        // Mouse-wheel zoom anywhere over the viewport. 1.2× per notch.
        scrollViewer.PointerWheelChanged += (_, e) =>
        {
            if (ZoomSlider == null) return;
            var factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
            ZoomSlider.Value = Math.Clamp(ZoomSlider.Value * factor, ZoomSlider.Minimum, ZoomSlider.Maximum);
            e.Handled = true;
        };

        // Left-mouse-drag pan: change ScrollViewer.Offset by the negative
        // mouse delta. Captures the pointer so the gesture survives a quick
        // exit out of the viewport bounds.
        bool isPanning = false;
        global::Avalonia.Point panStart = default;
        global::Avalonia.Vector panStartOffset = default;

        scrollViewer.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(scrollViewer).Properties;
            if (!props.IsLeftButtonPressed) return;
            isPanning = true;
            panStart = e.GetPosition(scrollViewer);
            panStartOffset = scrollViewer.Offset;
            e.Pointer.Capture(scrollViewer);
            e.Handled = true;
        };

        scrollViewer.PointerMoved += (_, e) =>
        {
            if (!isPanning) return;
            var current = e.GetPosition(scrollViewer);
            var delta = current - panStart;
            scrollViewer.Offset = new global::Avalonia.Vector(
                Math.Max(0, panStartOffset.X - delta.X),
                Math.Max(0, panStartOffset.Y - delta.Y));
            e.Handled = true;
        };

        scrollViewer.PointerReleased += (_, e) =>
        {
            if (!isPanning) return;
            isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        };

        // If the pointer capture is lost mid-drag (alt-tab, focus stolen by a
        // modal popup, …), reset state so the next hover doesn't keep panning.
        scrollViewer.PointerCaptureLost += (_, _) => { isPanning = false; };
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
                nameof(vm.HasNazcaOverlay) or
                nameof(vm.ShowNazcaOverlay))
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

        bool gdsShown = vm.HasNazcaOverlay && vm.ShowNazcaOverlay;

        // Layering, bottom → top:
        //   1. Nazca GDS polygons (the actual chip geometry — the picture)
        //   2. Lunima component bbox — solid when no GDS, dashed outline when
        //      GDS is shown so it's a calibration reference rather than the
        //      thing pretending to BE the component.
        //   3. Nazca pin stubs (truth)
        //   4. Lunima pin dots + labels (what the JSON says)
        //   5. Origin crosshair + label
        // GDS at the bottom means the polygons read as the canvas, and pins
        // / box / origin float on top, all visible.

        if (gdsShown)
        {
            foreach (var poly in vm.NazcaPolygons)
            {
                var polygon = new Polygon
                {
                    Points = new AvaloniaList<global::Avalonia.Point>(
                        poly.CanvasPoints.Select(p => new global::Avalonia.Point(p.X, p.Y))),
                    Fill = BrushForLayer(poly.Layer),
                    Stroke = BorderForLayer(poly.Layer),
                    StrokeThickness = 0.6,
                };
                ToolTip.SetTip(polygon, $"layer {poly.Layer}");
                OverlayCanvas.Children.Add(polygon);
            }
        }

        var box = new Rectangle
        {
            Width  = vm.CanvasComponentWidth,
            Height = vm.CanvasComponentHeight,
            Fill   = gdsShown ? null : ComponentBoxBrush,
            Stroke = ComponentBorderBrush,
            StrokeThickness = gdsShown ? 1.0 : 1.5,
            StrokeDashArray = gdsShown ? new AvaloniaList<double> { 4, 4 } : null,
        };
        Canvas.SetLeft(box, vm.CanvasComponentLeft);
        Canvas.SetTop(box, vm.CanvasComponentTop);
        OverlayCanvas.Children.Add(box);

        if (gdsShown)
        {
            foreach (var stub in vm.NazcaPinStubs)
            {
                var line = new Line
                {
                    StartPoint = new global::Avalonia.Point(stub.X0, stub.Y0),
                    EndPoint   = new global::Avalonia.Point(stub.X1, stub.Y1),
                    Stroke     = NazcaStubBrush,
                    StrokeThickness = 2.0,
                };
                ToolTip.SetTip(line, $"Nazca pin {stub.Name}");
                OverlayCanvas.Children.Add(line);
            }
        }

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
