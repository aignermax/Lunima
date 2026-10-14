using Avalonia;
using Avalonia.Media;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Controls.Rendering.LabelDeclutter;
using CAP.Avalonia.Controls.Rendering.WorkspaceEmphasis;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Renders all components (simple and group) onto the design canvas.
/// Handles dimming in group-edit mode, lock icons, and group hierarchy display.
/// Items outside the viewport are culled and tiny on-screen items are drawn
/// body-only (see <see cref="RenderCulling"/>) so huge imported designs stay
/// responsive. Implements <see cref="ICanvasRenderer"/> for world-space rendering.
/// </summary>
public sealed class ComponentRenderer : ICanvasRenderer
{
    private readonly PinRenderer _pinRenderer = new();
    private readonly ComponentOutlineRenderer _outlineRenderer = new();
    private readonly ComponentNameLabelComputer _nameLabels = new();

    /// <summary>A name is drawn only while its owner is at least this fraction of the label's
    /// width on screen — farther out the name would dwarf the block it labels (level of detail).
    /// Selected and hovered names are exempt.</summary>
    internal const double NameLodMinOwnerFraction = 0.35;

    /// <summary>Below this zoom a group reads as one block named by its (enlarged) group tag;
    /// the names of its children are left out.</summary>
    internal const double GroupOverviewZoom = 0.25;

    private static readonly IBrush LockIconBackdrop = CanvasPalette.SurfaceBrush;
    private static readonly IBrush DimmedChildNameBrush = CanvasPalette.Brush(CanvasPalette.WithAlpha(CanvasPalette.Gray300, 0x80));

    /// <inheritdoc/>
    public void Render(DrawingContext context, CanvasRenderContext rc)
    {
        var viewport = RenderCulling.ComputeViewportWorld(rc.ViewModel.PanX, rc.ViewModel.PanY, rc.Bounds, rc.Zoom);
        var cullRect = RenderCulling.InflateForCulling(viewport, rc.Zoom);
        Guid? hoveredComponentId = rc.InteractionState.HoveredComponent?.Component.Id;
        var visibleNameIds = _nameLabels.GetVisibleLabelIds(
            rc.ViewModel.Components, hoveredComponentId, viewport, rc.Zoom);

        foreach (var comp in rc.ViewModel.Components)
            DrawComponent(context, comp, rc, visibleNameIds, cullRect);
    }

    private void DrawComponent(DrawingContext context, ComponentViewModel comp, CanvasRenderContext rc,
        IReadOnlySet<Guid> visibleNameIds, Rect cullRect)
    {
        bool isDimmed = IsComponentDimmedInEditMode(comp, rc.ViewModel);

        if (comp.Component is ComponentGroup group)
        {
            using (PushDim(context, isDimmed))
                DrawComponentGroup(context, group, comp.IsSelected, rc, visibleNameIds, cullRect, isDimmed);
            return;
        }

        var rect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);
        if (!cullRect.Intersects(rect))
            return;

