using CAP.Avalonia.ViewModels;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for <see cref="BoundingBoxCalculator"/> and <see cref="BoundingBox"/>.
/// </summary>
public class BoundingBoxCalculatorTests
{
    [Fact]
    public void Calculate_EmptyCollection_ReturnsNull()
    {
        var result = BoundingBoxCalculator.Calculate(
            Array.Empty<ComponentViewModel>());

        result.ShouldBeNull();
    }

    [Fact]
    public void Calculate_SingleComponent_ReturnsBounds()
    {
        var comp = CreateComponent(100, 200, 50, 30);

        var result = BoundingBoxCalculator.Calculate(new[] { comp });

        result.ShouldNotBeNull();
        result.Value.MinX.ShouldBe(100);
        result.Value.MinY.ShouldBe(200);
        result.Value.MaxX.ShouldBe(150);
        result.Value.MaxY.ShouldBe(230);
    }

    [Fact]
    public void Calculate_MultipleComponents_EnclosesAll()
    {
        var components = new[]
        {
            CreateComponent(100, 100, 50, 50),
            CreateComponent(300, 400, 100, 80),
            CreateComponent(0, 0, 20, 20),
        };

        var result = BoundingBoxCalculator.Calculate(components);

        result.ShouldNotBeNull();
        result.Value.MinX.ShouldBe(0);
        result.Value.MinY.ShouldBe(0);
        result.Value.MaxX.ShouldBe(400);
        result.Value.MaxY.ShouldBe(480);
    }

    [Fact]
    public void Calculate_BoundingBoxProperties_AreCorrect()
    {
        var comp = CreateComponent(100, 200, 300, 400);

        var result = BoundingBoxCalculator.Calculate(new[] { comp });

        result.ShouldNotBeNull();
        result.Value.Width.ShouldBe(300);
        result.Value.Height.ShouldBe(400);
        result.Value.CenterX.ShouldBe(250);
        result.Value.CenterY.ShouldBe(400);
        result.Value.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void WithPadding_Adds10PercentOnEachSide()
    {
        var box = new BoundingBox(100, 200, 400, 600);

        var padded = BoundingBoxCalculator.WithPadding(box, 0.1);

        // Width=300, 10% = 30 on each side
        padded.MinX.ShouldBe(70);
        padded.MaxX.ShouldBe(430);
        // Height=400, 10% = 40 on each side
        padded.MinY.ShouldBe(160);
        padded.MaxY.ShouldBe(640);
    }

    [Fact]
    public void WithPadding_ZeroPadding_ReturnsSameBox()
    {
        var box = new BoundingBox(10, 20, 30, 40);

        var padded = BoundingBoxCalculator.WithPadding(box, 0.0);

        padded.MinX.ShouldBe(box.MinX);
        padded.MinY.ShouldBe(box.MinY);
        padded.MaxX.ShouldBe(box.MaxX);
        padded.MaxY.ShouldBe(box.MaxY);
    }

    [Fact]
    public void CalculateZoomToFit_SquareBoxInSquareViewport_ZoomIs1()
    {
        var box = new BoundingBox(0, 0, 1000, 1000);

        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(
            box, 1000, 1000);

        zoom.ShouldBe(1.0);
        panX.ShouldBe(0, 0.001);
        panY.ShouldBe(0, 0.001);
    }

    [Fact]
    public void CalculateZoomToFit_WideBoxInSquareViewport_FitsWidth()
    {
        // Box is 2000 wide, 1000 tall. Viewport is 1000x1000.
        // zoomX = 1000/2000 = 0.5, zoomY = 1000/1000 = 1.0
        // zoom = min(0.5, 1.0) = 0.5
        var box = new BoundingBox(0, 0, 2000, 1000);

        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(
            box, 1000, 1000);

        zoom.ShouldBe(0.5);
    }

    [Fact]
    public void CalculateZoomToFit_TallBoxInSquareViewport_FitsHeight()
    {
        var box = new BoundingBox(0, 0, 1000, 2000);

        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(
            box, 1000, 1000);

        zoom.ShouldBe(0.5);
    }

    [Fact]
    public void CalculateZoomToFit_CentersContent()
    {
        // Box at (100,100)-(300,300), center=(200,200)
        // Viewport 800x800, zoom should be 800/200=4.0
        var box = new BoundingBox(100, 100, 300, 300);

        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(
            box, 800, 800);

        zoom.ShouldBe(4.0);
        // panX = 800/2 - 200*4 = 400 - 800 = -400
        panX.ShouldBe(-400, 0.001);
        panY.ShouldBe(-400, 0.001);
    }

    [Fact]
    public void CalculateZoomToFit_ClampsToMaxZoom()
    {
        // Tiny box: 1x1. Viewport 10000x10000. zoom=10000 but max=10
        var box = new BoundingBox(0, 0, 1, 1);

        var (zoom, _, _) = BoundingBoxCalculator.CalculateZoomToFit(
            box, 10000, 10000, maxZoom: 10.0);

        zoom.ShouldBe(10.0);
    }

    [Fact]
    public void CalculateZoomToFit_ClampsToMinZoom()
    {
        // Huge box: 1000000x1000000. Viewport 100x100. zoom=0.0001 but min=0.1
        var box = new BoundingBox(0, 0, 1000000, 1000000);

        var (zoom, _, _) = BoundingBoxCalculator.CalculateZoomToFit(
            box, 100, 100, minZoom: 0.1);

        zoom.ShouldBe(0.1);
    }

    [Fact]
    public void BoundingBox_IsEmpty_TrueForZeroDimension()
    {
        var box = new BoundingBox(10, 10, 10, 20);
        box.IsEmpty.ShouldBeTrue();
    }

    private static ComponentViewModel CreateComponent(
        double x, double y, double width, double height)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        return new ComponentViewModel(component);
    }
}
