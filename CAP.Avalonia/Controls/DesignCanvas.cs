using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CAP.Avalonia.ViewModels;
using CAP_Core.Components;
using CAP_Core.Routing;

namespace CAP.Avalonia.Controls;

public class DesignCanvas : Control
{
    public static readonly StyledProperty<DesignCanvasViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<DesignCanvas, DesignCanvasViewModel?>(nameof(ViewModel));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DesignCanvas, double>(nameof(Zoom), 1.0);

    public static readonly StyledProperty<MainViewModel?> MainViewModelProperty =
        AvaloniaProperty.Register<DesignCanvas, MainViewModel?>(nameof(MainViewModel));

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

    public MainViewModel? MainViewModel
    {
        get => GetValue(MainViewModelProperty);
        set => SetValue(MainViewModelProperty, value);
    }

    private Point _lastPointerPosition;
    private ComponentViewModel? _draggingComponent;
    private bool _isPanning;
    private const double PinHitRadius = 15; // Hit test radius for pins

    static DesignCanvas()
    {
        AffectsRender<DesignCanvas>(ViewModelProperty, ZoomProperty);
        MainViewModelProperty.Changed.AddClassHandler<DesignCanvas>((canvas, e) => canvas.OnMainViewModelChanged(e));
    }

    public DesignCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private void OnMainViewModelChanged(AvaloniaPropertyChangedEventArgs e)
    {
        // Unsubscribe from old
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.CommandManager.StateChanged -= OnCommandStateChanged;
        }

