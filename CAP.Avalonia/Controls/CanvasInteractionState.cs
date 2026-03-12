using Avalonia;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Manages interaction state for the DesignCanvas (drag operations, previews, hover state).
/// Consolidates all drag-related fields and preview state in one place.
/// </summary>
public class CanvasInteractionState
{
    // Component drag state
    public ComponentViewModel? DraggingComponent { get; set; }
    public double DragStartX { get; set; }
    public double DragStartY { get; set; }
    public bool ShowDragPreview { get; set; }
    public Point DragPreviewPosition { get; set; }
    public bool DragPreviewValid { get; set; }

    // Group drag state
    public bool IsGroupDragging { get; set; }
    public double GroupDragStartCanvasX { get; set; }
    public double GroupDragStartCanvasY { get; set; }
    public Dictionary<ComponentViewModel, (double x, double y)> GroupDragStartPositions { get; } = new();

    // Connection drag state
    public PhysicalPin? ConnectionDragStartPin { get; set; }
    public Point ConnectionDragCurrentPoint { get; set; }

    // Component placement preview state
    public bool ShowPlacementPreview { get; set; }
    public ComponentTemplate? PlacementPreviewTemplate { get; set; }
    public Point PlacementPreviewPosition { get; set; }

    // Panning state
    public bool IsPanning { get; set; }
    public bool HasPanned { get; set; }
    public Point LastPointerPosition { get; set; }

    // Power flow hover state
    public WaveguideConnectionViewModel? HoveredConnection { get; set; }
    public Point LastCanvasPosition { get; set; }

    // Double-click detection state
    public DateTime LastClickTime { get; set; }
    public Point LastClickPosition { get; set; }
    public ComponentViewModel? LastClickedComponent { get; set; }
    public const double DoubleClickTimeMs = 500;
    public const double DoubleClickDistanceThreshold = 5.0;

    /// <summary>
    /// Resets all drag-related state.
    /// </summary>
    public void ResetDragState()
    {
        DraggingComponent = null;
        ShowDragPreview = false;
        IsGroupDragging = false;
        GroupDragStartPositions.Clear();
    }

    /// <summary>
    /// Resets connection drag state.
    /// </summary>
    public void ResetConnectionDrag()
    {
        ConnectionDragStartPin = null;
    }

    /// <summary>
    /// Resets placement preview state.
    /// </summary>
    public void ResetPlacementPreview()
    {
        ShowPlacementPreview = false;
        PlacementPreviewTemplate = null;
    }
}
