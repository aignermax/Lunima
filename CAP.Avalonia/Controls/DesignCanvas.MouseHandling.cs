using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Mouse and keyboard event handling methods for DesignCanvas.
/// </summary>
public partial class DesignCanvas
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetPosition(this);
        _interactionState.LastPointerPosition = point;

        var vm = ViewModel;
        var mainVm = MainViewModel;
        if (vm == null) return;

        var canvasPoint = ScreenToCanvas(point);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            HandleLeftButtonPressed(e, canvasPoint, vm, mainVm);
        }
        else if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
                 e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _interactionState.IsPanning = true;
        }

        e.Handled = true;
        Focus();
    }

    private void HandleLeftButtonPressed(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel vm, MainViewModel? mainVm)
    {
        if (mainVm?.CurrentMode == InteractionMode.Connect)
        {
            HandleConnectModeClick(e, canvasPoint, vm, mainVm);
        }
        else if (mainVm?.CurrentMode == InteractionMode.PlaceComponent)
        {
            HandlePlaceComponentClick(canvasPoint, vm, mainVm);
        }
        else if (mainVm?.CurrentMode == InteractionMode.Delete)
        {
            HandleDeleteModeClick(canvasPoint, mainVm);
        }
        else
        {
            HandleSelectModeClick(e, canvasPoint, vm, mainVm);
        }
    }

    private void HandleConnectModeClick(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel vm, MainViewModel mainVm)
    {
        var pin = vm.HighlightedPin?.Pin ?? DesignCanvasHitTesting.HitTestPin(canvasPoint, vm);
        if (pin != null)
        {
            _interactionState.ConnectionDragStartPin = pin;
            _interactionState.ConnectionDragCurrentPoint = canvasPoint;
            mainVm.BottomPanel.StatusText = $"Drag to another pin to connect from {pin.Name}...";
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void HandlePlaceComponentClick(Point canvasPoint, DesignCanvasViewModel vm, MainViewModel mainVm)
    {
        var snapSettings = vm.GridSnap;
        var (placementX, placementY) = snapSettings.Snap(canvasPoint.X, canvasPoint.Y);
        mainVm.CanvasClicked(placementX, placementY);
        InvalidateVisual();
    }

    private void HandleDeleteModeClick(Point canvasPoint, MainViewModel mainVm)
    {
        mainVm.CanvasClicked(canvasPoint.X, canvasPoint.Y);
        InvalidateVisual();
    }

    private void HandleSelectModeClick(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel vm, MainViewModel? mainVm)
    {
        _interactionState.DraggingComponent = DesignCanvasHitTesting.HitTestComponent(canvasPoint, vm);
        if (_interactionState.DraggingComponent != null)
        {
            // Check for double-click
            bool isDoubleClick = CheckForDoubleClick(e, canvasPoint, _interactionState.DraggingComponent);
            if (isDoubleClick)
            {
                HandleDoubleClick(_interactionState.DraggingComponent, vm);
                _interactionState.DraggingComponent = null;
            }
            else
            {
                HandleComponentSelection(e, canvasPoint, vm, mainVm);
            }
        }
        else
        {
            StartBoxSelection(canvasPoint, vm);
        }
    }

    /// <summary>
    /// Checks if the current click is a double-click on the same component.
    /// </summary>
    private bool CheckForDoubleClick(PointerPressedEventArgs e, Point canvasPoint, ComponentViewModel? component)
    {
        var now = DateTime.UtcNow;
        var timeSinceLastClick = (now - _interactionState.LastClickTime).TotalMilliseconds;
        var distanceFromLastClick = Math.Sqrt(
            Math.Pow(canvasPoint.X - _interactionState.LastClickPosition.X, 2) +
            Math.Pow(canvasPoint.Y - _interactionState.LastClickPosition.Y, 2));

        bool isDoubleClick = timeSinceLastClick < CanvasInteractionState.DoubleClickTimeMs &&
                             distanceFromLastClick < CanvasInteractionState.DoubleClickDistanceThreshold &&
                             _interactionState.LastClickedComponent == component;

        // Update last click state
        _interactionState.LastClickTime = now;
        _interactionState.LastClickPosition = canvasPoint;
        _interactionState.LastClickedComponent = component;

        return isDoubleClick;
    }

    /// <summary>
    /// Handles double-click on a component (enters group edit mode if component belongs to a group).
    /// </summary>
    private void HandleDoubleClick(ComponentViewModel component, DesignCanvasViewModel vm)
    {
        if (component?.Component.ParentGroupInstanceId != null)
        {
            vm.EnterGroupEditModeForComponent(component);
            InvalidateVisual();
        }
    }

    private void HandleComponentSelection(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel vm, MainViewModel? mainVm)
    {
        // Check if component can be selected in current edit context
        if (!vm.CanSelectComponent(_interactionState.DraggingComponent!))
        {
            _interactionState.DraggingComponent = null;
            return;
        }

        bool isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isAltPressed = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (isCtrlPressed)
        {
            vm.Selection.AddToSelection(_interactionState.DraggingComponent!);
            _interactionState.DraggingComponent = null;
        }
        else if (isAltPressed)
        {
            vm.Selection.RemoveFromSelection(_interactionState.DraggingComponent!);
            _interactionState.DraggingComponent = null;
        }
        else
        {
            if (!vm.Selection.SelectedComponents.Contains(_interactionState.DraggingComponent))
            {
                vm.Selection.SelectSingle(_interactionState.DraggingComponent!);
                vm.SelectedComponent = _interactionState.DraggingComponent;
            }
        }

        if (_interactionState.DraggingComponent != null)
        {
            mainVm?.CanvasClicked(canvasPoint.X, canvasPoint.Y);

            // Start tracking for undo
            if (vm.Selection.SelectedComponents.Count > 1)
            {
                // Group move
                mainVm?.StartGroupMove(vm.Selection.SelectedComponents);
            }
            else
            {
                // Single component move
                mainVm?.StartMoveComponent(_interactionState.DraggingComponent);
            }

            _interactionState.DragStartX = _interactionState.DraggingComponent.X;
            _interactionState.DragStartY = _interactionState.DraggingComponent.Y;

            _interactionState.GroupDragStartPositions.Clear();
            foreach (var comp in vm.Selection.SelectedComponents)
            {
                _interactionState.GroupDragStartPositions[comp] = (comp.X, comp.Y);
            }
        }

        InvalidateVisual();
    }

    private void StartBoxSelection(Point canvasPoint, DesignCanvasViewModel vm)
    {
        vm.Selection.IsBoxSelecting = true;
        vm.Selection.BoxStartX = canvasPoint.X;
        vm.Selection.BoxStartY = canvasPoint.Y;
        vm.Selection.BoxEndX = canvasPoint.X;
        vm.Selection.BoxEndY = canvasPoint.Y;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);
        var delta = point - _interactionState.LastPointerPosition;

        var vm = ViewModel;
        if (vm == null) return;

        var canvasPoint = ScreenToCanvas(point);

        // Always track the last canvas position for paste-at-cursor
        _interactionState.LastCanvasPosition = canvasPoint;

        UpdatePlacementPreview(canvasPoint, vm);
        UpdatePinHighlighting(canvasPoint, vm);
        UpdatePowerFlowHover(canvasPoint, vm);
        UpdateGroupHover(canvasPoint, vm);
        UpdateConnectionDragPreview(canvasPoint, vm);
        UpdateComponentDrag(delta, canvasPoint, vm);
        UpdateBoxSelection(canvasPoint, vm);
        UpdatePanning(delta, vm);

        _interactionState.LastPointerPosition = point;
    }

    private void UpdatePlacementPreview(Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (MainViewModel?.CurrentMode == InteractionMode.PlaceComponent &&
            MainViewModel?.LeftPanel.SelectedTemplate != null)
        {
            _interactionState.ShowPlacementPreview = true;
            _interactionState.PlacementPreviewTemplate = MainViewModel.LeftPanel.SelectedTemplate;
            var snapSettings = vm.GridSnap;

            var (snappedCenterX, snappedCenterY) = snapSettings.Snap(canvasPoint.X, canvasPoint.Y);
            _interactionState.PlacementPreviewPosition = new Point(snappedCenterX, snappedCenterY);
            InvalidateVisual();
        }
        else
        {
            if (_interactionState.ShowPlacementPreview)
            {
                _interactionState.ShowPlacementPreview = false;
                InvalidateVisual();
            }
        }
    }

    private void UpdatePinHighlighting(Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (MainViewModel?.CurrentMode == InteractionMode.Connect)
        {
            if (_interactionState.ConnectionDragStartPin != null)
            {
                vm.UpdatePinHighlight(canvasPoint.X, canvasPoint.Y, _interactionState.ConnectionDragStartPin);
            }
            else
            {
                MainViewModel.CanvasMouseMove(canvasPoint.X, canvasPoint.Y);
            }
            InvalidateVisual();
        }
    }

    private void UpdatePowerFlowHover(Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (vm.ShowPowerFlow)
        {
            var previousHover = _interactionState.HoveredConnection;
            _interactionState.HoveredConnection = DesignCanvasHitTesting.HitTestConnection(canvasPoint, vm);
            if (_interactionState.HoveredConnection != previousHover)
            {
                InvalidateVisual();
            }
        }
        else if (_interactionState.HoveredConnection != null)
        {
            _interactionState.HoveredConnection = null;
        }
    }

    private void UpdateGroupHover(Point canvasPoint, DesignCanvasViewModel vm)
    {
        // Don't update group hover when dragging or in other modes
        if (_interactionState.DraggingComponent != null ||
            _interactionState.IsPanning ||
            vm.Selection.IsBoxSelecting ||
            MainViewModel?.CurrentMode != InteractionMode.Select)
        {
            return;
        }

        var previousHover = _interactionState.HoveredGroupInstance;

        // First, check if we're hovering over any component
        var hoveredComponent = DesignCanvasHitTesting.HitTestComponent(canvasPoint, vm);

        if (hoveredComponent != null && hoveredComponent.Component.ParentGroupInstanceId != null)
        {
            // Component belongs to a group - find and highlight the group
            var groupInstance = vm.GetGroupInstance(hoveredComponent.Component.ParentGroupInstanceId.Value);
            _interactionState.HoveredGroupInstance = groupInstance;
        }
        else
        {
            // Check if we're hovering directly over a group border/label
            var hoveredGroup = HitTestGroupBorder(canvasPoint, vm);
            _interactionState.HoveredGroupInstance = hoveredGroup;
        }

        // Repaint if hover state changed
        if (_interactionState.HoveredGroupInstance != previousHover)
        {
            InvalidateVisual();
        }
    }

    private ComponentGroupInstance? HitTestGroupBorder(
        Point canvasPoint,
        DesignCanvasViewModel vm)
    {
        foreach (var groupInstance in vm.GetAllGroupInstances())
        {
            if (ComponentGroupRenderer.HitTestGroupBounds(
                groupInstance,
                canvasPoint.X,
                canvasPoint.Y,
                10.0))
            {
                return groupInstance;
            }
        }

        return null;
    }

    private void UpdateConnectionDragPreview(Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (_interactionState.ConnectionDragStartPin != null)
        {
            _interactionState.ConnectionDragCurrentPoint = canvasPoint;

            var targetPin = vm.HighlightedPin?.Pin;
            if (targetPin != null && targetPin != _interactionState.ConnectionDragStartPin &&
                targetPin.ParentComponent != _interactionState.ConnectionDragStartPin.ParentComponent)
            {
                MainViewModel!.BottomPanel.StatusText = $"Release to connect {_interactionState.ConnectionDragStartPin.Name} to {targetPin.Name}";
            }
            else
            {
                MainViewModel!.BottomPanel.StatusText = $"Drag to another pin to connect from {_interactionState.ConnectionDragStartPin.Name}...";
            }

            InvalidateVisual();
        }
    }

    private void UpdateComponentDrag(Point delta, Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (_interactionState.DraggingComponent != null && MainViewModel?.CurrentMode == InteractionMode.Select)
        {
            double deltaX = delta.X / Zoom;
            double deltaY = delta.Y / Zoom;

            bool isDraggingGroup = vm.Selection.SelectedComponents.Count > 1;

            if (isDraggingGroup)
            {
                UpdateGroupDrag(deltaX, deltaY, vm);
            }
            else
            {
                UpdateSingleComponentDrag(deltaX, deltaY, vm);
            }

            InvalidateVisual();
        }
    }

    private void UpdateGroupDrag(double deltaX, double deltaY, DesignCanvasViewModel vm)
    {
        // First, move by mouse delta
        foreach (var comp in vm.Selection.SelectedComponents)
        {
            vm.MoveComponent(comp, deltaX, deltaY);
        }

        // Then check for alignment snapping
        if (vm.AlignmentGuide.IsEnabled)
        {
            vm.AlignmentGuide.UpdateAlignments(_interactionState.DraggingComponent!, vm.Components);

            // Calculate snap delta based on current position (after mouse move)
            var (snapDX, snapDY) = vm.AlignmentGuide.CalculateSnapDelta(_interactionState.DraggingComponent!, vm.Components, Zoom);

            // Only apply snap if it would move us (non-zero delta)
            // The snap delta is calculated to bring pins into alignment
            if (snapDX != 0 || snapDY != 0)
            {
                foreach (var comp in vm.Selection.SelectedComponents)
                {
                    vm.MoveComponent(comp, snapDX, snapDY);
                }
            }
        }

        var snapSettings = vm.GridSnap;
        double centerX = _interactionState.DraggingComponent!.X + _interactionState.DraggingComponent.Width / 2.0;
        double centerY = _interactionState.DraggingComponent.Y + _interactionState.DraggingComponent.Height / 2.0;
        var (snappedCX, snappedCY) = snapSettings.Snap(centerX, centerY);
        double previewX = snappedCX - _interactionState.DraggingComponent.Width / 2.0;
        double previewY = snappedCY - _interactionState.DraggingComponent.Height / 2.0;
        _interactionState.DragPreviewPosition = new Point(previewX, previewY);

        double snapDeltaX = previewX - _interactionState.DraggingComponent.X;
        double snapDeltaY = previewY - _interactionState.DraggingComponent.Y;
        _interactionState.DragPreviewValid = vm.Selection.CanMoveGroup(vm, snapDeltaX, snapDeltaY);
        _interactionState.ShowDragPreview = snapSettings.IsEnabled || !_interactionState.DragPreviewValid;
    }

    private void UpdateSingleComponentDrag(double deltaX, double deltaY, DesignCanvasViewModel vm)
    {
        vm.MoveComponent(_interactionState.DraggingComponent!, deltaX, deltaY);

        // Update alignment guides
        if (vm.AlignmentGuide.IsEnabled)
        {
            vm.AlignmentGuide.UpdateAlignments(_interactionState.DraggingComponent!, vm.Components);

            // Apply pin alignment snapping
            var (snapDX, snapDY) = vm.AlignmentGuide.CalculateSnapDelta(_interactionState.DraggingComponent!, vm.Components, Zoom);
            if (snapDX != 0 || snapDY != 0)
            {
                vm.MoveComponent(_interactionState.DraggingComponent!, snapDX, snapDY);
            }
        }

        var snapSettings = vm.GridSnap;
        double centerX = _interactionState.DraggingComponent!.X + _interactionState.DraggingComponent.Width / 2.0;
        double centerY = _interactionState.DraggingComponent.Y + _interactionState.DraggingComponent.Height / 2.0;
        var (snappedCX, snappedCY) = snapSettings.Snap(centerX, centerY);
        double previewX = snappedCX - _interactionState.DraggingComponent.Width / 2.0;
        double previewY = snappedCY - _interactionState.DraggingComponent.Height / 2.0;
        _interactionState.DragPreviewPosition = new Point(previewX, previewY);
        _interactionState.DragPreviewValid = vm.CanMoveComponentTo(_interactionState.DraggingComponent, previewX, previewY);
        _interactionState.ShowDragPreview = snapSettings.IsEnabled || !_interactionState.DragPreviewValid;
    }

    private void UpdateBoxSelection(Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (vm.Selection.IsBoxSelecting)
        {
            vm.Selection.BoxEndX = canvasPoint.X;
            vm.Selection.BoxEndY = canvasPoint.Y;
            InvalidateVisual();
        }
    }

    private void UpdatePanning(Point delta, DesignCanvasViewModel vm)
    {
        if (_interactionState.IsPanning)
        {
            vm.PanX += delta.X;
            vm.PanY += delta.Y;
            _interactionState.HasPanned = true; // Mark that we actually panned
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // Suppress context menu if we panned (right-click drag)
        if (_interactionState.HasPanned && e.InitialPressMouseButton == MouseButton.Right)
        {
            e.Handled = true;
            _interactionState.HasPanned = false;
            _interactionState.IsPanning = false;
            return;
        }

        base.OnPointerReleased(e);

        CompleteConnectionDrag(e);
        CompleteComponentDrag(e);
        CompleteBoxSelection(e);

        _interactionState.DraggingComponent = null;
        _interactionState.IsPanning = false;
        _interactionState.HasPanned = false;
    }

    private void CompleteConnectionDrag(PointerReleasedEventArgs e)
    {
        if (_interactionState.ConnectionDragStartPin != null)
        {
            var targetPin = ViewModel?.HighlightedPin?.Pin;

            if (targetPin != null && targetPin != _interactionState.ConnectionDragStartPin &&
                targetPin.ParentComponent != _interactionState.ConnectionDragStartPin.ParentComponent)
            {
                var cmd = new Commands.CreateConnectionCommand(ViewModel!, _interactionState.ConnectionDragStartPin, targetPin);
                MainViewModel?.CommandManager.ExecuteCommand(cmd);
                MainViewModel!.BottomPanel.StatusText = $"Connected {_interactionState.ConnectionDragStartPin.Name} to {targetPin.Name}";
            }
            else
            {
                var existingConnection = ViewModel?.GetConnectionForPin(_interactionState.ConnectionDragStartPin);
                if (existingConnection != null)
                {
                    var deleteCmd = new Commands.DeleteConnectionCommand(ViewModel!, existingConnection);
                    MainViewModel?.CommandManager.ExecuteCommand(deleteCmd);
                    MainViewModel!.BottomPanel.StatusText = $"Deleted connection from {_interactionState.ConnectionDragStartPin.Name}";
                }
                else
                {
                    MainViewModel!.BottomPanel.StatusText = "Connect mode: Drag from a pin to another pin to connect";
                }
            }

            _interactionState.ConnectionDragStartPin = null;
            InvalidateVisual();
        }
    }

    private void CompleteComponentDrag(PointerReleasedEventArgs e)
    {
        if (_interactionState.DraggingComponent != null)
        {
            var vm = ViewModel;
            bool isDraggingGroup = vm?.Selection.SelectedComponents.Count > 1;

            double finalX = _interactionState.DraggingComponent.X;
            double finalY = _interactionState.DraggingComponent.Y;

            var snapSettings = vm?.GridSnap;
            if (snapSettings != null && snapSettings.IsEnabled)
            {
                double centerX = _interactionState.DraggingComponent.X + _interactionState.DraggingComponent.Width / 2.0;
                double centerY = _interactionState.DraggingComponent.Y + _interactionState.DraggingComponent.Height / 2.0;
                var (snappedCX, snappedCY) = snapSettings.Snap(centerX, centerY);
                finalX = snappedCX - _interactionState.DraggingComponent.Width / 2.0;
                finalY = snappedCY - _interactionState.DraggingComponent.Height / 2.0;
            }

            double deltaX = finalX - _interactionState.DraggingComponent.X;
            double deltaY = finalY - _interactionState.DraggingComponent.Y;

            bool canPlace;
            if (isDraggingGroup)
            {
                canPlace = vm?.Selection.CanMoveGroup(vm, deltaX, deltaY) ?? true;
            }
            else
            {
                canPlace = vm?.CanMoveComponentTo(_interactionState.DraggingComponent, finalX, finalY) ?? true;
            }

            if (canPlace)
            {
                ApplyFinalMove(deltaX, deltaY, vm, isDraggingGroup);
            }
            else
            {
                RevertDrag(vm, isDraggingGroup);
            }

            _interactionState.ShowDragPreview = false;

            // Clear alignment guides and reset snap state
            if (vm != null)
            {
                vm.AlignmentGuide.ClearAlignments();
                vm.AlignmentGuide.ResetSnapState();
            }

            // End tracking for undo
            if (isDraggingGroup)
            {
                MainViewModel?.EndGroupMove(vm!.Selection.SelectedComponents);

                // Restore selection highlighting
                foreach (var comp in vm!.Selection.SelectedComponents)
                {
                    comp.IsSelected = true;
                }
            }
            else
            {
                MainViewModel?.EndMoveComponent();
            }

            InvalidateVisual();
        }
    }

    private void ApplyFinalMove(double deltaX, double deltaY, DesignCanvasViewModel? vm, bool isDraggingGroup)
    {
        if (Math.Abs(deltaX) > 0.001 || Math.Abs(deltaY) > 0.001)
        {
            if (isDraggingGroup)
            {
                // Move all components in the group by the same delta
                foreach (var comp in vm!.Selection.SelectedComponents)
                {
                    vm.MoveComponent(comp, deltaX, deltaY);
                }
            }
            else
            {
                // Move single component
                vm?.MoveComponent(_interactionState.DraggingComponent!, deltaX, deltaY);
            }
        }
    }

    private void RevertDrag(DesignCanvasViewModel? vm, bool isDraggingGroup)
    {
        if (isDraggingGroup)
        {
            foreach (var comp in vm!.Selection.SelectedComponents)
            {
                if (_interactionState.GroupDragStartPositions.TryGetValue(comp, out var startPos))
                {
                    double revertDeltaX = startPos.x - comp.X;
                    double revertDeltaY = startPos.y - comp.Y;
                    if (Math.Abs(revertDeltaX) > 0.001 || Math.Abs(revertDeltaY) > 0.001)
                    {
                        vm.MoveComponent(comp, revertDeltaX, revertDeltaY);
                    }
                }
            }
        }
        else
        {
            double revertDeltaX = _interactionState.DragStartX - _interactionState.DraggingComponent!.X;
            double revertDeltaY = _interactionState.DragStartY - _interactionState.DraggingComponent.Y;
            if (Math.Abs(revertDeltaX) > 0.001 || Math.Abs(revertDeltaY) > 0.001)
            {
                vm?.MoveComponent(_interactionState.DraggingComponent, revertDeltaX, revertDeltaY);
            }
        }
        if (MainViewModel != null)
            MainViewModel.BottomPanel.StatusText = "Cannot place here - overlaps with another component";
    }

    private void CompleteBoxSelection(PointerReleasedEventArgs e)
    {
        if (ViewModel?.Selection.IsBoxSelecting == true)
        {
            var (minX, minY, maxX, maxY) = ViewModel.Selection.GetNormalizedBox();

            bool isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool isAltPressed = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

            ViewModel.Selection.SelectInRectangle(
                ViewModel.Components,
                minX, minY, maxX, maxY,
                addToSelection: isCtrlPressed,
                removeFromSelection: isAltPressed);

            ViewModel.Selection.IsBoxSelecting = false;
            InvalidateVisual();
        }
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

    private Point ScreenToCanvas(Point screenPoint)
    {
        var vm = ViewModel;
        if (vm == null) return screenPoint;

        return new Point(
            (screenPoint.X - vm.PanX) / Zoom,
            (screenPoint.Y - vm.PanY) / Zoom);
    }
}
