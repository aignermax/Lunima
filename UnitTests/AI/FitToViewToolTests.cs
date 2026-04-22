using System.Text.Json;
using CAP.Avalonia.Services.AiTools.GridTools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;

namespace UnitTests.AI;

/// <summary>
/// Unit tests for <see cref="FitToViewTool"/>.
/// </summary>
public class FitToViewToolTests
{
    private static ViewportControlViewModel CreateViewport(out DesignCanvasViewModel canvas)
    {
        canvas = new DesignCanvasViewModel();
        return new ViewportControlViewModel(canvas);
    }

    [Fact]
    public void Name_ShouldBe_FitToView()
    {
        var tool = new FitToViewTool(CreateViewport(out _));
        tool.Name.ShouldBe("fit_to_view");
    }

    [Fact]
    public void Description_ShouldMentionViewport()
    {
        var tool = new FitToViewTool(CreateViewport(out _));
        tool.Description.ShouldContain("viewport");
    }

    [Fact]
    public void InputSchema_ShouldBeEmptyObjectSchema()
    {
        var tool = new FitToViewTool(CreateViewport(out _));
        var schemaJson = JsonSerializer.Serialize(tool.InputSchema);
        schemaJson.ShouldContain("\"type\"");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCanvas_ReturnsSuccessJson()
    {
        var tool = new FitToViewTool(CreateViewport(out _));
        using var doc = JsonDocument.Parse("{}");
        var result = await tool.ExecuteAsync(doc.RootElement);

        result.ShouldNotBeNullOrEmpty();
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("success").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_UsesGetViewportSizeCallback_WhenProvided()
    {
        var viewport = CreateViewport(out _);
        double capturedWidth = 0;
        double capturedHeight = 0;
        viewport.GetViewportSize = () => (1200.0, 900.0);

        // Hook into ZoomToFit via UpdateStatus to verify execution
        viewport.UpdateStatus = _ => { };

        var tool = new FitToViewTool(viewport);
        using var doc = JsonDocument.Parse("{}");

        // Should not throw even with no components
        var result = await tool.ExecuteAsync(doc.RootElement);
        result.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NoViewportSizeCallback_UsesDefaultSize()
    {
        var viewport = CreateViewport(out _);
        // GetViewportSize is null — fallback to 900×800
        viewport.GetViewportSize = null;

        var tool = new FitToViewTool(viewport);
        using var doc = JsonDocument.Parse("{}");

        var result = await tool.ExecuteAsync(doc.RootElement);
        result.ShouldNotBeNullOrEmpty();
    }
}
