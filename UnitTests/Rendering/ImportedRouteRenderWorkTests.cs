using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Rendering;

public class ImportedRouteRenderWorkTests
{
    [AvaloniaFact]
    public void VisibleImportedConnection_ValidatesMutableRouteOnlyOncePerFrame()
    {
        var canvas = new DesignCanvasViewModel();
        var start = TestComponentFactory.CreateBasicComponent();
        var end = TestComponentFactory.CreateBasicComponent();
        end.PhysicalX = 400;
        var connection = TestComponentFactory.CreateConnection(start, end);
        var a = connection.StartPin.GetAbsolutePosition();
        var b = connection.EndPin.GetAbsolutePosition();
        var segment = new CountingStraight(a.x, a.y, b.x, b.y);
        var path = new RoutedPath();
        path.Segments.Add(segment);
        connection.RestoreCachedPath(path);
        connection.IsRouteFrozen = true;
        connection.AttachAsDrawnGeometry(new AsDrawnGeometry(new[] { Square(260, 120) }));
        canvas.Connections.Add(new WaveguideConnectionViewModel(connection));
        var rc = Context(canvas, 500);
        var renderer = new WaveguideConnectionRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(500, 200));
        segment.Reads = 0;
        using (var ctx = bitmap.CreateDrawingContext()) renderer.Render(ctx, rc);
        segment.Reads.ShouldBe(1, "one shape validation supplies geometry and all warning decisions in this frame");

        end.PhysicalX += 15;
        segment.Reads = 0;
        var moved = Context(canvas, 500);
        using (var ctx = bitmap.CreateDrawingContext()) renderer.Render(ctx, moved);
        segment.Reads.ShouldBe(1);
        renderer.BlockedMarkers.PendingCount.ShouldBe(1, "moved imported endpoints still get warning markers");
        moved.Labels.Pending.Count.ShouldBe(1, "the endpoint mismatch warning remains visible");

