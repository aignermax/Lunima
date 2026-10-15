using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CAP.Avalonia.Controls.Canvas.AnalysisOutput;
using CAP.Avalonia.Controls.Canvas.BendHandles;
using CAP.Avalonia.Controls.Canvas.CrossingDrag;
using CAP.Avalonia.Controls.Canvas.CutTool;
using CAP.Avalonia.Controls.Canvas.GateView;
using CAP.Avalonia.Controls.Canvas.LibraryDrop;
using CAP.Avalonia.Controls.Canvas.SegmentShiftHandles;
using CAP.Avalonia.Controls.Handlers;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Controls.Rendering.WorkspaceEmphasis;
using CAP.Avalonia.Gestures;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls;

public partial class DesignCanvas
{
    private void RenderGateView(DrawingContext context, CanvasRenderContext rc, DesignCanvasViewModel vm, ViewModels.Logic.GateView.GateViewViewModel gateView)
    {
        using (context.PushTransform(Matrix.CreateTranslation(vm.PanX, vm.PanY)))
        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom)))
        {
            _gridRenderer.RenderWorld(context, rc);
            _gateViewRenderer.Render(context, rc, gateView);
            rc.Labels.Flush(context, Zoom);
            _previewRenderer.Render(context, rc);
        }

        _overlayRenderer.Render(context, rc);
        GateViewLegendRenderer.Render(context, Bounds, gateView);
        Motion.EndFrame();
    }
}
