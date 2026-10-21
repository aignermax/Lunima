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
    internal long IssuedSourceImageCount => _sourceRaster.IssuedImageCount;

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        if (rc.ViewModel.CanvasFrozenPaths.Count == 0)
            return;

        var viewport = RenderCulling.ComputeViewportWorld(rc.ViewModel.PanX, rc.ViewModel.PanY, rc.Bounds, rc.Zoom);
        var cullRect = RenderCulling.InflateForCulling(viewport, rc.Zoom);

        var snapshot = _sourceRaster.Prepare(rc.ViewModel.CanvasFrozenPaths);
        bool drewSources = _sourceRaster.TryDraw(context, snapshot, rc.Zoom, rc.LayerVisibility);
        for (int i = 0; i < rc.ViewModel.CanvasFrozenPaths.Count; i++)
        {
            var pathVm = rc.ViewModel.CanvasFrozenPaths[i];
            if (drewSources && !pathVm.IsSelected) continue;
            var asDrawn = snapshot.Sources[i];
            if (RenderCulling.GetFrozenPathBounds(pathVm.Path, asDrawn) is { } bounds && !cullRect.Intersects(bounds))
                continue;

            if (!drewSources && asDrawn is not null)
            {
                // Drawn polygons replace the traced line; selection still highlights the path.
                AsDrawnGeometryRenderer.Draw(context, asDrawn, rc.Zoom, rc.LayerVisibility, visibleWorld: cullRect);
                if (!pathVm.IsSelected)
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
