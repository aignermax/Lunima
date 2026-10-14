using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Die-surface half of <see cref="ComponentRenderer"/>: at the root of a die assembly each die
/// is drawn as its frame with the parts on it as individual, selectable parts (pins, names,
/// selection and hover like any top-level part). In the System workspace the content is
/// locked and drawn dimmed, so the dies themselves read as the units of work.
/// </summary>
public sealed partial class ComponentRenderer
{
    /// <summary>Opacity of die content while it is locked (System workspace).</summary>
    internal const double LockedDieContentOpacity = 0.35;

    private void DrawDieSurface(DrawingContext context, ComponentGroup die, bool isSelected, bool isHovered,
        CanvasRenderContext rc, IReadOnlySet<Guid> visibleNameIds, Rect cullRect)
    {
        var surface = rc.ViewModel.Surface;
        using (surface.IsContentLocked ? context.PushOpacity(LockedDieContentOpacity) : (IDisposable?)null)
        {
            foreach (var part in die.ChildComponents)
                DrawSurfacePart(context, surface.ViewModelFor(part), rc, visibleNameIds, cullRect, surface.IsContentLocked);
            DrawPinlessDiePaths(context, die, rc, cullRect);
        }

        var bounds = ComponentGroupRenderer.CalculateGroupBounds(die);
        if (cullRect.Intersects(bounds))
            DieFrameRenderer.Render(context, die, isSelected, isHovered, rc.Zoom);
    }

    private void DrawSurfacePart(DrawingContext context, ComponentViewModel part, CanvasRenderContext rc,
        IReadOnlySet<Guid> visibleNameIds, Rect cullRect, bool isLocked)
    {
        if (part.Component is ComponentGroup nested)
        {
            DrawComponentGroup(context, nested, part.IsSelected, rc, visibleNameIds, cullRect, isLocked);
            return;
        }

        var rect = new Rect(part.X, part.Y, part.Width, part.Height);
        if (!cullRect.Intersects(rect))
            return;
        bool isHovered = !isLocked && ReferenceEquals(rc.InteractionState.HoveredComponent, part);
        DrawComponentBody(context, part, rc, rect, isHovered);
        if (!isLocked)
            DrawComponentDecorations(context, part, rc, rect, visibleNameIds, isHovered, isDimmed: false);
    }

    /// <summary>Pin-less frozen geometry stored on the die itself (GDS-imported route outlines).</summary>
    private static void DrawPinlessDiePaths(DrawingContext context, ComponentGroup die, CanvasRenderContext rc, Rect cullRect)
    {
        var powerFlowResult = rc.ViewModel.ShowPowerFlow ? rc.ViewModel.PowerFlowVisualizer.CurrentResult : null;
        foreach (var frozenPath in die.InternalPaths)
        {
            if (RenderCulling.GetFrozenPathBounds(frozenPath) is { } pathBounds && !cullRect.Intersects(pathBounds))
                continue;
            ComponentGroupRenderer.RenderFrozenWaveguidePath(context, frozenPath, powerFlowResult,
                rc.ViewModel.PowerFlowVisualizer.FadeThresholdDb, cullRect, rc.LayerVisibility, zoom: rc.Zoom);
        }
    }
}
