using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Helpers;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// ViewModel for pin alignment guide visualization.
/// Manages settings and provides access to current alignments during component drag.
/// </summary>
public partial class AlignmentGuideViewModel : ObservableObject
{
    private readonly PinAlignmentHelper _helper;

    /// <summary>
    /// Whether alignment guides are enabled.
    /// When false, no guides will be shown even if alignments exist.
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// Tolerance for alignment detection in micrometers.
    /// Two pins are considered aligned if their X or Y coordinates differ by less than this value.
    /// </summary>
    [ObservableProperty]
    private double _alignmentToleranceMicrometers = 2.0;

    /// <summary>
    /// Tolerance for snap-to-align in micrometers.
    /// Component will snap to alignment if within this distance.
    /// Increased to ~20µm for better usability at typical zoom levels.
    /// </summary>
    [ObservableProperty]
    private double _snapToleranceMicrometers = 20.0;

    /// <summary>
    /// Distance in screen pixels that mouse must move away from snap position
    /// before snap is released. This creates a "sticky" feel without locking the component.
    /// Set to 10 pixels for comfortable mouse movement tolerance.
    /// </summary>
    [ObservableProperty]
    private double _snapBreakDistancePixels = 10.0;

    /// <summary>
    /// Whether snapping to alignment guides is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _snapEnabled = true;

    /// <summary>
    /// Tracks if the component is currently snapped to prevent immediate re-snap.
    /// </summary>
    private bool _isCurrentlySnapped = false;

    /// <summary>
    /// When snapped, this stores which axis is locked (true = X is snapped, false = Y is snapped).
    /// Null if not snapped or both axes snapped.
    /// </summary>
    private bool? _snappedAxisIsX = null;

    /// <summary>
    /// The snapped coordinate value (either X or Y depending on _snappedAxisIsX).
    /// </summary>
    private double? _snappedCoordinate = null;

    /// <summary>
    /// Mouse position in canvas coordinates when snap first engaged.
    /// Used to measure snap break distance on the free axis.
    /// </summary>
    private (double x, double y)? _snapStartMousePosition = null;

    /// <summary>
    /// Initial offset between component and mouse at drag start.
    /// Preserved throughout drag to maintain visual relationship.
    /// </summary>
    private (double x, double y)? _initialDragOffset = null;

    /// <summary>
    /// Current horizontal alignments (updated during drag).
    /// </summary>
    [ObservableProperty]
    private List<HorizontalAlignment> _horizontalAlignments = new();

    /// <summary>
    /// Current vertical alignments (updated during drag).
    /// </summary>
    [ObservableProperty]
    private List<VerticalAlignment> _verticalAlignments = new();

    /// <summary>
    /// Whether any alignments are currently detected.
    /// </summary>
    public bool HasAlignments => HorizontalAlignments.Count > 0 || VerticalAlignments.Count > 0;

    public AlignmentGuideViewModel()
    {
        _helper = new PinAlignmentHelper
        {
            AlignmentToleranceMicrometers = AlignmentToleranceMicrometers
        };
    }

    partial void OnAlignmentToleranceMicrometersChanged(double value)
    {
        _helper.AlignmentToleranceMicrometers = value;
    }

    /// <summary>
    /// Updates alignments for the currently dragging component.
    /// Call this during pointer move events when dragging a component.
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    public void UpdateAlignments(ComponentViewModel draggingComponent, IEnumerable<ComponentViewModel> otherComponents)
    {
        if (!IsEnabled || draggingComponent == null)
        {
            ClearAlignments();
            return;
        }

        var otherCoreComponents = otherComponents
            .Where(c => c != draggingComponent)
            .Select(c => c.Component);

        var (horizontal, vertical) = _helper.FindAllAlignments(
            draggingComponent.Component,
            otherCoreComponents);

        HorizontalAlignments = horizontal;
        VerticalAlignments = vertical;
        OnPropertyChanged(nameof(HasAlignments));
    }

    /// <summary>
    /// Clears all current alignments.
    /// Call this when drag ends or is cancelled.
    /// </summary>
    public void ClearAlignments()
    {
        HorizontalAlignments = new List<HorizontalAlignment>();
        VerticalAlignments = new List<VerticalAlignment>();
        OnPropertyChanged(nameof(HasAlignments));
    }

