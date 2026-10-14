using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering.DeviceCategories;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas.ViewSettings;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering.WorkspaceEmphasis;

/// <summary>
/// The Optics canvas emphasis (<see cref="CanvasEmphasis.Light"/>): component bodies and pins
/// recede so the light on the waveguides reads first, while the light's endpoints — sources,
/// grating/edge couplers, detectors — keep full strength and a light-coloured name. Selected
/// and hovered components never recede, so editing stays as clear as in Layout.
/// </summary>
public static class LightEmphasis
{
    /// <summary>Opacity of a receded component body (with its pins).</summary>
    public const double RecededOpacity = 0.45;

    /// <summary>Name colour of a light endpoint: the waveguide (light) hue.</summary>
    public static readonly IBrush EndpointNameBrush = CanvasPalette.Brush(CanvasPalette.Lighten(CanvasPalette.Waveguide, 0.25));

    /// <summary>True while the canvas emphasises light.</summary>
    public static bool IsActive(CanvasRenderContext rc) =>
        rc.ViewModel.ViewSettings.Emphasis == CanvasEmphasis.Light;

    /// <summary>True for components where light enters or leaves the circuit.</summary>
    public static bool IsLightEndpoint(Component component) =>
        SimulationService.IsLightSource(component)
        || DeviceCategoryClassifier.Classify(component) is DeviceCategory.InputOutput or DeviceCategory.Detector;

    /// <summary>
    /// Pushes the receded opacity for <paramref name="component"/> when light is emphasised and
    /// the component is neither a light endpoint nor being worked on; null otherwise.
    /// </summary>
    public static IDisposable? PushRecede(DrawingContext context, CanvasRenderContext rc, Component component,
        bool isEngaged)
    {
        if (isEngaged || !IsActive(rc) || IsLightEndpoint(component))
            return null;
        return context.PushOpacity(RecededOpacity);
    }

    /// <summary>The highlighted name brush for a light endpoint while light is emphasised; null otherwise.</summary>
    public static IBrush? EndpointNameBrushFor(CanvasRenderContext rc, Component component) =>
        IsActive(rc) && IsLightEndpoint(component) ? EndpointNameBrush : null;
}
