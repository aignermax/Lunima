using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.ViewModels;

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
            mainVm.StatusText = $"Drag to another pin to connect from {pin.Name}...";
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
            HandleComponentSelection(e, canvasPoint, vm, mainVm);
        }
        else
        {
            StartBoxSelection(canvasPoint, vm);
        }
    }

    private void HandleComponentSelection(PointerPressedEventArgs e, Point canvasPoint, DesignCanvasViewModel vm, MainViewModel? mainVm)
    {
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

        UpdatePlacementPreview(canvasPoint, vm);
        UpdatePinHighlighting(canvasPoint, vm);
        UpdatePowerFlowHover(canvasPoint, vm);
        UpdateConnectionDragPreview(canvasPoint, vm);
        UpdateComponentDrag(delta, canvasPoint, vm);
        UpdateBoxSelection(canvasPoint, vm);
        UpdatePanning(delta, vm);

        _interactionState.LastPointerPosition = point;
    }

    private void UpdatePlacementPreview(Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (MainViewModel?.CurrentMode == InteractionMode.PlaceComponent &&
            MainViewModel?.SelectedTemplate != null)
        {
            _interactionState.ShowPlacementPreview = true;
            _interactionState.PlacementPreviewTemplate = MainViewModel.SelectedTemplate;
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
            _interactionState.LastCanvasPosition = canvasPoint;
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

    private void UpdateConnectionDragPreview(Point canvasPoint, DesignCanvasViewModel vm)
    {
        if (_interactionState.ConnectionDragStartPin != null)
        {
            _interactionState.ConnectionDragCurrentPoint = canvasPoint;

            var targetPin = vm.HighlightedPin?.Pin;
            if (targetPin != null && targetPin != _interactionState.ConnectionDragStartPin &&
                targetPin.ParentComponent != _interactionState.ConnectionDragStartPin.ParentComponent)
            {
                MainViewModel!.StatusText = $"Release to connect {_interactionState.ConnectionDragStartPin.Name} to {targetPin.Name}";
            }
            else
            {
                MainViewModel!.StatusText = $"Drag to another pin to connect from {_interactionState.ConnectionDragStartPin.Name}...";
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
        foreach (var comp in vm.Selection.SelectedComponents)
        {
            vm.MoveComponent(comp, deltaX, deltaY);
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
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        CompleteConnectionDrag(e);
        CompleteComponentDrag(e);
        CompleteBoxSelection(e);

        _interactionState.DraggingComponent = null;
        _interactionState.IsPanning = false;
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
                MainViewModel!.StatusText = $"Connected {_interactionState.ConnectionDragStartPin.Name} to {targetPin.Name}";
            }
            else
            {
                var existingConnection = ViewModel?.GetConnectionForPin(_interactionState.ConnectionDragStartPin);
                if (existingConnection != null)
                {
                    var deleteCmd = new Commands.DeleteConnectionCommand(ViewModel!, existingConnection);
                    MainViewModel?.CommandManager.ExecuteCommand(deleteCmd);
                    MainViewModel!.StatusText = $"Deleted connection from {_interactionState.ConnectionDragStartPin.Name}";
                }
                else
                {
                    MainViewModel!.StatusText = "Connect mode: Drag from a pin to another pin to connect";
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
            MainViewModel.StatusText = "Cannot place here - overlaps with another component";
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
