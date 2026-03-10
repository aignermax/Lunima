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
