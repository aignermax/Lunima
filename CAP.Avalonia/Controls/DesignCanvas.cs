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

    // Waveguide connection drag & drop state
    private PhysicalPin? _connectionDragStartPin;
    private Point _connectionDragCurrentPoint;

    // Component placement preview state
    private bool _showPlacementPreview;
    private ComponentTemplate? _placementPreviewTemplate;
    private Point _placementPreviewPosition;

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
            // Draw chip boundary
            DrawChipBoundary(context, vm);

            // Draw pathfinding grid overlay (if enabled)
            if (vm.ShowGridOverlay)
            {
                DrawPathfindingGridOverlay(context, vm);
            }

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

            // Draw component placement preview
            if (_showPlacementPreview && _placementPreviewTemplate != null)
            {
                DrawPlacementPreview(context, vm);
            }

            // Draw connection drag preview
            if (_connectionDragStartPin != null)
            {
                DrawConnectionPreview(context);
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

    /// <summary>
    /// Draws the pathfinding grid overlay showing blocked cells.
    /// Red = blocked by component, Blue = blocked by waveguide.
    /// </summary>
    private void DrawPathfindingGridOverlay(DrawingContext context, DesignCanvasViewModel vm)
    {
        var grid = vm.Router.PathfindingGrid;
        if (grid == null) return;

        // Semi-transparent brushes for overlay
        var componentBlockedBrush = new SolidColorBrush(Color.FromArgb(80, 255, 50, 50)); // Red
        var waveguideBlockedBrush = new SolidColorBrush(Color.FromArgb(80, 50, 100, 255)); // Blue
        var freeBrush = new SolidColorBrush(Color.FromArgb(15, 0, 255, 0)); // Very faint green

        double cellSize = grid.CellSizeMicrometers;

        // Only draw visible cells (optimization)
        // Calculate visible bounds in physical coordinates
        double viewMinX = -vm.PanX / Zoom;
        double viewMinY = -vm.PanY / Zoom;
        double viewMaxX = viewMinX + Bounds.Width / Zoom;
        double viewMaxY = viewMinY + Bounds.Height / Zoom;

        // Convert to grid coordinates
        var (gridMinX, gridMinY) = grid.PhysicalToGrid(Math.Max(viewMinX, grid.MinX), Math.Max(viewMinY, grid.MinY));
        var (gridMaxX, gridMaxY) = grid.PhysicalToGrid(Math.Min(viewMaxX, grid.MaxX), Math.Min(viewMaxY, grid.MaxY));

        // Clamp to grid bounds
        gridMinX = Math.Max(0, gridMinX);
        gridMinY = Math.Max(0, gridMinY);
        gridMaxX = Math.Min(grid.Width - 1, gridMaxX);
        gridMaxY = Math.Min(grid.Height - 1, gridMaxY);

        // Limit the number of cells we draw (for performance)
        int maxCellsToDraw = 10000;
        int step = 1;
        int totalCells = (gridMaxX - gridMinX + 1) * (gridMaxY - gridMinY + 1);
        if (totalCells > maxCellsToDraw)
        {
            step = (int)Math.Ceiling(Math.Sqrt((double)totalCells / maxCellsToDraw));
        }

        for (int gx = gridMinX; gx <= gridMaxX; gx += step)
        {
            for (int gy = gridMinY; gy <= gridMaxY; gy += step)
            {
                var (physX, physY) = grid.GridToPhysical(gx, gy);
                var cellRect = new Rect(
                    physX - cellSize * step / 2,
                    physY - cellSize * step / 2,
                    cellSize * step,
                    cellSize * step);

                byte cellState = grid.GetCellState(gx, gy);

                IBrush? brush = cellState switch
                {
                    1 => componentBlockedBrush, // Component obstacle
                    2 => waveguideBlockedBrush, // Waveguide obstacle
                    _ => null // Free cell - don't draw anything for performance
                };

                if (brush != null)
                {
                    context.FillRectangle(brush, cellRect);
                }
            }
        }

        // Draw grid info text
        var infoText = new FormattedText(
            $"Grid: {grid.Width}x{grid.Height} cells, {grid.CellSizeMicrometers}µm/cell, {grid.GetBlockedCellCount()} blocked",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            Brushes.Yellow);

        // Draw at top-left of visible area
        context.DrawText(infoText, new Point(grid.MinX + 10, grid.MinY + 10));
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
        var highlightedPin = ViewModel?.HighlightedPin?.Pin;

        foreach (var pin in comp.Component.PhysicalPins)
        {
            var (pinX, pinY) = pin.GetAbsolutePosition();

            // Check if this is the highlighted pin
            bool isHighlighted = pin == highlightedPin;

            // Draw pin based on connection mode and highlight state
            IBrush pinBrush;
            double pinSize = isConnectMode ? 8 : 5;

            if (isHighlighted)
            {
                // Highlighted pin - bright cyan, larger
                pinBrush = new SolidColorBrush(Color.FromRgb(0, 255, 255));
                pinSize = 12; // Larger size for highlighted pin
            }
            else if (isConnectMode)
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

            // Draw glow effect for highlighted pin
            if (isHighlighted)
            {
                var glowBrush = new SolidColorBrush(Color.FromArgb(100, 0, 255, 255));
                var glowRect = new Rect(pinX - pinSize * 1.5, pinY - pinSize * 1.5, pinSize * 3, pinSize * 3);
                context.FillRectangle(glowBrush, glowRect);
            }

            var pinRect = new Rect(pinX - pinSize, pinY - pinSize, pinSize * 2, pinSize * 2);
            context.FillRectangle(pinBrush, pinRect);

            // Draw pin direction indicator (using absolute angle which includes component rotation)
            var dirPen = new Pen(isHighlighted ? Brushes.Cyan : Brushes.White, isHighlighted ? 2 : 1);
            double angle = pin.GetAbsoluteAngle() * Math.PI / 180;
            double dirLength = isHighlighted ? 20 : 15;
            context.DrawLine(dirPen,
                new Point(pinX, pinY),
                new Point(pinX + Math.Cos(angle) * dirLength, pinY + Math.Sin(angle) * dirLength));

            // Draw pin name for highlighted pin
            if (isHighlighted)
            {
                var pinText = new FormattedText(
                    pin.Name,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    10,
                    Brushes.Cyan);
                context.DrawText(pinText, new Point(pinX + 15, pinY - 15));
            }
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

    private void DrawChipBoundary(DrawingContext context, DesignCanvasViewModel vm)
    {
        // Draw chip area with subtle fill
        var chipRect = new global::Avalonia.Rect(vm.ChipMinX, vm.ChipMinY,
            vm.ChipMaxX - vm.ChipMinX, vm.ChipMaxY - vm.ChipMinY);

        // Subtle chip background
        var chipFill = new SolidColorBrush(Color.FromArgb(20, 100, 150, 255));
        context.FillRectangle(chipFill, chipRect);

        // Chip border
        var chipBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 100, 150, 255)), 2);
        context.DrawRectangle(null, chipBorderPen, chipRect);

        // Corner markers
        var cornerSize = 30.0;
        var cornerPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 100, 150, 255)), 3);

        // Top-left corner
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMinY + cornerSize), new Point(vm.ChipMinX, vm.ChipMinY));
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMinY), new Point(vm.ChipMinX + cornerSize, vm.ChipMinY));

        // Top-right corner
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX - cornerSize, vm.ChipMinY), new Point(vm.ChipMaxX, vm.ChipMinY));
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX, vm.ChipMinY), new Point(vm.ChipMaxX, vm.ChipMinY + cornerSize));

        // Bottom-left corner
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMaxY - cornerSize), new Point(vm.ChipMinX, vm.ChipMaxY));
        context.DrawLine(cornerPen, new Point(vm.ChipMinX, vm.ChipMaxY), new Point(vm.ChipMinX + cornerSize, vm.ChipMaxY));

        // Bottom-right corner
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX - cornerSize, vm.ChipMaxY), new Point(vm.ChipMaxX, vm.ChipMaxY));
        context.DrawLine(cornerPen, new Point(vm.ChipMaxX, vm.ChipMaxY), new Point(vm.ChipMaxX, vm.ChipMaxY - cornerSize));

        // Chip dimensions label
        var dimText = new FormattedText(
            $"{vm.ChipMaxX - vm.ChipMinX}µm × {vm.ChipMaxY - vm.ChipMinY}µm",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            12,
            new SolidColorBrush(Color.FromArgb(150, 100, 150, 255)));

        context.DrawText(dimText, new Point(vm.ChipMinX + 5, vm.ChipMaxY + 5));
    }

    private void DrawPlacementPreview(DrawingContext context, DesignCanvasViewModel vm)
    {
        if (_placementPreviewTemplate == null) return;

        double width = _placementPreviewTemplate.WidthMicrometers;
        double height = _placementPreviewTemplate.HeightMicrometers;
        double x = _placementPreviewPosition.X - width / 2;
        double y = _placementPreviewPosition.Y - height / 2;

        // Check if placement would be valid
        bool canPlace = vm.CanPlaceComponent(x, y, width, height);

        // Draw preview rectangle
        var fillColor = canPlace
            ? Color.FromArgb(50, 100, 255, 100)  // Green tint for valid
            : Color.FromArgb(50, 255, 100, 100); // Red tint for invalid

        var borderColor = canPlace
            ? Color.FromArgb(200, 100, 255, 100)
            : Color.FromArgb(200, 255, 100, 100);

        var previewRect = new global::Avalonia.Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(fillColor), previewRect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), 2), previewRect);

        // Draw component name
        var nameText = new FormattedText(
            _placementPreviewTemplate.Name,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            new SolidColorBrush(borderColor));

        context.DrawText(nameText, new Point(x + 5, y + 5));
    }

    private void DrawConnectionPreview(DrawingContext context)
    {
        if (_connectionDragStartPin == null) return;

        var (startX, startY) = _connectionDragStartPin.GetAbsolutePosition();

        // Check if hovering over a valid target pin
        var targetPin = HitTestPin(_connectionDragCurrentPoint);
        bool isValidTarget = targetPin != null && targetPin != _connectionDragStartPin &&
                             targetPin.ParentComponent != _connectionDragStartPin.ParentComponent;

        // Use green for valid, gray dashed for dragging
        var previewPen = isValidTarget
            ? new Pen(Brushes.LimeGreen, 2)
            : new Pen(Brushes.Gray, 2) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };

        var endPoint = isValidTarget
            ? new Point(targetPin!.GetAbsolutePosition().x, targetPin.GetAbsolutePosition().y)
            : _connectionDragCurrentPoint;

        context.DrawLine(previewPen, new Point(startX, startY), endPoint);

        // Draw circle at start point
        context.DrawEllipse(Brushes.LimeGreen, null, new Point(startX, startY), 5, 5);

        // Draw circle at end if valid target
        if (isValidTarget)
        {
            context.DrawEllipse(Brushes.LimeGreen, null, endPoint, 5, 5);
        }
    }

    private void DrawWaveguideConnection(DrawingContext context, WaveguideConnectionViewModel conn)
    {
        var segments = conn.Connection.GetPathSegments();

        // Choose pen based on selection and blocked fallback state
        Pen waveguidePen;
        if (conn.IsSelected)
        {
            waveguidePen = new Pen(Brushes.Yellow, 3);
        }
        else if (conn.IsBlockedFallback)
        {
            // Red dashed line for blocked/invalid paths
            waveguidePen = new Pen(Brushes.Red, 2)
            {
                DashStyle = new DashStyle(new double[] { 5, 3 }, 0)
            };
        }
        else
        {
            waveguidePen = new Pen(Brushes.Orange, 2);
        }

        // Check if path segments are still valid (endpoints match current pin positions)
        bool pathIsStale = false;
        if (segments.Count > 0)
        {
            var firstSeg = segments[0];
            var lastSeg = segments[^1];
            double startDist = Math.Sqrt(Math.Pow(firstSeg.StartPoint.X - conn.StartX, 2) +
                                         Math.Pow(firstSeg.StartPoint.Y - conn.StartY, 2));
            double endDist = Math.Sqrt(Math.Pow(lastSeg.EndPoint.X - conn.EndX, 2) +
                                       Math.Pow(lastSeg.EndPoint.Y - conn.EndY, 2));
            pathIsStale = startDist > 1.0 || endDist > 1.0; // More than 1µm mismatch
        }

        if (segments.Count == 0 || pathIsStale)
        {
            // Fallback: draw simple line if no path calculated or path is stale (during drag)
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
        // Use the pre-calculated start and end points from the BendSegment
        // to ensure continuity with adjacent straight segments
        int numSegments = Math.Max(8, (int)(Math.Abs(bend.SweepAngleDegrees) / 5));

        if (numSegments == 0 || Math.Abs(bend.SweepAngleDegrees) < 0.1)
        {
            // Degenerate case - just draw a line
            context.DrawLine(pen,
                new Point(bend.StartPoint.X, bend.StartPoint.Y),
                new Point(bend.EndPoint.X, bend.EndPoint.Y));
            return;
        }

        var points = new List<Point>();

        // Interpolate points along the arc using the known start and end points
        for (int i = 0; i <= numSegments; i++)
        {
            double t = i / (double)numSegments;

            // Calculate angle along the arc
            double startRad = bend.StartAngleDegrees * Math.PI / 180;
            double sweepRad = bend.SweepAngleDegrees * Math.PI / 180;
            double angle = startRad + sweepRad * t;

            // Calculate perpendicular angle (points away from center for arc on outside of turn)
            double sign = Math.Sign(bend.SweepAngleDegrees);
            if (sign == 0) sign = 1;
            double perpAngle = angle - Math.PI / 2 * sign;

            double x = bend.Center.X + bend.RadiusMicrometers * Math.Cos(perpAngle);
            double y = bend.Center.Y + bend.RadiusMicrometers * Math.Sin(perpAngle);
            points.Add(new Point(x, y));
        }

        // Draw the arc as line segments
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

        string gridInfo = vm.ShowGridOverlay ? " | [G] Grid: ON" : " | [G] Grid: OFF";

        var statusText = new FormattedText(
            $"Zoom: {Zoom:P0} | Components: {vm.Components.Count} | Connections: {vm.Connections.Count}{gridInfo}",
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
            // Check if in connect mode and clicking on a pin - start drag for connection
            if (mainVm?.CurrentMode == InteractionMode.Connect)
            {
                var pin = HitTestPin(canvasPoint);
                if (pin != null)
                {
                    // Start dragging a connection from this pin
                    _connectionDragStartPin = pin;
                    _connectionDragCurrentPoint = canvasPoint;
                    mainVm.StatusText = $"Drag to another pin to connect from {pin.Name}...";
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

        var canvasPoint = ScreenToCanvas(point);

        // Update component placement preview
        if (MainViewModel?.CurrentMode == InteractionMode.PlaceComponent &&
            MainViewModel?.SelectedTemplate != null)
        {
            _showPlacementPreview = true;
            _placementPreviewTemplate = MainViewModel.SelectedTemplate;
            _placementPreviewPosition = canvasPoint;
            InvalidateVisual();
        }
        else
        {
            if (_showPlacementPreview)
            {
                _showPlacementPreview = false;
                InvalidateVisual();
            }
        }

        // Update pin highlighting in Connect mode
        if (MainViewModel?.CurrentMode == InteractionMode.Connect)
        {
            MainViewModel.CanvasMouseMove(canvasPoint.X, canvasPoint.Y);
            InvalidateVisual(); // Redraw to show highlighting
        }

        // Update connection drag preview
        if (_connectionDragStartPin != null)
        {
            _connectionDragCurrentPoint = canvasPoint;

            // Check if hovering over a valid target pin
            var targetPin = HitTestPin(_connectionDragCurrentPoint);
            if (targetPin != null && targetPin != _connectionDragStartPin &&
                targetPin.ParentComponent != _connectionDragStartPin.ParentComponent)
            {
                MainViewModel!.StatusText = $"Release to connect {_connectionDragStartPin.Name} to {targetPin.Name}";
            }
            else
            {
                MainViewModel!.StatusText = $"Drag to another pin to connect from {_connectionDragStartPin.Name}...";
            }

            InvalidateVisual();
        }
        else if (_draggingComponent != null && MainViewModel?.CurrentMode == InteractionMode.Select)
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

        // Complete connection drag & drop
        if (_connectionDragStartPin != null)
        {
            var point = e.GetPosition(this);
            var canvasPoint = ScreenToCanvas(point);
            var targetPin = HitTestPin(canvasPoint);

            if (targetPin != null && targetPin != _connectionDragStartPin &&
                targetPin.ParentComponent != _connectionDragStartPin.ParentComponent)
            {
                // Create the connection via command
                var cmd = new Commands.CreateConnectionCommand(ViewModel!, _connectionDragStartPin, targetPin);
                MainViewModel?.CommandManager.ExecuteCommand(cmd);
                MainViewModel!.StatusText = $"Connected {_connectionDragStartPin.Name} to {targetPin.Name}";
            }
            else
            {
                MainViewModel!.StatusText = "Connect mode: Drag from a pin to another pin to connect";
            }

            _connectionDragStartPin = null;
            InvalidateVisual();
        }

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
            case Key.G:
                // Toggle grid overlay
                if (!ctrlPressed)
                {
                    var vm = ViewModel;
                    if (vm != null)
                    {
                        vm.ShowGridOverlay = !vm.ShowGridOverlay;
                    }
                }
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
