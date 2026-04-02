using Avalonia;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Controls;

/// <summary>
/// Unit tests for <see cref="CanvasRenderContext"/> construction and data integrity.
/// Verifies the context correctly captures canvas state for renderers.
/// </summary>
public class CanvasRenderContextTests
{
    [Fact]
    public void CanvasRenderContext_HoldsAllRequiredState()
    {
        var vm = new DesignCanvasViewModel();
        var state = new CanvasInteractionState();
        var bounds = new Rect(0, 0, 1024, 768);
        const double Zoom = 1.5;

        var ctx = new CanvasRenderContext
        {
            ViewModel = vm,
            MainViewModel = null,
            InteractionState = state,
            Zoom = Zoom,
            Bounds = bounds
        };

        ctx.ViewModel.ShouldBe(vm);
        ctx.MainViewModel.ShouldBeNull();
        ctx.InteractionState.ShouldBe(state);
        ctx.Zoom.ShouldBe(Zoom);
        ctx.Bounds.ShouldBe(bounds);
    }

    [Fact]
    public void CanvasRenderContext_DefaultZoom_IsZero()
    {
        var vm = new DesignCanvasViewModel();
        var ctx = new CanvasRenderContext
        {
            ViewModel = vm,
            InteractionState = new CanvasInteractionState()
        };

        // Default value for double is 0
        ctx.Zoom.ShouldBe(0.0);
    }

    [Fact]
    public void CanvasRenderContext_BoundsDefault_IsEmpty()
    {
        var vm = new DesignCanvasViewModel();
        var ctx = new CanvasRenderContext
        {
            ViewModel = vm,
            InteractionState = new CanvasInteractionState()
        };

        ctx.Bounds.ShouldBe(default(Rect));
    }
}
