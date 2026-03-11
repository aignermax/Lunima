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
    /// Tolerance for snap-to-align on mouse release in micrometers.
    /// Component will snap to alignment if released within this distance.
    /// Figma-style: shows guides during drag, snaps gently on release.
    /// </summary>
    [ObservableProperty]
    private double _snapToleranceMicrometers = 5.0;

    /// <summary>
    /// Whether snapping to alignment guides is enabled on mouse release.
    /// When enabled, component snaps to alignment if released while guides are visible.
    /// </summary>
    [ObservableProperty]
    private bool _snapEnabled = true;

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
    /// Calculates snap delta to align pins on mouse release (Figma-style).
    /// During drag: NO snapping, just shows guide lines.
    /// On release: Snaps gently if within tolerance.
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    /// <returns>Tuple of (deltaX, deltaY) to apply for snapping, or (0, 0) if no snap.</returns>
    public (double deltaX, double deltaY) CalculateSnapOnRelease(
        ComponentViewModel draggingComponent,
        IEnumerable<ComponentViewModel> otherComponents)
    {
        if (!IsEnabled || !SnapEnabled || draggingComponent == null)
            return (0, 0);

        // Check if there are visible alignment guides (component is close to alignment)
        if (HorizontalAlignments.Count == 0 && VerticalAlignments.Count == 0)
            return (0, 0);

        // Calculate snap delta using helper with snap tolerance
        var otherCoreComponents = otherComponents
            .Where(c => c != draggingComponent)
            .Select(c => c.Component);

        return _helper.CalculateSnapDelta(
            draggingComponent.Component,
            otherCoreComponents,
            SnapToleranceMicrometers);
    }

    /// <summary>
    /// Resets state and clears alignment guides when drag ends.
    /// Call this when pointer is released.
    /// </summary>
    public void ResetSnapState()
    {
        ClearAlignments();
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