    /// <summary>
    /// Calculates the target component position with snap-to-align behavior.
    /// Maintains mouse-relative positioning and proper snap break mechanism.
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    /// <param name="currentMouseCanvasX">Current mouse X position in canvas coordinates.</param>
    /// <param name="currentMouseCanvasY">Current mouse Y position in canvas coordinates.</param>
    /// <param name="initialOffsetX">Initial X offset (component X - mouse X at drag start).</param>
    /// <param name="initialOffsetY">Initial Y offset (component Y - mouse Y at drag start).</param>
    /// <param name="zoom">Current zoom level (for screen pixel distance calculation).</param>
    /// <returns>Target (X, Y) position for the component.</returns>
    public (double targetX, double targetY) CalculateComponentPosition(
        ComponentViewModel draggingComponent,
        IEnumerable<ComponentViewModel> otherComponents,
        double currentMouseCanvasX,
        double currentMouseCanvasY,
        double initialOffsetX,
        double initialOffsetY,
        double zoom)
    {
        if (!IsEnabled || !SnapEnabled || draggingComponent == null)
        {
            // No snapping - return mouse-relative position
            return (currentMouseCanvasX + initialOffsetX, currentMouseCanvasY + initialOffsetY);
        }

        // Store initial drag offset for snap break restoration
        if (!_initialDragOffset.HasValue)
        {
            _initialDragOffset = (initialOffsetX, initialOffsetY);
        }

        // Check if we're currently snapped - maintain snapped axis but check for break
        if (_isCurrentlySnapped && _snappedAxisIsX.HasValue && _snappedCoordinate.HasValue && _snapStartMousePosition.HasValue)
        {
            // Measure snap break distance in screen pixels on the free axis (zoom-independent)
            double mouseScreenDeltaOnFreeAxis;
            if (_snappedAxisIsX.Value)
            {
                // X is snapped, Y is free - check Y distance in screen pixels
                mouseScreenDeltaOnFreeAxis = Math.Abs(currentMouseCanvasY - _snapStartMousePosition.Value.y) * zoom;
            }
            else
            {
                // Y is snapped, X is free - check X distance in screen pixels
                mouseScreenDeltaOnFreeAxis = Math.Abs(currentMouseCanvasX - _snapStartMousePosition.Value.x) * zoom;
            }

            // If moved too far on the free axis, break snap and restore to mouse-relative position
            if (mouseScreenDeltaOnFreeAxis > SnapBreakDistancePixels)
            {
                // Break snap
                _isCurrentlySnapped = false;
                _snappedAxisIsX = null;
                _snappedCoordinate = null;
                _snapStartMousePosition = null;

                // Return to mouse-relative position using preserved initial offset
                return (currentMouseCanvasX + _initialDragOffset.Value.x,
                        currentMouseCanvasY + _initialDragOffset.Value.y);
            }

            // Still within tolerance - maintain snap on locked axis, follow mouse on free axis
            if (_snappedAxisIsX.Value)
            {
                // X is snapped - lock X, let Y follow mouse
                return (_snappedCoordinate.Value, currentMouseCanvasY + _initialDragOffset.Value.y);
            }
            else
            {
                // Y is snapped - lock Y, let X follow mouse
                return (currentMouseCanvasX + _initialDragOffset.Value.x, _snappedCoordinate.Value);
            }
        }

        // Not currently snapped - calculate normal mouse-relative position
        double targetX = currentMouseCanvasX + initialOffsetX;
        double targetY = currentMouseCanvasY + initialOffsetY;

        // Temporarily set component to target position to check for snap opportunities
        double originalX = draggingComponent.X;
        double originalY = draggingComponent.Y;
        draggingComponent.X = targetX;
        draggingComponent.Y = targetY;

        // Check for new snap opportunities
        var otherCoreComponents = otherComponents
            .Where(c => c != draggingComponent)
            .Select(c => c.Component);

        var (snapDX, snapDY) = _helper.CalculateSnapDelta(
            draggingComponent.Component,
            otherCoreComponents,
            SnapToleranceMicrometers);

        // Restore original position
        draggingComponent.X = originalX;
        draggingComponent.Y = originalY;

        // If we found a snap, engage it
        if (snapDX != 0 || snapDY != 0)
        {
            _isCurrentlySnapped = true;
            _snapStartMousePosition = (currentMouseCanvasX, currentMouseCanvasY);

            // Apply snap delta to target position
            double snappedX = targetX + snapDX;
            double snappedY = targetY + snapDY;

            // Determine which axis is being snapped (use the larger delta)
            if (Math.Abs(snapDX) > Math.Abs(snapDY))
            {
                // X snap is dominant - lock X coordinate
                _snappedAxisIsX = true;
                _snappedCoordinate = snappedX;
                // Return: snapped X, mouse-relative Y
                return (_snappedCoordinate.Value, targetY);
            }
            else
            {
                // Y snap is dominant (or equal) - lock Y coordinate
                _snappedAxisIsX = false;
                _snappedCoordinate = snappedY;
                // Return: mouse-relative X, snapped Y
                return (targetX, _snappedCoordinate.Value);
            }
        }

        // No snap - return normal mouse-relative position
        return (targetX, targetY);
    }

    /// <summary>
    /// Legacy method for backward compatibility. Use CalculateComponentPosition instead.
    /// </summary>
    [Obsolete("Use CalculateComponentPosition instead")]
    public (double deltaX, double deltaY) CalculateSnapDelta(
        ComponentViewModel draggingComponent,
        IEnumerable<ComponentViewModel> otherComponents,
        double zoom)
    {
        // This method is kept for backward compatibility but should not be used
        // Return no snap
        return (0, 0);
    }

    /// <summary>
    /// Resets snap state when drag ends.
    /// Call this when pointer is released.
    /// </summary>
    public void ResetSnapState()
    {
        _isCurrentlySnapped = false;
        _snappedAxisIsX = null;
        _snappedCoordinate = null;
        _snapStartMousePosition = null;
        _initialDragOffset = null;
    }

    /// <summary>
    /// Toggles alignment guides on/off.
    /// </summary>
    public void Toggle()
    {
        IsEnabled = !IsEnabled;
        if (!IsEnabled)
        {
            ClearAlignments();
        }
    }
}
