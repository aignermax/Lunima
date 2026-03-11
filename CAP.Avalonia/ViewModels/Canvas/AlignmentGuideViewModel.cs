using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Helpers;

namespace CAP.Avalonia.ViewModels.Canvas;

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
    /// The original position before snapping occurred (used to restore if snap breaks).
    /// </summary>
    private (double x, double y)? _preSnapPosition = null;

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
    /// Calculates snap delta to align pins if snapping is enabled.
    /// Returns the offset to apply to achieve perfect alignment.
    /// Uses "sticky but breakable" logic - tracks accumulated movement and releases
    /// snap when total movement exceeds SnapBreakDistance.
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    /// <param name="zoom">Current zoom level of the canvas (to convert pixels to micrometers).</param>
    /// <returns>Tuple of (deltaX, deltaY) to apply for snapping, or (0, 0) if no snap.</returns>
    public (double deltaX, double deltaY) CalculateSnapDelta(
        ComponentViewModel draggingComponent,
        IEnumerable<ComponentViewModel> otherComponents,
        double zoom)
    {
        if (!IsEnabled || !SnapEnabled || draggingComponent == null)
            return (0, 0);

        var currentX = draggingComponent.X;
        var currentY = draggingComponent.Y;

        // Check if we're currently snapped - maintain snapped axis but check for break
        if (_isCurrentlySnapped && _snappedAxisIsX.HasValue && _snappedCoordinate.HasValue && _preSnapPosition.HasValue)
        {
            // Convert pixel-based snap break distance to micrometers based on current zoom
            double snapBreakDistanceMicrometers = SnapBreakDistancePixels / zoom;

            // Check if mouse has moved too far away from the pre-snap position on the free axis
            double distanceOnFreeAxis;
            if (_snappedAxisIsX.Value)
            {
                // X is snapped, Y is free - check Y distance from where we started the snap
                distanceOnFreeAxis = Math.Abs(currentY - _preSnapPosition.Value.y);
            }
            else
            {
                // Y is snapped, X is free - check X distance from where we started the snap
                distanceOnFreeAxis = Math.Abs(currentX - _preSnapPosition.Value.x);
            }

            // If moved too far on the free axis, break snap and restore original position
            if (distanceOnFreeAxis > snapBreakDistanceMicrometers)
            {
                _isCurrentlySnapped = false;
                var restoreDeltaX = _preSnapPosition.Value.x - currentX;
                var restoreDeltaY = _preSnapPosition.Value.y - currentY;

                // Clear snap state
                _snappedAxisIsX = null;
                _snappedCoordinate = null;
                _preSnapPosition = null;

                // Return to original pre-snap position
                return (restoreDeltaX, restoreDeltaY);
            }

            // Still within tolerance - maintain snap
            if (_snappedAxisIsX.Value)
            {
                // X is snapped - lock X, let Y follow mouse
                double deltaX = _snappedCoordinate.Value - currentX;
                return (deltaX, 0);
            }
            else
            {
                // Y is snapped - lock Y, let X follow mouse
                double deltaY = _snappedCoordinate.Value - currentY;
                return (0, deltaY);
            }
        }

        // Not currently snapped - check for new snap opportunities
        var otherCoreComponents = otherComponents
            .Where(c => c != draggingComponent)
            .Select(c => c.Component);

        var (snapDX, snapDY) = _helper.CalculateSnapDelta(
            draggingComponent.Component,
            otherCoreComponents,
            SnapToleranceMicrometers);

        // If we found a snap, record which axis is snapped and save original position
        if (snapDX != 0 || snapDY != 0)
        {
            _isCurrentlySnapped = true;
            _preSnapPosition = (currentX, currentY); // Save position before snapping

            // Determine which axis is being snapped
            if (Math.Abs(snapDX) > Math.Abs(snapDY))
            {
                // X snap is dominant
                _snappedAxisIsX = true;
                _snappedCoordinate = currentX + snapDX;
            }
            else
            {
                // Y snap is dominant (or equal)
                _snappedAxisIsX = false;
                _snappedCoordinate = currentY + snapDY;
            }
        }

        return (snapDX, snapDY);
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
        _preSnapPosition = null;
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
