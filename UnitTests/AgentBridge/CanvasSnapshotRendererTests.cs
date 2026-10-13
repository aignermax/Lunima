using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// The <c>screenshot</c> tool renders through the real canvas renderer off-screen and must
/// hand the live canvas its borrowed state (pan, repaint callback) back untouched.
/// </summary>
public class CanvasSnapshotRendererTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47 };

    private static DesignCanvasViewModel CanvasWithOneComponent()
    {
        var canvas = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.PhysicalX = 200;
        component.PhysicalY = 300;
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 250;
        canvas.AddComponent(component);
        return canvas;
    }

    [AvaloniaFact]
    public void Render_ProducesPng_AndRestoresLiveCanvasState()
    {
        var canvas = CanvasWithOneComponent();
        canvas.PanX = 12;
        canvas.PanY = -34;
        Action repaint = () => { };
        canvas.RepaintRequested = repaint;

        var (png, width, height) = new CanvasSnapshotRenderer(() => null)
            .Render(canvas, new Rect(0, 0, 1000, 500), 400);

        png.Take(4).ShouldBe(PngSignature);
        (width, height).ShouldBe((400, 200));
        canvas.PanX.ShouldBe(12);
        canvas.PanY.ShouldBe(-34);
        canvas.RepaintRequested.ShouldBeSameAs(repaint, "the on-screen canvas keeps its repaint callback");
    }

    [Fact]
    public void FitZoom_CapsTallRegionsAtTheEdgeLimit()
    {
        var zoom = CanvasSnapshotRenderer.FitZoom(new Rect(0, 0, 10, 100_000), 1000);

        (100_000 * zoom).ShouldBeLessThanOrEqualTo(CanvasSnapshotRenderer.MaxEdgePixels);
    }

    [AvaloniaFact]
    public async Task ScreenshotTool_ReturnsImageContent_FramingTheDesign()
    {
        var canvas = CanvasWithOneComponent();
        var session = new AgentDesignSession(canvas, new CommandManager(), () => null, new ExampleDesignsService());
        var tool = new ScreenshotTool(session, new CanvasSnapshotRenderer(() => null));

        var result = await tool.ExecuteAsync(JsonDocument.Parse("""{"width_px":300}""").RootElement);

        result.IsError.ShouldBeFalse();
        var image = result.Content.Single(c => c.Type == "image");
        image.MimeType.ShouldBe("image/png");
        Convert.FromBase64String(image.Data!).Take(4).ShouldBe(PngSignature);
        ScreenshotTool.DesignRegion(canvas).X.ShouldBeLessThan(200);
    }
}
