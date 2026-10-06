using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using Shouldly;
using UnitTests.Services.GdsImport;
using UnitTests.Services.GdsImport.ProductionScale;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.Integration;

/// <summary>
/// Import, routing and rendering at production scale on <see cref="ProductionScaleChipFixture"/>
/// (159 instances, 128 Nazca-drawn routes, ~700 k top-cell vertices). The budgets are
/// generous for slow CI runners but an order of magnitude below the timings before the
/// lossless/raster work (import 55 s, routing pass 5.5 s, zoomed-out frame ~190 ms on a
/// desktop), so a regression back to per-cell or per-polygon work fails here.
/// </summary>
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public sealed class ProductionScaleImportTests : IDisposable
{
    private const double ImportBudgetSeconds = 20;
    private const double RoutingPassBudgetSeconds = 6;
    private const double ZoomedOutFrameBudgetMs = 120;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lunima-prodscale-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<(DesignCanvasViewModel Canvas, TimeSpan Elapsed)> ImportAsync()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, 1000, 1000);
        var dialog = new GdsImportDialogViewModel(ProductionScaleChipFixture.WriteTo(_root), _host.CreateService(),
            new GdsPlacementExecutor(canvas, null, () => _host.Templates.ToList()));
        await dialog.StartAnalysisAsync();
        // The layers the dialog's detection picks on the production file it mirrors.
        dialog.PortLayersText = "235,0";
        dialog.WaveguideLayersText = "401,0";
        dialog.MetalLayersText = "";
        var stopwatch = Stopwatch.StartNew();
        await dialog.ImportCommand.ExecuteAsync(null);
        dialog.HasError.ShouldBeFalse(dialog.ErrorText);
        return (canvas, stopwatch.Elapsed);
    }

    [Fact]
    public async Task Import_KeepsEveryRouteFrozenAsDrawn_WithinBudget()
    {
        var (canvas, elapsed) = await ImportAsync();

        elapsed.TotalSeconds.ShouldBeLessThan(ImportBudgetSeconds);
        canvas.Components.Count.ShouldBe(159);
        var connections = canvas.Connections.Select(c => c.Connection).ToList();
        connections.Count.ShouldBe(ProductionScaleChipFixture.RouteCount);
        connections.ShouldAllBe(c => c.IsRouteFrozen && c.AsDrawnGeometry != null);
        connections.Sum(c => c.RoutedPath!.Segments.OfType<CAP_Core.Routing.BendSegment>().Count())
            .ShouldBe(2 * ProductionScaleChipFixture.RouteCount, "every drawn arc is a real bend");
        canvas.Components.Count(c => c.Component.IsMirroredHorizontally).ShouldBe(ProductionScaleChipFixture.CouplersPerSide);
        canvas.Components.Single(c => c.Width > 16000).Component.IsRoutingObstacle.ShouldBeFalse("the die frame is background");
    }

    [Fact]
    public async Task RoutingPass_KeepsTheImportUntouched_WithinBudget()
    {
        var (canvas, _) = await ImportAsync();

        var stopwatch = Stopwatch.StartNew();
        await canvas.RecalculateRoutesAsync();

        stopwatch.Elapsed.TotalSeconds.ShouldBeLessThan(RoutingPassBudgetSeconds);
        canvas.Connections.ShouldAllBe(c => c.Connection.IsRouteFrozen && c.Connection.AsDrawnGeometry != null);
    }

    [AvaloniaFact]
    public async Task ZoomedOutFrame_RendersWithinBudget()
    {
        var (canvas, _) = await ImportAsync();
        var world = new Rect(-200, -200, 17000, 13500);
        var size = new PixelSize(1800, 1000);
        double scale = size.Width / world.Width;
        canvas.PanX = -world.X * scale;
        canvas.PanY = -world.Y * scale;
        var connections = new WaveguideConnectionRenderer();
        var components = new ComponentRenderer();

        var frames = new List<double>();
        for (int frame = 0; frame < 6; frame++)
        {
            using var bitmap = new RenderTargetBitmap(size);
            var stopwatch = Stopwatch.StartNew();
            using (var ctx = bitmap.CreateDrawingContext())
            {
                var rc = new CanvasRenderContext
                {
                    ViewModel = canvas,
                    InteractionState = new CanvasInteractionState(),
                    Zoom = scale,
                    Bounds = new Rect(0, 0, size.Width, size.Height),
                };
                using var _ = ctx.PushTransform(Matrix.CreateTranslation(-world.X, -world.Y) * Matrix.CreateScale(scale, scale));
                connections.Render(ctx, rc);
                components.Render(ctx, rc);
            }
            if (frame > 0) frames.Add(stopwatch.Elapsed.TotalMilliseconds); // frame 0 fills the raster cache
        }

        frames.Order().ElementAt(frames.Count / 2).ShouldBeLessThan(ZoomedOutFrameBudgetMs,
            $"median zoomed-out frame; frames: {string.Join(", ", frames.Select(f => f.ToString("F0")))} ms");
    }
}
