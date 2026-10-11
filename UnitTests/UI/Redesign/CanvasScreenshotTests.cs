using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Canvas;
using Xunit;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Captures the redesigned design canvas headless (production <see cref="CAP.Avalonia.Controls.DesignCanvas"/>,
/// shipped examples) into <c>$UI_SHOT_DIR/ui-redesign/</c>: an MZI close-up with body
/// fallbacks (no Nazca previews headless), a selected and a hovered component, the Full Adder
/// at overview and mid zoom (label declutter + level of detail), a blocked connection, and the
/// ALU + RAM overview. No-op unless <c>UI_SHOT_DIR</c> is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class CanvasScreenshotTests
{
    private const int Width = 1600;
    private const int Height = 1000;

    [AvaloniaFact]
    public async Task CaptureRedesignedCanvas()
    {
        var shotDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(shotDir))
            return;
        var outputDir = Path.Combine(shotDir, "ui-redesign");
        Directory.CreateDirectory(outputDir);

        await CaptureMzi(outputDir);
        await CaptureFullAdder(outputDir);
        await CaptureBlockedConnection(outputDir);
        await CaptureLargeOverview(outputDir);
    }

    private static async Task CaptureMzi(string outputDir)
    {
        var canvas = await CanvasRenderHarness.LoadExampleAsync("Mach-Zehnder Interferometer.lun");
        double zoom = CanvasRenderHarness.FitToView(canvas, Width, Height);
        Save(canvas, zoom, outputDir, "canvas-01-mzi.png");

        var selected = canvas.Components[canvas.Components.Count / 2];
        selected.IsSelected = true;
        canvas.SelectedComponent = selected;
        var hovered = canvas.Components.First(c => c != selected);
        Save(canvas, zoom, outputDir, "canvas-02-mzi-selected-hovered.png", hovered);
    }

    private static async Task CaptureFullAdder(string outputDir)
    {
        var canvas = await CanvasRenderHarness.LoadExampleAsync("Logic Gate Full Adder.lun");
        double zoom = CanvasRenderHarness.FitToView(canvas, Width, Height);
        Save(canvas, zoom, outputDir, "canvas-03-full-adder-overview.png");

        var first = canvas.Components[0];
        const double MidZoom = 0.6;
        CanvasRenderHarness.CenterOn(canvas, first.X + first.Width / 2, first.Y + first.Height / 2, MidZoom, Width, Height);
        Save(canvas, MidZoom, outputDir, "canvas-04-full-adder-mid-zoom.png");
    }

    private static async Task CaptureBlockedConnection(string outputDir)
    {
        var canvas = await CanvasRenderHarness.LoadExampleAsync("Logic Gate Counter 2-bit.lun");
        var blocked = canvas.Connections.FirstOrDefault(c => c.IsBlockedFallback);
        if (blocked == null)
            return;
        const double Zoom = 0.8;
        CanvasRenderHarness.CenterOn(canvas, (blocked.StartX + blocked.EndX) / 2, (blocked.StartY + blocked.EndY) / 2,
            Zoom, Width, Height);
        Save(canvas, Zoom, outputDir, "canvas-05-blocked-connection.png");
    }

    private static async Task CaptureLargeOverview(string outputDir)
    {
        var canvas = await CanvasRenderHarness.LoadExampleAsync("Logic Gate ALU + RAM.lun");
        double zoom = CanvasRenderHarness.FitToView(canvas, Width, Height);
        Save(canvas, zoom, outputDir, "canvas-06-alu-ram-overview.png");
    }

    private static void Save(DesignCanvasViewModel canvas, double zoom, string outputDir, string fileName,
        ComponentViewModel? hovered = null)
    {
        var control = CanvasRenderHarness.CreateCanvas(canvas, zoom, Width, Height);
        control.InteractionState.HoveredComponent = hovered;
        using var bitmap = CanvasRenderHarness.Render(control, Width, Height);
        bitmap.Save(Path.Combine(outputDir, fileName));
    }
}
