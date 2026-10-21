using Avalonia.Media;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders the pin-less frozen waveguide paths that live directly on the canvas
/// (<see cref="ViewModels.Canvas.DesignCanvasViewModel.CanvasFrozenPaths"/>, issue #856).
/// Reuses the group frozen-path drawing (layer-tagged palette pens, per-segment
/// culling); a selected path draws with the same accent highlight as a selected
/// waveguide connection. Implements <see cref="ICanvasRenderer"/> for world-space rendering.
/// </summary>
public sealed class CanvasFrozenPathRenderer : ICanvasRenderer
{
    private readonly GroupFrozenPathRasterCache _sourceRaster = new();
    private readonly List<CAP_Core.Components.Connections.AsDrawnGeometry?> _selectedSources = new();
    internal long IssuedSourceImageCount => _sourceRaster.IssuedImageCount;

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        if (rc.ViewModel.CanvasFrozenPaths.Count == 0)
        {
            _selectedSources.Clear();
            _sourceRaster.Prepare(rc.ViewModel.CanvasFrozenPaths);
            _sourceRaster.Prepare(_selectedSources);
            return;
        }

        var viewport = RenderCulling.ComputeViewportWorld(rc.ViewModel.PanX, rc.ViewModel.PanY, rc.Bounds, rc.Zoom);
        var cullRect = RenderCulling.InflateForCulling(viewport, rc.Zoom);

        var snapshot = _sourceRaster.Prepare(rc.ViewModel.CanvasFrozenPaths);
        bool drewSources = _sourceRaster.TryDraw(context, snapshot, rc.Zoom, rc.LayerVisibility);
        _selectedSources.Clear();
        for (int i = 0; i < snapshot.Sources.Length; i++)
            if (rc.ViewModel.CanvasFrozenPaths[i].IsSelected) _selectedSources.Add(snapshot.Sources[i]);
        int selectedCount = _selectedSources.Count;
        bool allSelected = selectedCount == snapshot.Sources.Length;
        var selectedSnapshot = allSelected ? snapshot : _sourceRaster.Prepare(_selectedSources);
        if (allSelected)
        {
            _selectedSources.Clear();
            _sourceRaster.Prepare(_selectedSources);
        }
        bool drewSelection = drewSources && selectedCount > 0
            && _sourceRaster.TryDraw(context, selectedSnapshot, rc.Zoom, rc.LayerVisibility, rc.Pens.WaveguideSelected.Brush);
        for (int i = 0; i < rc.ViewModel.CanvasFrozenPaths.Count; i++)
        {
            var pathVm = rc.ViewModel.CanvasFrozenPaths[i];
            if (drewSources && (!pathVm.IsSelected || drewSelection)) continue;
            var asDrawn = snapshot.Sources[i];
            if (RenderCulling.GetFrozenPathBounds(pathVm.Path, asDrawn) is { } bounds && !cullRect.Intersects(bounds))
                continue;

            if (asDrawn is not null)
            {
                AsDrawnGeometryRenderer.Draw(context, asDrawn, rc.Zoom, rc.LayerVisibility, visibleWorld: cullRect,
                    highlightBrush: pathVm.IsSelected ? rc.Pens.WaveguideSelected.Brush : null);
                continue;
            }

            // Canvas-level paths never carry simulated power (pin-less ⇒ not simulated),
            // so no power-flow result is passed; the layer-visibility filter still applies.
            ComponentGroupRenderer.RenderFrozenWaveguidePath(
                context, pathVm.Path, powerFlowResult: null, cullRect: cullRect,
                layerVisibility: rc.LayerVisibility,
                penOverride: pathVm.IsSelected ? rc.Pens.WaveguideSelected : null, zoom: rc.Zoom);
        }
    }
}
