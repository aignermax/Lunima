using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace UnitTests.UI;

/// <summary>
/// Scene control for the issue #1158 walkthrough: renders a <see cref="DesignCanvasViewModel"/>
/// through the production <see cref="WaveguideConnectionRenderer"/> and
/// <see cref="ComponentRenderer"/> exactly like <see cref="CanvasLabelDeclutterSceneControl"/>,
/// plus two knobs that scene lacks: an optional <see cref="MainViewModel"/> (so Connect mode
/// reaches <see cref="PinRenderer"/>) and an optional world-space overlay draw used to
/// reproduce the pre-fix rendering (full-size pin markers / in-body name label) for the
/// "before" frames.
/// </summary>
internal sealed class Issue1158SceneControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));

    private readonly DesignCanvasViewModel _canvas;
    private readonly Rect _world;
    private readonly WaveguideConnectionRenderer _connectionRenderer = new();
    private readonly ComponentRenderer _componentRenderer = new();

    /// <summary>Main ViewModel carrying the interaction mode; null renders in Select mode.</summary>
    public MainViewModel? MainViewModel { get; init; }

    /// <summary>Skips the deferred name-label flush — used by the "before" label frame, which
    /// draws the legacy in-body label itself instead.</summary>
    public bool SuppressNameLabels { get; init; }

    /// <summary>Extra world-space drawing on top of the scene, receiving (context, zoom).</summary>
    public Action<DrawingContext, double>? OverlayWorldDraw { get; init; }

    /// <param name="world">World (µm) region mapped to fill <see cref="Visual.Bounds"/>.</param>
    public Issue1158SceneControl(DesignCanvasViewModel canvas, Rect world)
    {
        _canvas = canvas;
        _world = world;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        double scale = Bounds.Width / _world.Width;
        // ComponentRenderer recovers the viewport from Pan/Zoom (-PanX/zoom, ...) exactly like
        // the real canvas — Pan must be scaled for that formula to recover _world.X/.Y.
        _canvas.PanX = -_world.X * scale;
        _canvas.PanY = -_world.Y * scale;

        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        var rc = new CanvasRenderContext
        {
            ViewModel = _canvas,
            MainViewModel = MainViewModel,
            InteractionState = new CAP.Avalonia.Controls.CanvasInteractionState(),
            Zoom = scale,
            Bounds = new Rect(Bounds.Size),
        };

        using var _ = context.PushTransform(
            Matrix.CreateTranslation(-_world.X, -_world.Y) * Matrix.CreateScale(scale, scale));

        // Same draw order as DesignCanvas.Render: waveguides under components, then the
        // deferred label flush on top of all geometry.
        _connectionRenderer.Render(context, rc);
        _componentRenderer.Render(context, rc);
        if (!SuppressNameLabels)
            rc.Labels.Flush(context, scale);
        OverlayWorldDraw?.Invoke(context, scale);
    }
}
