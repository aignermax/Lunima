using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Visualization;
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

    // Drag preview state (shows snap target + collision feedback)
    private double _dragStartX;
    private double _dragStartY;
    private bool _showDragPreview;
    private Point _dragPreviewPosition; // top-left of snapped target
    private bool _dragPreviewValid;

    // Power flow hover state
    private WaveguideConnectionViewModel? _hoveredConnection;
    private Point _lastCanvasPosition;

    static DesignCanvas()
    {
        AffectsRender<DesignCanvas>(ViewModelProperty, ZoomProperty);
        MainViewModelProperty.Changed.AddClassHandler<DesignCanvas>((canvas, e) => canvas.OnMainViewModelChanged(e));
        ViewModelProperty.Changed.AddClassHandler<DesignCanvas>((canvas, e) => canvas.OnViewModelChanged(e));
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

    private void OnViewModelChanged(AvaloniaPropertyChangedEventArgs e)
    {
        // Subscribe to DesignCanvasViewModel property changes for simulation redraw
        if (e.OldValue is DesignCanvasViewModel oldCanvas)
        {
            oldCanvas.PropertyChanged -= OnCanvasViewModelPropertyChanged;
        }

        if (e.NewValue is DesignCanvasViewModel newCanvas)
        {
            newCanvas.PropertyChanged += OnCanvasViewModelPropertyChanged;
        }
    }

    private void OnCanvasViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignCanvasViewModel.ShowPowerFlow) ||
            e.PropertyName == nameof(DesignCanvasViewModel.IsRouting))
        {
            InvalidateVisual();
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

            // Draw snap grid overlay (if enabled)
            if (vm.GridSnap.IsEnabled)
            {
                DrawSnapGridOverlay(context, vm);
            }

            // Draw pathfinding grid overlay (if enabled)
            if (vm.ShowGridOverlay)
            {
                DrawPathfindingGridOverlay(context, vm);
            }

            // Draw connections first (behind components)
            foreach (var conn in vm.Connections)
            {
                DrawWaveguideConnection(context, conn, vm);
            }

            // Draw power label on hovered connection
            if (vm.ShowPowerFlow && _hoveredConnection != null)
            {
                DrawPowerHoverLabel(context, _hoveredConnection, vm);
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

            // Draw drag preview (snap target + collision feedback)
            if (_showDragPreview && _draggingComponent != null)
            {
                DrawDragPreview(context);
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
    /// Draws a snap grid overlay showing snap points within the visible chip area.
    /// Rendered in canvas coordinates (inside zoom/pan transform).
    /// </summary>
    private void DrawSnapGridOverlay(DrawingContext context, DesignCanvasViewModel vm)
    {
        double gridSize = vm.GridSnap.GridSizeMicrometers;
        if (gridSize <= 0) return;

        var dotBrush = new SolidColorBrush(Color.FromArgb(100, 0, 200, 255));
        double dotRadius = 1.5;

        // Calculate visible bounds in physical coordinates
        double viewMinX = -vm.PanX / Zoom;
        double viewMinY = -vm.PanY / Zoom;
        double viewMaxX = viewMinX + Bounds.Width / Zoom;
        double viewMaxY = viewMinY + Bounds.Height / Zoom;

        // Clamp to chip boundaries
        double startX = Math.Max(vm.ChipMinX, Math.Floor(viewMinX / gridSize) * gridSize);
        double startY = Math.Max(vm.ChipMinY, Math.Floor(viewMinY / gridSize) * gridSize);
        double endX = Math.Min(vm.ChipMaxX, viewMaxX);
        double endY = Math.Min(vm.ChipMaxY, viewMaxY);

        // Limit number of dots for performance
        const int MaxDotsPerAxis = 200;
        double stepX = gridSize;
        double stepY = gridSize;
        int countX = (int)((endX - startX) / gridSize) + 1;
        int countY = (int)((endY - startY) / gridSize) + 1;
        if (countX > MaxDotsPerAxis)
            stepX = (endX - startX) / MaxDotsPerAxis;
        if (countY > MaxDotsPerAxis)
            stepY = (endY - startY) / MaxDotsPerAxis;

        for (double x = startX; x <= endX; x += stepX)
        {
            for (double y = startY; y <= endY; y += stepY)
            {
                context.DrawEllipse(dotBrush, null, new Point(x, y), dotRadius, dotRadius);
            }
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

        // Draw A* paths for all connections (if available)
        var astarPathBrush = new SolidColorBrush(Color.FromArgb(150, 255, 165, 0)); // Orange
        foreach (var conn in vm.Connections)
        {
            var gridPath = conn.Connection.RoutedPath?.DebugGridPath;
            if (gridPath != null && gridPath.Count > 0)
            {
                // Draw each grid cell in the A* path
                foreach (var node in gridPath)
                {
                    var (physX, physY) = grid.GridToPhysical(node.X, node.Y);
                    var cellRect = new Rect(
                        physX - cellSize / 2,
                        physY - cellSize / 2,
                        cellSize,
                        cellSize);
                    context.FillRectangle(astarPathBrush, cellRect);
                }

                // Optionally draw path direction arrows (commented out for performance)
                // for (int i = 0; i < gridPath.Count - 1; i++)
                // {
                //     var (x1, y1) = grid.GridToPhysical(gridPath[i].X, gridPath[i].Y);
                //     var (x2, y2) = grid.GridToPhysical(gridPath[i + 1].X, gridPath[i + 1].Y);
                //     var pen = new Pen(Brushes.Yellow, 1);
                //     context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                // }
            }
        }

        // Draw grid info text
        int totalAstarPaths = vm.Connections.Count(c => c.Connection.RoutedPath?.DebugGridPath != null);
        var infoText = new FormattedText(
            $"Grid: {grid.Width}x{grid.Height} cells, {grid.CellSizeMicrometers}µm/cell, {grid.GetBlockedCellCount()} blocked | A* paths: {totalAstarPaths}",
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
                context.DrawEllipse(glowBrush, null, new Point(pinX, pinY), pinSize * 1.5, pinSize * 1.5);
            }

            context.DrawEllipse(pinBrush, null, new Point(pinX, pinY), pinSize, pinSize);

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

    private void DrawDragPreview(DrawingContext context)
    {
        if (_draggingComponent == null) return;

        double width = _draggingComponent.Width;
        double height = _draggingComponent.Height;
        double x = _dragPreviewPosition.X;
        double y = _dragPreviewPosition.Y;

        var fillColor = _dragPreviewValid
            ? Color.FromArgb(50, 100, 255, 100)   // Green tint for valid
            : Color.FromArgb(50, 255, 100, 100);  // Red tint for invalid

        var borderColor = _dragPreviewValid
            ? Color.FromArgb(200, 100, 255, 100)
            : Color.FromArgb(200, 255, 100, 100);

        var previewRect = new global::Avalonia.Rect(x, y, width, height);
        context.FillRectangle(new SolidColorBrush(fillColor), previewRect);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), 2), previewRect);
    }

    private void DrawConnectionPreview(DrawingContext context)
    {
        if (_connectionDragStartPin == null) return;

        var (startX, startY) = _connectionDragStartPin.GetAbsolutePosition();

        // Use the highlighted pin (set by UpdatePinHighlight) for consistent hit-testing
        var targetPin = ViewModel?.HighlightedPin?.Pin;
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

    private void DrawWaveguideConnection(
        DrawingContext context,
        WaveguideConnectionViewModel conn,
        DesignCanvasViewModel vm)
    {
        var segments = conn.Connection.GetPathSegments();

        // Choose pen based on power flow overlay, selection, and blocked state
        Pen waveguidePen;
        if (conn.IsSelected)
        {
            waveguidePen = new Pen(Brushes.Yellow, 3);
        }
        else if (vm.ShowPowerFlow && vm.PowerFlowVisualizer.CurrentResult != null)
        {
            var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
            waveguidePen = flow != null
                ? PowerFlowRenderer.CreatePowerPen(flow, vm.PowerFlowVisualizer.FadeThresholdDb)
                : new Pen(new SolidColorBrush(Color.FromArgb(40, 80, 80, 120)), 1);
        }
        else if (conn.IsBlockedFallback || conn.Connection.RoutedPath?.IsInvalidGeometry == true)
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

        // Draw connection info - show power data when simulation is active
        var midX = (conn.StartX + conn.EndX) / 2;
        var midY = (conn.StartY + conn.EndY) / 2;
        string labelText;
        IBrush labelBrush;

        if (vm.ShowPowerFlow && vm.PowerFlowVisualizer.CurrentResult != null)
        {
            var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
            if (flow != null && flow.AveragePower > 0)
            {
                labelText = $"{flow.NormalizedPowerDb:F1}dB ({flow.NormalizedPowerFraction * 100:F0}%)";
                var fraction = Math.Clamp(flow.NormalizedPowerFraction, 0, 1);
                labelBrush = new SolidColorBrush(PowerFlowRenderer.InterpolatePowerColor(fraction));
            }
            else
            {
                labelText = "no signal";
                labelBrush = new SolidColorBrush(Color.FromArgb(120, 150, 150, 150));
            }
        }
        else
        {
            labelText = $"{conn.PathLength:F0}µm, {conn.LossDb:F2}dB";
            labelBrush = Brushes.LightGray;
        }

        var infoText = new FormattedText(
            labelText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            10,
            labelBrush);

        context.DrawText(infoText, new Point(midX, midY - 15));
    }

    private void DrawPowerHoverLabel(
        DrawingContext context,
        WaveguideConnectionViewModel conn,
        DesignCanvasViewModel vm)
    {
        var flow = vm.PowerFlowVisualizer.GetFlowForConnection(conn.Connection.Id);
        if (flow == null) return;

        var midX = (conn.StartX + conn.EndX) / 2;
        var midY = (conn.StartY + conn.EndY) / 2;
        PowerFlowRenderer.DrawPowerLabel(context, flow, new Point(midX, midY - 25));
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

        string snapInfo = vm.GridSnap.IsEnabled
            ? $" | [G] Snap: {vm.GridSnap.GridSizeMicrometers}µm"
            : " | [G] Snap: OFF";
        string gridInfo = vm.ShowGridOverlay ? " | [Shift+G] Grid: ON" : "";
        string powerInfo = vm.ShowPowerFlow ? " | [P] Power: ON" : " | [P] Power: OFF";

        var statusText = new FormattedText(
            $"Zoom: {Zoom:P0} | Components: {vm.Components.Count} | Connections: {vm.Connections.Count}{snapInfo}{gridInfo}{powerInfo}",
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
                // Use the highlighted pin (from hover) if available, otherwise hit test
                var pin = vm.HighlightedPin?.Pin ?? HitTestPin(canvasPoint);
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
                // Apply grid snap to placement position if enabled
                var snapSettings = vm.GridSnap;
                var (placementX, placementY) = snapSettings.Snap(canvasPoint.X, canvasPoint.Y);
                mainVm.CanvasClicked(placementX, placementY);
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
                _dragStartX = _draggingComponent.X;
                _dragStartY = _draggingComponent.Y;
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
            var snapSettings = vm.GridSnap;

            // Snap the center point (consistent with placement and drag behavior)
            var (snappedCenterX, snappedCenterY) = snapSettings.Snap(canvasPoint.X, canvasPoint.Y);

            // Store center position for preview rendering
            _placementPreviewPosition = new Point(snappedCenterX, snappedCenterY);
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
            if (_connectionDragStartPin != null)
            {
                // During drag: use drag start pin as exclude so same-component pins are excluded
                vm.UpdatePinHighlight(canvasPoint.X, canvasPoint.Y, _connectionDragStartPin);
            }
            else
            {
                // Not dragging: use normal flow (MainViewModel tracks click-to-connect state)
                MainViewModel.CanvasMouseMove(canvasPoint.X, canvasPoint.Y);
            }
            InvalidateVisual();
        }

        // Update power flow hover detection
        if (vm.ShowPowerFlow)
        {
            _lastCanvasPosition = canvasPoint;
            var previousHover = _hoveredConnection;
            _hoveredConnection = HitTestConnection(canvasPoint);
            if (_hoveredConnection != previousHover)
            {
                InvalidateVisual();
            }
        }
        else if (_hoveredConnection != null)
        {
            _hoveredConnection = null;
        }

        // Update connection drag preview
        if (_connectionDragStartPin != null)
        {
            _connectionDragCurrentPoint = canvasPoint;

            // Use the highlighted pin (already set by UpdatePinHighlight above) for consistent targeting
            var targetPin = vm.HighlightedPin?.Pin;
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
            // Move smoothly during drag (snap + collision checked on release)
            vm.MoveComponent(_draggingComponent, delta.X / Zoom, delta.Y / Zoom);

            // Calculate drag preview (shows where component will land on release)
            var snapSettings = vm.GridSnap;
            double centerX = _draggingComponent.X + _draggingComponent.Width / 2.0;
            double centerY = _draggingComponent.Y + _draggingComponent.Height / 2.0;
            var (snappedCX, snappedCY) = snapSettings.Snap(centerX, centerY);
            double previewX = snappedCX - _draggingComponent.Width / 2.0;
            double previewY = snappedCY - _draggingComponent.Height / 2.0;
            _dragPreviewPosition = new Point(previewX, previewY);
            _dragPreviewValid = vm.CanMoveComponentTo(_draggingComponent, previewX, previewY);
            // Show preview when grid snap is on (snap target differs) or position is invalid (red warning)
            _showDragPreview = snapSettings.IsEnabled || !_dragPreviewValid;

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
            // Use the highlighted pin — same pin the user saw highlighted during hover
            var targetPin = ViewModel?.HighlightedPin?.Pin;

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
                // Dropped into empty space - check if start pin has existing connection to delete
                var existingConnection = ViewModel?.GetConnectionForPin(_connectionDragStartPin);
                if (existingConnection != null)
                {
                    // Delete the existing connection (drag-to-delete)
                    var deleteCmd = new Commands.DeleteConnectionCommand(ViewModel!, existingConnection);
                    MainViewModel?.CommandManager.ExecuteCommand(deleteCmd);
                    MainViewModel!.StatusText = $"Deleted connection from {_connectionDragStartPin.Name}";
                }
                else
                {
                    MainViewModel!.StatusText = "Connect mode: Drag from a pin to another pin to connect";
                }
            }

            _connectionDragStartPin = null;
            InvalidateVisual();
        }

        // End move tracking for undo
        if (_draggingComponent != null)
        {
            var vm = ViewModel;

            // Calculate final position (with grid snap if enabled)
            double finalX = _draggingComponent.X;
            double finalY = _draggingComponent.Y;

            var snapSettings = vm?.GridSnap;
            if (snapSettings != null && snapSettings.IsEnabled)
            {
                // Snap the CENTER of the component (consistent with placement behavior)
                double centerX = _draggingComponent.X + _draggingComponent.Width / 2.0;
                double centerY = _draggingComponent.Y + _draggingComponent.Height / 2.0;
                var (snappedCX, snappedCY) = snapSettings.Snap(centerX, centerY);
                finalX = snappedCX - _draggingComponent.Width / 2.0;
                finalY = snappedCY - _draggingComponent.Height / 2.0;
            }

            // Check collision at final position
            bool canPlace = vm?.CanMoveComponentTo(_draggingComponent, finalX, finalY) ?? true;

            if (canPlace)
            {
                // Move to final (possibly snapped) position
                double deltaX = finalX - _draggingComponent.X;
                double deltaY = finalY - _draggingComponent.Y;
                if (Math.Abs(deltaX) > 0.001 || Math.Abs(deltaY) > 0.001)
                {
                    vm?.MoveComponent(_draggingComponent, deltaX, deltaY);
                }
            }
            else
            {
                // Revert to start position - drop is invalid
                double deltaX = _dragStartX - _draggingComponent.X;
                double deltaY = _dragStartY - _draggingComponent.Y;
                if (Math.Abs(deltaX) > 0.001 || Math.Abs(deltaY) > 0.001)
                {
                    vm?.MoveComponent(_draggingComponent, deltaX, deltaY);
                }
                if (MainViewModel != null)
                    MainViewModel.StatusText = "Cannot place here - overlaps with another component";
            }

            _showDragPreview = false;
            MainViewModel?.EndMoveComponent();
            InvalidateVisual();
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
                if (!ctrlPressed)
                {
                    var vm = ViewModel;
                    if (vm != null)
                    {
                        bool shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                        if (shiftPressed)
                        {
                            // Shift+G: toggle pathfinding grid overlay
                            vm.ShowGridOverlay = !vm.ShowGridOverlay;
                        }
                        else
                        {
                            // G: toggle grid snap
                            vm.GridSnap.Toggle();
                            mainVm.StatusText = vm.GridSnap.IsEnabled
                                ? $"Grid snap ON ({vm.GridSnap.GridSizeMicrometers}µm)"
                                : "Grid snap OFF";
                        }
                    }
                }
                break;
            case Key.F:
                if (!ctrlPressed)
                {
                    mainVm.ZoomToFit(Bounds.Width, Bounds.Height);
                }
                break;
            case Key.P:
                // Toggle power flow overlay
                if (!ctrlPressed)
                {
                    var canvasVm = ViewModel;
                    if (canvasVm != null)
                    {
                        if (!canvasVm.ShowPowerFlow)
                        {
                            // Turning on - run simulation if no data exists
                            if (canvasVm.PowerFlowVisualizer.CurrentResult == null)
                            {
                                mainVm?.RunSimulationCommand.Execute(null);
                            }
                            else
                            {
                                canvasVm.ShowPowerFlow = true;
                                canvasVm.PowerFlowVisualizer.IsEnabled = true;
                            }
                            if (mainVm != null)
                                mainVm.StatusText = "Power flow overlay: ON (auto-updates on changes)";
                        }
                        else
                        {
                            // Turning off
                            canvasVm.ShowPowerFlow = false;
                            canvasVm.PowerFlowVisualizer.IsEnabled = false;
                            if (mainVm != null)
                                mainVm.StatusText = "Power flow overlay: OFF";
                        }
                    }
                }
                break;
            case Key.L:
                // Run light simulation
                if (!ctrlPressed)
                {
                    mainVm?.RunSimulationCommand.Execute(null);
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

    private WaveguideConnectionViewModel? HitTestConnection(Point canvasPoint)
    {
        var vm = ViewModel;
        if (vm == null) return null;

        const double hitTolerance = 10.0;

        foreach (var conn in vm.Connections)
        {
            double distance = PointToSegmentDistance(
                canvasPoint.X, canvasPoint.Y,
                conn.StartX, conn.StartY,
                conn.EndX, conn.EndY);

            if (distance <= hitTolerance)
                return conn;
        }

        return null;
    }

    private static double PointToSegmentDistance(
        double px, double py,
        double x1, double y1,
        double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSq = dx * dx + dy * dy;

        if (lengthSq < 0.0001)
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));

        var t = Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / lengthSq));
        var projX = x1 + t * dx;
        var projY = y1 + t * dy;

        return Math.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
    }

    /// <summary>
    /// Finds the nearest pin within hit radius of the given canvas point.
    /// </summary>
    private PhysicalPin? HitTestPin(Point canvasPoint)
    {
        var vm = ViewModel;
        if (vm == null) return null;

        PhysicalPin? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (var comp in vm.Components)
        {
            foreach (var pin in comp.Component.PhysicalPins)
            {
                var (pinX, pinY) = pin.GetAbsolutePosition();
                var distance = Math.Sqrt(Math.Pow(canvasPoint.X - pinX, 2) + Math.Pow(canvasPoint.Y - pinY, 2));
                if (distance < nearestDistance && distance <= PinHitRadius)
                {
                    nearest = pin;
                    nearestDistance = distance;
                }
            }
        }

        return nearest;
    }
}