        bool isHovered = ReferenceEquals(rc.InteractionState.HoveredComponent, comp);
        using (PushDim(context, isDimmed))
        using (LightEmphasis.PushRecede(context, rc, comp.Component, comp.IsSelected || isHovered))
        {
            DrawComponentBody(context, comp, rc, rect, isHovered);
            DrawComponentDecorations(context, comp, rc, rect, visibleNameIds, isHovered, isDimmed);
        }
    }

    private void DrawComponentDecorations(DrawingContext context, ComponentViewModel comp, CanvasRenderContext rc,
        Rect rect, IReadOnlySet<Guid> visibleNameIds, bool isHovered, bool isDimmed)
    {
        // Below the LOD threshold, pins, labels and icons are sub-pixel noise —
        // the body drawn above is all that remains visible at that scale.
        if (RenderCulling.IsBelowLodThreshold(comp.Width, comp.Height, rc.Zoom))
            return;

        if (!RenderCulling.IsBelowPinLodThreshold(comp.Width, comp.Height, rc.Zoom))
            _pinRenderer.DrawComponentPins(context, comp, rc, isDimmed);

        // A name overlapping a higher-priority (selected > hovered > rest) label is skipped
        // rather than drawn as illegible overlapping text.
        if (visibleNameIds.Contains(comp.Component.Id)
            && _nameLabels.TryGetLabelText(comp.Component.Id) is { } labelText
            && IsNameLegibleAtZoom(comp.Width, labelText.Width, rc.Zoom, comp.IsSelected || isHovered))
            _pinRenderer.DrawComponentName(rc.Labels, comp, labelText, isDimmed, comp.IsSelected || isHovered,
                LightEmphasis.EndpointNameBrushFor(rc, comp.Component));

        if (comp.IsLocked)
            DrawLockIcon(context, comp, rc.Zoom);

        if (comp.IsLightSource)
        {
            LaserIndicatorRenderer.Draw(context, comp,
                rc.InteractionState.HoveredLaserIconComponent == comp,
                rc.ViewModel.IsSimulationModeActive);
        }
    }

    /// <summary>Name-label level of detail: the owner must be at least
    /// <see cref="NameLodMinOwnerFraction"/> of the label's width on screen, unless emphasized.</summary>
    internal static bool IsNameLegibleAtZoom(double ownerWidthWorld, double labelWidthWorld, double zoom, bool isEmphasized) =>
        isEmphasized || ownerWidthWorld >= NameLodMinOwnerFraction * labelWidthWorld;

    private static IDisposable? PushDim(DrawingContext context, bool isDimmed) =>
        isDimmed ? context.PushOpacity(DimmedOpacity) : null;

    /// <summary>Opacity of components outside the group being edited.</summary>
    private const double DimmedOpacity = 0.5;

    private void DrawComponentBody(DrawingContext context, ComponentViewModel comp, CanvasRenderContext rc,
        Rect rect, bool isHovered)
    {
        var state = new ComponentBodyRenderer.BodyState(comp.IsSelected, isHovered,
            comp.IsSelected ? rc.Motion?.SelectionGlow(comp.Component.Id) ?? 1.0 : 0);

        if (comp.Component.OutlinePolygons is { Count: > 0 } outlines)
        {
            // GDS-imported component: draw its outline polygons instead of the plain
            // rectangle body. No Nazca preview is fetched for it — the real imported
            // geometry is already on screen, and the synthesized import function name
            // would only spawn a doomed Python render per unique cell.
            _outlineRenderer.Draw(context, comp, outlines, false, rc.Zoom, rc.LayerVisibility,
                RenderCulling.ComputeViewportWorld(rc.ViewModel.PanX, rc.ViewModel.PanY, rc.Bounds, rc.Zoom));
            if (comp.IsSelected || isHovered)
                ComponentBodyRenderer.DrawOutlineOnly(context, rect, state, rc);
            return;
        }

        var previewData = rc.GdsPreviewRenderService?.TryGetPreview(comp.Component);
        ComponentBodyRenderer.Draw(context, comp.Component, rect, state, rc, previewData);
    }

    private void DrawComponentGroup(DrawingContext context, ComponentGroup group, bool isSelected,
        CanvasRenderContext rc, IReadOnlySet<Guid> visibleNameIds, Rect cullRect, bool isDimmed = false)
    {
        var vm = rc.ViewModel;
        bool isHovered = rc.InteractionState.HoveredGroup == group;
        bool isCurrentEditGroup = vm.CurrentEditGroup == group;

        var bounds = ComponentGroupRenderer.CalculateGroupBounds(group);
        if (group.OutlinePolygons is { Count: > 0 } backgroundPolygons && cullRect.Intersects(bounds))
        {
            // Imported background geometry (base plates, exclusion zones, logos)
            // draws beneath the children so components stay legible on top.
            _outlineRenderer.Draw(context, group.PhysicalX, group.PhysicalY,
                group.WidthMicrometers, group.HeightMicrometers,
                group.RotationDegrees, backgroundPolygons, false, rc.Zoom,
                group.UnrotatedWidthMicrometers, group.UnrotatedHeightMicrometers,
                rc.LayerVisibility);
        }

        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nestedGroup)
            {
                DrawComponentGroup(context, nestedGroup, isSelected, rc, visibleNameIds, cullRect, isDimmed);
                continue;
            }

            var childRect = new Rect(child.PhysicalX, child.PhysicalY, child.WidthMicrometers, child.HeightMicrometers);
            if (cullRect.Intersects(childRect))
                DrawGroupChild(context, child, childRect, rc, visibleNameIds, isHovered, isDimmed);
        }

        var powerFlowResult = vm.ShowPowerFlow ? vm.PowerFlowVisualizer.CurrentResult : null;
        var fadeThreshold = vm.PowerFlowVisualizer.FadeThresholdDb;
        foreach (var frozenPath in group.InternalPaths)
        {
            // Frozen paths may reach outside the group's child bounds, so each one
            // is culled individually by its cached bounding box.
            if (RenderCulling.GetFrozenPathBounds(frozenPath) is { } pathBounds && !cullRect.Intersects(pathBounds))
                continue;
            ComponentGroupRenderer.RenderFrozenWaveguidePath(context, frozenPath, powerFlowResult, fadeThreshold, cullRect, rc.LayerVisibility, zoom: rc.Zoom);
        }

        if (!cullRect.Intersects(bounds))
            return;

        if (!isCurrentEditGroup)
        {
            if (isSelected)
                ComponentGroupRenderer.RenderGroupSelectionBorder(context, bounds, rc, group.Id);
            else
                ComponentGroupRenderer.RenderGroupBorder(context, bounds, isHovered, rc.Pens);

            if (GroupBrokenPathMarkerRenderer.NeedsMarker(group))
                GroupBrokenPathMarkerRenderer.Render(context, bounds, rc.Pens);

            ComponentGroupRenderer.RenderGroupNameLabel(context, bounds, group.GroupName, false,
                rc.InteractionState.HoveredGroupLabel == group, rc.Zoom);
            ComponentGroupRenderer.RenderGroupLockIcon(context, group,
                rc.InteractionState.HoveredGroupLockIcon == group);
        }

        RenderGroupPins(context, group, isCurrentEditGroup, isHovered, vm, rc.Labels);
    }

    private void DrawGroupChild(DrawingContext context, Component child, Rect childRect,
        CanvasRenderContext rc, IReadOnlySet<Guid> visibleNameIds, bool isGroupHovered, bool isDimmed)
    {
        if (child.OutlinePolygons is { Count: > 0 } childOutlines)
        {
            // GDS-imported child: draw its outline polygons instead of the plain
            // rectangle body — the same branch DrawComponent takes for a top-level
            // outlined component (same renderer). Grouped children keep their thin bbox
            // border: inside a group the footprint rectangle is the hover/selection
            // affordance, so it stays visible just like the selection border does for
            // outlined top-level components.
            _outlineRenderer.Draw(context, child.PhysicalX, child.PhysicalY,
                child.WidthMicrometers, child.HeightMicrometers,
                child.RotationDegrees, childOutlines, false, rc.Zoom,
                child.UnrotatedWidthMicrometers, child.UnrotatedHeightMicrometers,
                rc.LayerVisibility, child.IsMirroredHorizontally);
            context.DrawRectangle(null, rc.Pens.ComponentBorder, childRect);
        }
        else
        {
            using (LightEmphasis.PushRecede(context, rc, child, isGroupHovered))
                ComponentBodyRenderer.Draw(context, child, childRect, default, rc,
                    rc.GdsPreviewRenderService?.TryGetPreview(child));
        }

        if (isGroupHovered)
            ComponentGroupRenderer.RenderGroupHoverOverlay(context, childRect.X, childRect.Y, childRect.Width, childRect.Height);

        if (RenderCulling.IsBelowLodThreshold(childRect.Width, childRect.Height, rc.Zoom))
            return;

        // Child names go through the same declutter pass as top-level names (measured bounds,
        // flat-below-footprint anchor, overlap resolution) — two stacked flat children must
        // never draw both labels on top of each other. Deferred to the topmost label pass:
        // a top-level component drawn after this group must never paint over a child name.
        if (rc.Zoom >= GroupOverviewZoom
            && visibleNameIds.Contains(child.Id)
            && _nameLabels.TryGetLabelText(child.Id) is { } childLabelText
            && IsNameLegibleAtZoom(childRect.Width, childLabelText.Width, rc.Zoom, isEmphasized: false))
        {
            rc.Labels.Enqueue(childLabelText, isDimmed ? DimmedChildNameBrush : CanvasPalette.TextSecondaryBrush,
                ComponentNameLabelComputer.GetLabelAnchor(
                    childRect.X, childRect.Y, childRect.Height, childLabelText.Height));
        }
    }

    private static void RenderGroupPins(DrawingContext context, ComponentGroup group, bool isCurrentEditGroup, bool isHovered, DesignCanvasViewModel vm, DeferredLabelLayer labels)
    {
        if (!isCurrentEditGroup)
        {
            var allConnections = vm.Connections.Select(c => c.Connection);
            var unoccupiedPins = GroupPinOccupancyChecker.GetUnoccupiedPins(group, allConnections);
            var highlightedPin = vm.HighlightedPin?.Pin;
            foreach (var externalPin in unoccupiedPins)
            {
                bool isPinHovered = highlightedPin != null && externalPin.InternalPin == highlightedPin;
                ComponentGroupRenderer.RenderUnoccupiedGroupPin(context, externalPin, group, isPinHovered, labels);
            }
        }
        else
        {
            foreach (var externalPin in group.ExternalPins)
                ComponentGroupRenderer.RenderExternalPin(context, externalPin, group, isHovered, labels);
        }
    }

    private static void DrawLockIcon(DrawingContext context, ComponentViewModel comp, double zoom)
    {
        double iconSize = Math.Clamp(Math.Min(comp.Width, comp.Height) * 0.25, 12, 24);
        double iconX = comp.X + comp.Width - iconSize - 4;
        double iconY = comp.Y + comp.Height - iconSize - 4;

        context.DrawEllipse(LockIconBackdrop, null,
            new Point(iconX + iconSize / 2, iconY + iconSize / 2), iconSize / 2, iconSize / 2);

        double bodyWidth = iconSize * 0.5;
        double bodyHeight = iconSize * 0.4;
        double bodyX = iconX + (iconSize - bodyWidth) / 2;
        double bodyY = iconY + iconSize * 0.5;
        double corner = bodyWidth * 0.15;
        context.DrawRectangle(CanvasPalette.WarningBrush, null, new Rect(bodyX, bodyY, bodyWidth, bodyHeight), corner, corner);

        double shackleWidth = iconSize * 0.34;
        double shackleHeight = iconSize * 0.24;
        double shackleCenterX = iconX + iconSize / 2;
        var shackleGeometry = new StreamGeometry();
        using (var ctx = shackleGeometry.Open())
        {
            ctx.BeginFigure(new Point(shackleCenterX - shackleWidth / 2, bodyY), false);
            ctx.ArcTo(new Point(shackleCenterX + shackleWidth / 2, bodyY),
                new Size(shackleWidth / 2, shackleHeight), 0, false, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, new Pen(CanvasPalette.WarningBrush, iconSize * 0.09), shackleGeometry);
    }

    private static bool IsComponentDimmedInEditMode(ComponentViewModel comp, DesignCanvasViewModel vm)
    {
        if (!vm.IsInGroupEditMode || vm.CurrentEditGroup == null)
            return false;
        return !vm.CurrentEditGroup.ChildComponents.Contains(comp.Component);
    }
}
