using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using CAP.Avalonia.ViewModels;
using CAP_Core.Routing;
using SkiaSharp;

namespace CAP.Avalonia.Controls;

public class DesignCanvas : Control
{
    public static readonly StyledProperty<DesignCanvasViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<DesignCanvas, DesignCanvasViewModel?>(nameof(ViewModel));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DesignCanvas, double>(nameof(Zoom), 1.0);

    public DesignCanvasViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    private Point _lastPointerPosition;
    private ComponentViewModel? _draggingComponent;
    private bool _isPanning;

    static DesignCanvas()
    {
        AffectsRender<DesignCanvas>(ViewModelProperty, ZoomProperty);
    }

    public DesignCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var vm = ViewModel;
        if (vm == null) return;

        // Background
        context.FillRectangle(Brushes.Black, bounds);

        // Draw grid
        DrawGrid(context, bounds);

        // Apply zoom and pan transform
        using (context.PushTransform(Matrix.CreateTranslation(vm.PanX, vm.PanY)))
        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom)))
        {
            // Draw connections first (behind components)
            foreach (var conn in vm.Connections)
            {
                DrawWaveguideConnection(context, conn);
            }

            // Draw components
            foreach (var comp in vm.Components)
            {
                DrawComponent(context, comp);
            }
        }

        // Draw status info
        DrawStatusInfo(context, bounds);
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
        var majorGridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);

        double gridSize = 50 * Zoom;
        double majorGridSize = 250 * Zoom;

        var vm = ViewModel;
        double offsetX = vm?.PanX ?? 0;
        double offsetY = vm?.PanY ?? 0;

        // Minor grid
        for (double x = offsetX % gridSize; x < bounds.Width; x += gridSize)
        {
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, bounds.Height));
        }
        for (double y = offsetY % gridSize; y < bounds.Height; y += gridSize)
        {
            context.DrawLine(gridPen, new Point(0, y), new Point(bounds.Width, y));
        }

        // Major grid (250µm = 1 tile equivalent)
        for (double x = offsetX % majorGridSize; x < bounds.Width; x += majorGridSize)
        {
            context.DrawLine(majorGridPen, new Point(x, 0), new Point(x, bounds.Height));
        }
        for (double y = offsetY % majorGridSize; y < bounds.Height; y += majorGridSize)
        {
            context.DrawLine(majorGridPen, new Point(0, y), new Point(bounds.Width, y));
        }
    }

    private void DrawComponent(DrawingContext context, ComponentViewModel comp)
    {
        var rect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);

        // Component fill
        var fillBrush = comp.IsSelected
            ? new SolidColorBrush(Color.FromRgb(60, 80, 120))
            : new SolidColorBrush(Color.FromRgb(40, 50, 70));
        context.FillRectangle(fillBrush, rect);

        // Component border
        var borderPen = comp.IsSelected
            ? new Pen(Brushes.Cyan, 2)
            : new Pen(Brushes.Gray, 1);
        context.DrawRectangle(borderPen, rect);

        // Draw physical pins
        foreach (var pin in comp.Component.PhysicalPins)
        {
            var (pinX, pinY) = pin.GetAbsolutePosition();
            var pinRect = new Rect(pinX - 5, pinY - 5, 10, 10);

            var pinBrush = pin.LogicalPin != null
                ? new SolidColorBrush(Color.FromRgb(100, 200, 100))  // Linked pin - green
                : new SolidColorBrush(Color.FromRgb(200, 100, 100)); // Unlinked - red

            context.FillRectangle(pinBrush, pinRect);
        }

        // Component name
        var text = new FormattedText(
            comp.Name,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            Brushes.White);

        context.DrawText(text, new Point(comp.X + 5, comp.Y + 5));
    }

    private void DrawWaveguideConnection(DrawingContext context, WaveguideConnectionViewModel conn)
    {
        var segments = conn.Connection.GetPathSegments();

        var waveguidePen = conn.IsSelected
            ? new Pen(Brushes.Yellow, 3)
            : new Pen(Brushes.Orange, 2);

        if (segments.Count == 0)
        {
            // Fallback: draw simple line if no path calculated
            context.DrawLine(waveguidePen,
                new Point(conn.StartX, conn.StartY),
                new Point(conn.EndX, conn.EndY));
            return;
        }

        // Draw each path segment
        foreach (var segment in segments)
        {
            if (segment is StraightSegment straight)
            {
                context.DrawLine(waveguidePen,
                    new Point(straight.StartPoint.X, straight.StartPoint.Y),
                    new Point(straight.EndPoint.X, straight.EndPoint.Y));
            }
            else if (segment is BendSegment bend)
            {
                // Draw arc using polyline approximation
                DrawArc(context, waveguidePen, bend);
            }
        }

        // Draw connection info
        var midX = (conn.StartX + conn.EndX) / 2;
        var midY = (conn.StartY + conn.EndY) / 2;
        var infoText = new FormattedText(
            $"{conn.PathLength:F0}µm, {conn.LossDb:F2}dB",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            Brushes.LightGray);

        context.DrawText(infoText, new Point(midX, midY - 15));
    }

    private void DrawArc(DrawingContext context, Pen pen, BendSegment bend)
    {
        // Approximate arc with line segments
        int numSegments = Math.Max(8, (int)(Math.Abs(bend.SweepAngleDegrees) / 5));
        var points = new List<Point>();

        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;

        for (int i = 0; i <= numSegments; i++)
        {
            double t = i / (double)numSegments;
            double angle = startRad + sweepRad * t;

            // Calculate point on arc (perpendicular to tangent direction)
            double perpAngle = angle - Math.PI / 2 * Math.Sign(bend.SweepAngleDegrees);
            double x = bend.Center.X + bend.RadiusMicrometers * Math.Cos(perpAngle);
            double y = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(perpAngle);
            points.Add(new Point(x, y));
        }

        // Draw polyline
        for (int i = 0; i < points.Count - 1; i++)
        {
            context.DrawLine(pen, points[i], points[i + 1]);
        }
    }

    private void DrawStatusInfo(DrawingContext context, Rect bounds)
    {
        var vm = ViewModel;
        if (vm == null) return;

        var statusText = new FormattedText(
            $"Zoom: {Zoom:P0} | Components: {vm.Components.Count} | Connections: {vm.Connections.Count}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            Brushes.White);

        context.DrawText(statusText, new Point(10, bounds.Height - 25));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetPosition(this);
        _lastPointerPosition = point;

        var vm = ViewModel;
        if (vm == null) return;

        // Transform point to canvas coordinates
        var canvasPoint = ScreenToCanvas(point);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Check if clicking on a component
            _draggingComponent = HitTestComponent(canvasPoint);
            if (_draggingComponent != null)
            {
                // Select the component
                foreach (var c in vm.Components) c.IsSelected = false;
                _draggingComponent.IsSelected = true;
                InvalidateVisual();
            }
        }
        else if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
        }

        e.Handled = true;
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);
        var delta = point - _lastPointerPosition;

        var vm = ViewModel;
        if (vm == null) return;

        if (_draggingComponent != null)
        {
            // Move the component
            vm.MoveComponent(_draggingComponent, delta.X / Zoom, delta.Y / Zoom);
            InvalidateVisual();
        }
        else if (_isPanning)
        {
            vm.PanX += delta.X;
            vm.PanY += delta.Y;
            InvalidateVisual();
        }

        _lastPointerPosition = point;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _draggingComponent = null;
        _isPanning = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y > 0 ? 1.1 : 0.9;
        var newZoom = Math.Clamp(Zoom * delta, 0.1, 10.0);

        // Zoom toward pointer position
        var point = e.GetPosition(this);
        var vm = ViewModel;
        if (vm != null)
        {
            var beforeZoom = ScreenToCanvas(point);
            Zoom = newZoom;
            var afterZoom = ScreenToCanvas(point);

            vm.PanX += (afterZoom.X - beforeZoom.X) * Zoom;
            vm.PanY += (afterZoom.Y - beforeZoom.Y) * Zoom;
        }
        else
        {
            Zoom = newZoom;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private Point ScreenToCanvas(Point screenPoint)
    {
        var vm = ViewModel;
        if (vm == null) return screenPoint;

        return new Point(
            (screenPoint.X - vm.PanX) / Zoom,
            (screenPoint.Y - vm.PanY) / Zoom);
    }

    private ComponentViewModel? HitTestComponent(Point canvasPoint)
    {
        var vm = ViewModel;
        if (vm == null) return null;

        // Check in reverse order (top-most first)
        for (int i = vm.Components.Count - 1; i >= 0; i--)
        {
            var comp = vm.Components[i];
            var rect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);
            if (rect.Contains(canvasPoint))
            {
                return comp;
            }
        }

        return null;
    }
}
