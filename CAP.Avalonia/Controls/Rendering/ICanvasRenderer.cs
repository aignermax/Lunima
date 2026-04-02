using Avalonia.Media;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Interface for canvas renderers that draw world-space or screen-space content
/// onto the design canvas.
/// </summary>
public interface ICanvasRenderer
{
    /// <summary>
    /// Renders content using the provided drawing context and render context.
    /// </summary>
    /// <param name="context">The Avalonia drawing context.</param>
    /// <param name="renderContext">Snapshot of canvas state needed for rendering.</param>
    void Render(DrawingContext context, CanvasRenderContext renderContext);
}