        segment.EndPoint = (b.x + 20, b.y);
        segment.Reads = 0;
        using (var ctx = bitmap.CreateDrawingContext()) renderer.Render(ctx, Context(canvas, 500));
        segment.Reads.ShouldBe(1, "mutable geometry is validated again next frame");
        connection.AsDrawnGeometry.ShouldBeNull("a changed route must never reuse its source polygons");
    }

    [AvaloniaFact]
    public void CanvasFrozenSourcePolygons_SkipTilesOutsideViewport()
    {
        var canvas = new DesignCanvasViewModel();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(20, 20, 1040, 40, 0));
        var frozen = new FrozenWaveguidePath { Path = path };
        frozen.AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(20, 20), Square(1020, 20) });
        canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(frozen));
        using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 100));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 1100, 100));
            new CanvasFrozenPathRenderer().Render(ctx, Context(canvas, 100));
        }
        PixelIsLit(bitmap, 25, 25).ShouldBeTrue("visible source geometry still renders");
        PixelIsLit(bitmap, 1025, 25).ShouldBeFalse("off-screen tiles must not be submitted even when the full route intersects the viewport");
    }

    [AvaloniaTheory]
    [Xunit.InlineData(false)]
    [Xunit.InlineData(true)]
    public void FrozenSourcePolygons_VisibleMaskOutsideCenterlineBoundsIsNotCulled(bool grouped)
    {
        var canvas = new DesignCanvasViewModel();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(200, 20, 240, 40, 0));
        var frozen = new FrozenWaveguidePath { Path = path };
        frozen.AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(20, 20) });
        if (grouped)
        {
            var group = new ComponentGroup("offscreen");
            var child = TestComponentFactory.CreateBasicComponent();
            child.PhysicalX = 300;
            child.PhysicalY = 300;
            group.AddChild(child);
            group.InternalPaths.Add(frozen);
            canvas.AddComponent(group);
        }
        else canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(frozen));
        using var bitmap = new RenderTargetBitmap(new PixelSize(100, 100));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 100, 100));
            if (grouped) new ComponentRenderer().Render(ctx, Context(canvas, 100));
            else new CanvasFrozenPathRenderer().Render(ctx, Context(canvas, 100));
        }
        PixelIsLit(bitmap, 25, 25).ShouldBeTrue("source masks can extend beyond the fitted centerline");
    }

    [AvaloniaFact]
    public void GroupFrozenSourcePolygons_SkipTilesOutsideViewport()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(20, 20, 1040, 40, 0));
        var frozen = new FrozenWaveguidePath { Path = path };
        frozen.AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(20, 20), Square(1020, 20) });
        using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 100));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 1100, 100));
            ComponentGroupRenderer.RenderFrozenWaveguidePath(ctx, frozen, cullRect: new Rect(0, 0, 100, 100));
        }
        PixelIsLit(bitmap, 25, 25).ShouldBeTrue();
        PixelIsLit(bitmap, 1025, 25).ShouldBeFalse("group source polygons must respect the same viewport as traced paths");
    }

    [AvaloniaFact]
    public void CanvasFrozenSources_ReuseOneRasterAfterUngroup_AndKeepSelectedOverlay()
    {
        var canvas = new DesignCanvasViewModel();
        for (int i = 0; i < 32; i++)
        {
            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(20, 30, 40, 30, 0));
            var frozen = new FrozenWaveguidePath { Path = path };
            frozen.AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(20, 20) });
            canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(frozen));
        }
        var rc = new CanvasRenderContext
        {
            ViewModel = canvas, InteractionState = new CanvasInteractionState(), Zoom = 0.5,
            Bounds = new Rect(0, 0, 100, 100)
        };
        var renderer = new CanvasFrozenPathRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(100, 100));
        using (var ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(0.5, 0.5))) renderer.Render(ctx, rc);
        using (var ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(0.5, 0.5))) renderer.Render(ctx, rc);
        renderer.IssuedSourceImageCount.ShouldBe(1, "ungrouped source paths should share a single cached overview image");
        var unselected = PixelValue(bitmap, 15, 15);
        canvas.CanvasFrozenPaths[0].IsSelected = true;
        using (var ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(0.5, 0.5))) renderer.Render(ctx, rc);
        renderer.IssuedSourceImageCount.ShouldBe(2, "selection does not invalidate source geometry");
        PixelValue(bitmap, 15, 15).ShouldNotBe(unselected, "selection recolors the source mask above cached source geometry");
    }

    [AvaloniaFact]
    public void DenseFrozenSelection_UsesAggregateRasterForSelectedSubsetAndInvalidatesOnChange()
    {
        var canvas = new DesignCanvasViewModel();
        for (int i = 0; i < 64; i++)
        {
            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(i * 25, 30, i * 25 + 20, 30, 0));
            var frozen = new FrozenWaveguidePath { Path = path,
                AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(i * 25, 20) }) };
            canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(frozen) { IsSelected = i % 2 == 0 });
        }
        var rc = new CanvasRenderContext { ViewModel = canvas, InteractionState = new CanvasInteractionState(),
            Zoom = .5, Bounds = new Rect(0, 0, 1000, 100) };
        var renderer = new CanvasFrozenPathRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(1000, 100));
        void Draw()
        {
            using var ctx = bitmap.CreateDrawingContext();
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 1000, 100));
            using var scale = ctx.PushTransform(Matrix.CreateScale(.5, .5));
            renderer.Render(ctx, rc);
        }
        Draw(); Draw();
        long before = renderer.IssuedSourceImageCount;
        Draw();
        renderer.IssuedSourceImageCount.ShouldBe(before + 2, "base and selected subset each use one image");
        int selected = PixelValue(bitmap, 5, 15), plain = PixelValue(bitmap, 17, 15);
        selected.ShouldNotBe(plain);
        foreach (var path in canvas.CanvasFrozenPaths) path.IsSelected = !path.IsSelected;
        Draw(); Draw(); Draw();
        PixelValue(bitmap, 5, 15).ShouldBe(plain, "the previous selected subset must not remain tinted");
        PixelValue(bitmap, 17, 15).ShouldBe(selected);
        foreach (var path in canvas.CanvasFrozenPaths) path.IsSelected = true;
        Draw();
        before = renderer.IssuedSourceImageCount;
        Draw();
        renderer.IssuedSourceImageCount.ShouldBe(before + 2, "select all reuses the complete source image");
    }

    [AvaloniaTheory]
    [Xunit.InlineData(false)]
    [Xunit.InlineData(true)]
    public void ImportedConnection_EmphasisHighlightsSourceWithoutInventingCenterline(bool selected)
    {
        var canvas = new DesignCanvasViewModel();
        var start = TestComponentFactory.CreateBasicComponent();
        var end = TestComponentFactory.CreateBasicComponent();
        var connection = TestComponentFactory.CreateConnection(start, end);
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(20, 60, 180, 60, 0));
        connection.RestoreCachedPath(path);
        connection.IsRouteFrozen = true;
        connection.AttachAsDrawnGeometry(new AsDrawnGeometry(new[] { Square(20, 20) }));
        var vm = new WaveguideConnectionViewModel(connection) { IsSelected = selected };
        canvas.Connections.Add(vm);
        var rc = Context(canvas, 200);
        if (!selected) rc.InteractionState.HoveredConnection = vm;
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 200));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 200, 200));
            new WaveguideConnectionRenderer().Render(ctx, rc);
        }
        PixelIsLit(bitmap, 100, 60).ShouldBeFalse("the fitted centerline is not the imported mask");
        PixelIsLit(bitmap, 25, 25).ShouldBeTrue("the exact source remains visible");
    }

    [AvaloniaFact]
    public void SelectedFrozenImport_HighlightsSourceWithoutInventingCenterline()
    {
        var canvas = new DesignCanvasViewModel();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(20, 60, 180, 60, 0));
        var frozen = new FrozenWaveguidePath { Path = path,
            AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(20, 20) }) };
        canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(frozen) { IsSelected = true });
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 200));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 200, 200));
            new CanvasFrozenPathRenderer().Render(ctx, Context(canvas, 200));
        }
        PixelIsLit(bitmap, 100, 60).ShouldBeFalse();
        PixelIsLit(bitmap, 25, 25).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Overview_SelectedMismatchedImports_DoNotQueueWarningsOrLabels()
    {
        var canvas = new DesignCanvasViewModel();
        for (int i = 0; i < 100; i++)
        {
            var start = TestComponentFactory.CreateBasicComponent();
            var end = TestComponentFactory.CreateBasicComponent();
            var connection = TestComponentFactory.CreateConnection(start, end);
            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(20, 60, 180, 60, 0));
            connection.RestoreCachedPath(path);
            connection.IsRouteFrozen = true;
            connection.AttachAsDrawnGeometry(new AsDrawnGeometry(new[] { Square(20, 20) }));
            canvas.Connections.Add(new WaveguideConnectionViewModel(connection) { IsSelected = true });
        }
        var rc = new CanvasRenderContext { ViewModel = canvas, InteractionState = new CanvasInteractionState(),
            Zoom = .01, Bounds = new Rect(0, 0, 200, 200) };
        rc.InteractionState.HoveredConnection = canvas.Connections[0];
        var renderer = new WaveguideConnectionRenderer();
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 200));
        using (var ctx = bitmap.CreateDrawingContext()) renderer.Render(ctx, rc);
        renderer.BlockedMarkers.PendingCount.ShouldBe(0);
        rc.Labels.Pending.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Overview_FocusedPinKeepsItsLabelWithoutRestoringSelectedNames()
    {
        var canvas = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        canvas.AddComponent(component);
        var vm = canvas.Components[0];
        vm.IsSelected = true;
        canvas.UpdatePinHighlight(0, 0.5);
        var rc = new CanvasRenderContext { ViewModel = canvas, InteractionState = new CanvasInteractionState(),
            Zoom = .01, Bounds = new Rect(0, 0, 200, 200) };
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 200));
        using (var ctx = bitmap.CreateDrawingContext()) new ComponentRenderer().Render(ctx, rc);
        rc.Labels.Pending.Count.ShouldBe(1, "only the actively focused pin remains readable");
    }

    [AvaloniaFact]
    public void Overview_GroupWarningBadgeDoesNotCoverGeometry()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(100, 100));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, 100, 100));
            using (ctx.PushTransform(Matrix.CreateScale(.1, .1)))
                GroupBrokenPathMarkerRenderer.Render(ctx, new Rect(100, 100, 400, 400), CanvasPens.For(.1));
        }
        PixelIsLit(bitmap, 51, 11).ShouldBeFalse("overview suppresses warning badges as well as route text");
    }

    [AvaloniaTheory]
    [Xunit.InlineData(false)]
    [Xunit.InlineData(true)]
    public void ReplacingFrozenSelection_ReleasesPreviousSourceGeometry(bool selectReplacement)
    {
        var canvas = new DesignCanvasViewModel();
        var renderer = new CanvasFrozenPathRenderer();
        var oldSource = RenderThenReplaceSources(canvas, renderer, selectReplacement);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        oldSource.IsAlive.ShouldBeFalse("selection snapshots must release the previous design after empty or full replacement");
        GC.KeepAlive(canvas);
        GC.KeepAlive(renderer);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RenderThenReplaceSources(DesignCanvasViewModel canvas,
        CanvasFrozenPathRenderer renderer, bool selectReplacement)
    {
        for (int i = 0; i < 64; i++)
        {
            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(20, 30, 40, 30, 0));
            canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(new FrozenWaveguidePath { Path = path,
                AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(20, 20) }) }) { IsSelected = i % 2 == 0 });
        }
        var old = new WeakReference(canvas.CanvasFrozenPaths[0].Path.AsDrawnGeometry!);
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 200));
        using (var context = bitmap.CreateDrawingContext()) renderer.Render(context, Context(canvas, 200));
        canvas.CanvasFrozenPaths.Clear();
        if (selectReplacement)
        {
            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(80, 30, 100, 30, 0));
            canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(new FrozenWaveguidePath { Path = path,
                AsDrawnGeometry = new AsDrawnGeometry(new[] { Square(80, 20) }) }) { IsSelected = true });
        }
        using (var context = bitmap.CreateDrawingContext()) renderer.Render(context, Context(canvas, 200));
        return old;
    }

    private static int PixelValue(RenderTargetBitmap bitmap, int x, int y)
    {
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            return System.Runtime.InteropServices.Marshal.ReadInt32(buffer);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
    }
    private static CanvasRenderContext Context(DesignCanvasViewModel canvas, double width) => new()
    {
        ViewModel = canvas, InteractionState = new CanvasInteractionState(), Zoom = 1,
        Bounds = new Rect(0, 0, width, 200)
    };

    private static OutlinePolygon Square(double x, double y) => new()
    {
        Layer = 1, Points = new[] { new OutlinePoint(x, y), new OutlinePoint(x + 20, y),
            new OutlinePoint(x + 20, y + 20), new OutlinePoint(x, y + 20), new OutlinePoint(x, y) }
    };

    private static bool PixelIsLit(RenderTargetBitmap bitmap, int x, int y)
    {
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            return Enumerable.Range(0, 3).Any(i => System.Runtime.InteropServices.Marshal.ReadByte(buffer, i) > 0);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
    }

    private sealed class CountingStraight(double x0, double y0, double x1, double y1)
        : StraightSegment(x0, y0, x1, y1, 0)
    {
        public int Reads;
        public override double LengthMicrometers { get { Reads++; return base.LengthMicrometers; } }
    }
}
