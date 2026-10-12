using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>
/// Scene control for the canvas-preview visibility journey: renders a
/// <see cref="DesignCanvasViewModel"/> with the production <see cref="WaveguideConnectionRenderer"/>
/// and <see cref="ComponentRenderer"/> in the world transform — the same renderers in the same
/// order <c>DesignCanvas.Render</c> calls them (the real DesignCanvas needs the full App DI stack
/// and cannot be shown headless, see <c>CanvasLabelDeclutterSceneControl</c>) — and wires the
/// real <see cref="GdsPreviewRenderService"/> into the render context so placed components draw
/// their rasterised GDS previews exactly like the shipping canvas.
/// </summary>
internal sealed class CanvasPreviewJourneySceneControl : Control
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly GdsPreviewRenderService _previewService;
    private readonly Rect _world;
    private readonly WaveguideConnectionRenderer _connectionRenderer = new();
    private readonly ComponentRenderer _componentRenderer = new();

    /// <param name="world">World region (µm) mapped to fill <see cref="Visual.Bounds"/> exactly.</param>
    public CanvasPreviewJourneySceneControl(
        DesignCanvasViewModel canvas, GdsPreviewRenderService previewService, Rect world)
    {
        _canvas = canvas;
        _previewService = previewService;
        _world = world;
    }

    /// <summary>World→screen scale of the current frame.</summary>
    public double Scale => Bounds.Width / _world.Width;

    /// <summary>Maps a world-space rect to the frame's pixel coordinates.</summary>
    public Rect WorldToScreen(Rect worldRect) => new(
        (worldRect.X - _world.X) * Scale,
        (worldRect.Y - _world.Y) * Scale,
        worldRect.Width * Scale,
        worldRect.Height * Scale);

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        double scale = Scale;
        // ComponentRenderer recovers the viewport from Pan/Zoom (-PanX/zoom, ...) exactly like
        // the real canvas — Pan must be scaled for that formula to recover _world.X/.Y.
        _canvas.PanX = -_world.X * scale;
        _canvas.PanY = -_world.Y * scale;

        context.FillRectangle(CanvasPalette.ChipFillBrush, new Rect(Bounds.Size));

        var renderContext = new CanvasRenderContext
        {
            ViewModel = _canvas,
            InteractionState = new CanvasInteractionState(),
            Zoom = scale,
            Bounds = new Rect(Bounds.Size),
            GdsPreviewRenderService = _previewService,
        };

        using var _ = context.PushTransform(
            Matrix.CreateTranslation(-_world.X, -_world.Y) * Matrix.CreateScale(scale, scale));

        // Same draw order as DesignCanvas.Render: waveguides under components, labels on top.
        _connectionRenderer.Render(context, renderContext);
        _componentRenderer.Render(context, renderContext);
        renderContext.Labels.Flush(context, scale);
    }
}
