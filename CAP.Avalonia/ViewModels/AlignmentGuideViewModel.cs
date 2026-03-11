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
    private double _alignmentToleranceMicrometers = 1.0;

    /// <summary>
    /// Tolerance for snap-to-align in micrometers.
    /// Component will snap to alignment if within this distance.
    /// </summary>
    [ObservableProperty]
    private double _snapToleranceMicrometers = 4.0;

    /// <summary>
    /// Distance in micrometers that component must move away from snap position
    /// before snap is released. This creates a "sticky" feel without locking the component.
    /// </summary>
    [ObservableProperty]
    private double _snapBreakDistanceMicrometers = 2.0;

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
    /// The position when snap first occurred, used to track accumulated movement.
    /// </summary>
    private (double x, double y)? _snapOriginPosition = null;

    /// <summary>
    /// Total accumulated distance moved while snapped (resets when snap breaks).
    /// </summary>
    private double _accumulatedSnapMovement = 0;

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
    /// <returns>Tuple of (deltaX, deltaY) to apply for snapping, or (0, 0) if no snap.</returns>
    public (double deltaX, double deltaY) CalculateSnapDelta(
        ComponentViewModel draggingComponent,
        IEnumerable<ComponentViewModel> otherComponents)
    {
        if (!IsEnabled || !SnapEnabled || draggingComponent == null)
            return (0, 0);

        var currentX = draggingComponent.X;
        var currentY = draggingComponent.Y;

        // Check if we're currently snapped and need to track movement
        if (_isCurrentlySnapped && _snapOriginPosition.HasValue)
        {
            // Calculate total distance moved from where we first snapped
            var totalMovement = Math.Sqrt(
                Math.Pow(currentX - _snapOriginPosition.Value.x, 2) +
                Math.Pow(currentY - _snapOriginPosition.Value.y, 2));

            // If total accumulated movement exceeds break distance, release snap
            if (totalMovement > SnapBreakDistanceMicrometers)
            {
                _isCurrentlySnapped = false;
                _snapOriginPosition = null;
                _accumulatedSnapMovement = 0;
                // Don't snap to anything else this frame - let user move freely
                return (0, 0);
            }
            else
            {
                // Still within break distance - keep snapping to original target
                // Don't apply any delta, component should stay where it is
                return (0, 0);
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

        // If we found a new snap, record the origin position
        if (snapDX != 0 || snapDY != 0)
        {
            _isCurrentlySnapped = true;
            _snapOriginPosition = (currentX, currentY); // Save where we were when snap started
            _accumulatedSnapMovement = 0;
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
        _snapOriginPosition = null;
        _accumulatedSnapMovement = 0;
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
