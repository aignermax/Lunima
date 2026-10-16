namespace CAP.Avalonia.Controls;

public partial class DesignCanvas
{
    /// <summary>
    /// Repaints when the interaction mode changes so mode-dependent overlays (the
    /// analysis-output candidate glow #754, the HUD mode indicator) update immediately
    /// after a mode button click instead of on the next pointer movement.
    /// </summary>
    private void OnInteractionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Panels.CanvasInteractionViewModel.CurrentMode))
        {
            _gateConnectionGesture?.Cancel();
        }
        if (e.PropertyName is nameof(ViewModels.Panels.CanvasInteractionViewModel.CurrentMode)
            or nameof(ViewModels.Panels.CanvasInteractionViewModel.SelectedTemplate)
            or nameof(ViewModels.Panels.CanvasInteractionViewModel.SelectedGroupTemplate))
        {
            // A toolbar/key mode change has no subsequent canvas pointer event. Drop
            // the old ghost now; movement rearms the current template at the cursor.
            _interactionState.ResetPlacementPreview();
            _interactionState.ResetGroupTemplatePlacementPreview();
            InvalidateVisual();
        }
    }
}