        // Subscribe to new
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.CommandManager.StateChanged += OnCommandStateChanged;
        }
    }

    private void OnCommandStateChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
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

        // Draw mode indicator
        DrawModeIndicator(context, bounds);

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
        var mainVm = MainViewModel;
        bool isConnectMode = mainVm?.CurrentMode == InteractionMode.Connect;

        foreach (var pin in comp.Component.PhysicalPins)
        {
            var (pinX, pinY) = pin.GetAbsolutePosition();

            // Draw pin based on connection mode
            IBrush pinBrush;
            double pinSize = isConnectMode ? 8 : 5;

            if (isConnectMode)
            {
                pinBrush = new SolidColorBrush(Color.FromRgb(255, 200, 0)); // Yellow in connect mode
            }
            else if (pin.LogicalPin != null)
            {
                pinBrush = new SolidColorBrush(Color.FromRgb(100, 200, 100)); // Linked pin - green
            }
            else
            {
                pinBrush = new SolidColorBrush(Color.FromRgb(200, 100, 100)); // Unlinked - red
            }

            var pinRect = new Rect(pinX - pinSize, pinY - pinSize, pinSize * 2, pinSize * 2);
            context.FillRectangle(pinBrush, pinRect);

            // Draw pin direction indicator
            var dirPen = new Pen(Brushes.White, 1);
            double angle = pin.AngleDegrees * Math.PI / 180;
            double dirLength = 15;
            context.DrawLine(dirPen,
                new Point(pinX, pinY),
                new Point(pinX + Math.Cos(angle) * dirLength, pinY + Math.Sin(angle) * dirLength));
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
        int numSegments = Math.Max(8, (int)(Math.Abs(bend.SweepAngleDegrees) / 5));
        var points = new List<Point>();

        double startRad = bend.StartAngleDegrees * Math.PI / 180;
        double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;

        for (int i = 0; i <= numSegments; i++)
        {
            double t = i / (double)numSegments;
            double angle = startRad + sweepRad * t;

            double perpAngle = angle - Math.PI / 2 * Math.Sign(bend.SweepAngleDegrees);
            double x = bend.Center.X + bend.RadiusMicrometers * Math.Cos(perpAngle);
            double y = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(perpAngle);
            points.Add(new Point(x, y));
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            context.DrawLine(pen, points[i], points[i + 1]);
        }
    }

    private void DrawModeIndicator(DrawingContext context, Rect bounds)
    {
        var mainVm = MainViewModel;
        if (mainVm == null) return;

        string modeText = mainVm.CurrentMode switch
        {
            InteractionMode.Select => "[S] Select",
            InteractionMode.PlaceComponent => "[P] Place",
            InteractionMode.Connect => "[C] Connect",
            InteractionMode.Delete => "[D] Delete",
            _ => ""
        };

        var brush = mainVm.CurrentMode switch
        {
            InteractionMode.Select => Brushes.LightBlue,
            InteractionMode.PlaceComponent => Brushes.LightGreen,
            InteractionMode.Connect => Brushes.Orange,
            InteractionMode.Delete => Brushes.Red,
            _ => Brushes.White
        };

        var text = new FormattedText(
            modeText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial", FontStyle.Normal, FontWeight.Bold),
            14,
            brush);

        context.DrawText(text, new Point(bounds.Width - 100, 10));
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
        var mainVm = MainViewModel;
        if (vm == null) return;

        var canvasPoint = ScreenToCanvas(point);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Check if in connect mode and clicking on a pin
            if (mainVm?.CurrentMode == InteractionMode.Connect)
            {
                var pin = HitTestPin(canvasPoint);
                if (pin != null)
                {
                    mainVm.PinClicked(pin);
                    InvalidateVisual();
                    e.Handled = true;
                    Focus();
                    return;
                }
            }

            // Check if in place mode - place component
            if (mainVm?.CurrentMode == InteractionMode.PlaceComponent)
            {
                mainVm.CanvasClicked(canvasPoint.X, canvasPoint.Y);
                InvalidateVisual();
                e.Handled = true;
                Focus();
                return;
            }

            // Check if in delete mode
            if (mainVm?.CurrentMode == InteractionMode.Delete)
            {
                mainVm.CanvasClicked(canvasPoint.X, canvasPoint.Y);
                InvalidateVisual();
                e.Handled = true;
                Focus();
                return;
            }

            // Select mode - check if clicking on a component
            _draggingComponent = HitTestComponent(canvasPoint);
            if (_draggingComponent != null)
            {
                foreach (var c in vm.Components) c.IsSelected = false;
                _draggingComponent.IsSelected = true;
                vm.SelectedComponent = _draggingComponent;
                mainVm?.CanvasClicked(canvasPoint.X, canvasPoint.Y);
                // Start tracking for undo
                mainVm?.StartMoveComponent(_draggingComponent);
                InvalidateVisual();
            }
            else
            {
                // Deselect all
                foreach (var c in vm.Components) c.IsSelected = false;
                vm.SelectedComponent = null;
                InvalidateVisual();
            }
        }
        else if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
                 e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
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

        if (_draggingComponent != null && MainViewModel?.CurrentMode == InteractionMode.Select)
        {
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

        // End move tracking for undo
        if (_draggingComponent != null)
        {
            MainViewModel?.EndMoveComponent();
        }

        _draggingComponent = null;
        _isPanning = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y > 0 ? 1.1 : 0.9;
        var newZoom = Math.Clamp(Zoom * delta, 0.1, 10.0);

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var mainVm = MainViewModel;
        if (mainVm == null) return;

        // Check for Ctrl modifiers
        bool ctrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.S:
                if (ctrlPressed)
                    mainVm.SaveDesignCommand.Execute(null);
                else
                    mainVm.SetSelectModeCommand.Execute(null);
                break;
            case Key.C:
                if (!ctrlPressed)
                    mainVm.SetConnectModeCommand.Execute(null);
                break;
            case Key.D:
                if (!ctrlPressed)
                    mainVm.SetDeleteModeCommand.Execute(null);
                break;
            case Key.Delete:
            case Key.Back:
                mainVm.DeleteSelectedCommand.Execute(null);
                break;
            case Key.Escape:
                mainVm.SetSelectModeCommand.Execute(null);
                break;
            case Key.Z:
                if (ctrlPressed)
                    mainVm.UndoCommand.Execute(null);
                break;
            case Key.Y:
                if (ctrlPressed)
                    mainVm.RedoCommand.Execute(null);
                break;
            case Key.R:
                if (!ctrlPressed)
                    mainVm.RotateSelectedCommand.Execute(null);
                break;
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

    private PhysicalPin? HitTestPin(Point canvasPoint)
    {
        var vm = ViewModel;
        if (vm == null) return null;

        foreach (var comp in vm.Components)
        {
            foreach (var pin in comp.Component.PhysicalPins)
            {
                var (pinX, pinY) = pin.GetAbsolutePosition();
                var distance = Math.Sqrt(Math.Pow(canvasPoint.X - pinX, 2) + Math.Pow(canvasPoint.Y - pinY, 2));
                if (distance <= PinHitRadius)
                {
                    return pin;
                }
            }
        }

        return null;
    }
}
