using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Times full canvas frames of the largest shipped example (ALU + RAM: 345 gate groups,
/// thousands of frozen waveguide paths) at the zoom-to-fit overview and at 100 % zoom. The
/// budget is generous — headless Skia on a shared CI runner — and only catches an
/// order-of-magnitude regression; the measured medians are written to the test output.
/// </summary>
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class CanvasRenderPerformanceTests
{
    private const string LargeExample = "Logic Gate ALU + RAM.lun";
    private const int Width = 1600;
    private const int Height = 1000;
    private const int WarmupFrames = 2;
    private const int MeasuredFrames = 8;

    /// <summary>Median frame budget; the UI-thread budget in CLAUDE.md §5 is ~100 ms on a desktop GPU.</summary>
    private static readonly TimeSpan MedianFrameBudget = TimeSpan.FromMilliseconds(1500);

    private readonly ITestOutputHelper _output;

    /// <summary>Receives the xUnit output sink for the timing report.</summary>
    public CanvasRenderPerformanceTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public async Task LargeExample_FullFrame_StaysWithinBudget()
    {
        var canvas = await CanvasRenderHarness.LoadExampleAsync(LargeExample);

        double fitZoom = CanvasRenderHarness.FitToView(canvas, Width, Height);
        var overview = MeasureMedian(CanvasRenderHarness.CreateCanvas(canvas, fitZoom, Width, Height));

        var bounds = CAP.Avalonia.ViewModels.Canvas.BoundingBoxCalculator.Calculate(canvas.Components)!.Value;
        CanvasRenderHarness.CenterOn(canvas, (bounds.MinX + bounds.MaxX) / 2, (bounds.MinY + bounds.MaxY) / 2, 1.0, Width, Height);
        var closeUp = MeasureMedian(CanvasRenderHarness.CreateCanvas(canvas, 1.0, Width, Height));

        _output.WriteLine($"{LargeExample}: {canvas.Components.Count} components, {canvas.Connections.Count} connections");
        _output.WriteLine($"overview (zoom {fitZoom:F3}): median {overview.TotalMilliseconds:F1} ms");
        _output.WriteLine($"close-up (zoom 1.000): median {closeUp.TotalMilliseconds:F1} ms");

        overview.ShouldBeLessThan(MedianFrameBudget);
        closeUp.ShouldBeLessThan(MedianFrameBudget);
    }

    private static TimeSpan MeasureMedian(CAP.Avalonia.Controls.DesignCanvas control)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
        for (int i = 0; i < WarmupFrames; i++)
            CanvasRenderHarness.RenderInto(control, bitmap);

        var samples = new List<TimeSpan>(MeasuredFrames);
        for (int i = 0; i < MeasuredFrames; i++)
        {
            var watch = Stopwatch.StartNew();
            CanvasRenderHarness.RenderInto(control, bitmap);
            samples.Add(watch.Elapsed);
        }
        samples.Sort();
        return samples[MeasuredFrames / 2];
    }
}
