using Avalonia;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Encapsulates all state needed by canvas renderers in a single snapshot.
/// Created once per Render() call and passed to all renderers.
/// </summary>
public sealed class CanvasRenderContext
{
    /// <summary>Gets the canvas ViewModel containing design data (components, connections, etc.).</summary>
    public required DesignCanvasViewModel ViewModel { get; init; }

    /// <summary>Gets the main application ViewModel (interaction mode, commands).</summary>
    public MainViewModel? MainViewModel { get; init; }

    /// <summary>Gets the canvas interaction state (drag, hover, preview positions).</summary>
    public required CanvasInteractionState InteractionState { get; init; }

    /// <summary>Gets the current zoom level applied to world-space rendering.</summary>
    public double Zoom { get; init; }

    /// <summary>Gets the canvas control bounds in screen coordinates.</summary>
    public Rect Bounds { get; init; }

    /// <summary>
    /// Gets the GDS preview render service used to supply polygon thumbnails to
    /// <see cref="CAP.Avalonia.Controls.Rendering.ComponentRenderer"/>.
    /// <c>null</c> when the service is not available (e.g. design-time).
    /// </summary>
    public GdsPreviewRenderService? GdsPreviewRenderService { get; init; }
}
