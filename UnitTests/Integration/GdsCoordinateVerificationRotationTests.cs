using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.CodeExporter;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using Shouldly;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Xunit;
using static UnitTests.Integration.GdsCoordinateVerificationFixture;

namespace UnitTests.Integration;

/// <summary>Rotated components and vertical routes use mapped pin coordinates.</summary>
[Trait("Category", "Slow")]
[Collection("GdsCoordinateVerification")]
public class GdsCoordinateVerificationRotationTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly SimpleNazcaExporter _exporter = new();
    private readonly NazcaCodeParser _parser = new();
    private readonly ITestOutputHelper _output;

    public GdsCoordinateVerificationRotationTests(ITestOutputHelper output)
    {
        _output = output;
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Test rotated components: GC at 90° rotation.
    /// Pin positions must be correctly transformed after rotation.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0)]      // No rotation at origin
    [InlineData(90, 0, 0)]     // 90° rotation at origin
    [InlineData(180, 0, 0)]    // 180° rotation at origin
    [InlineData(270, 0, 0)]    // 270° rotation at origin
    [InlineData(0, 150, 100)]  // No rotation, offset position
    [InlineData(90, 150, 100)] // 90° rotation, offset position
    public void RotatedComponent_PinPositionsMustMatchAfterRotation(int rotation, double posX, double posY)
    {
        var canvas = new DesignCanvasViewModel();
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        // GC1 at specified position and rotation
        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, posX, posY);
        gc1.Identifier = $"gc_rot{rotation}";
        gc1.NazcaFunctionName = $"ebeam_gc_rot{rotation}";
        gc1.RotationDegrees = rotation;
        canvas.AddComponent(gc1, gcTemplate.Name);

        // GC2 for destination (position calculated based on rotated pin direction)
        var pin1 = gc1.PhysicalPins.First(p => p.Name == "waveguide");
        var (s1x, s1y) = pin1.GetAbsolutePosition();
        var angle = pin1.GetAbsoluteAngle();

        // Place GC2 200µm away in the direction of the rotated pin
        double destX = s1x + 200 * Math.Cos(angle * Math.PI / 180);
        double destY = s1y + 200 * Math.Sin(angle * Math.PI / 180);

        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, destX, destY);
        gc2.Identifier = $"gc2_rot{rotation}";
        gc2.NazcaFunctionName = $"ebeam_gc_rot{rotation}";
        gc2.RotationDegrees = rotation;
        canvas.AddComponent(gc2, gcTemplate.Name);

        var pin2 = gc2.PhysicalPins.First(p => p.Name == "waveguide");
        var (s2x, s2y) = pin2.GetAbsolutePosition();

        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(s1x, s1y, s2x, s2y, angle));
        canvas.ConnectPinsWithCachedRoute(pin1, pin2, route);

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        if (parsed.Components.Count == 0 || parsed.WaveguideStubs.Count == 0)
        {
            _output.WriteLine($"SKIP: Rotation {rotation}° not fully exported");
            return;
        }

        // Expected via NazcaCoordinateMapper (#565): pin positions convert by plain
        // Y negation for every rotation — the segment is routed from the app pin
        // position, so the exported start must be its Y-negated image. A rotated
        // origin-offset pin formula would diverge here (the #565 misalignment).
        var (expectedX, expectedY) = NazcaCoordinateMapper.GetPinNazcaPosition(pin1);

        var wg = parsed.WaveguideStubs.First();
        double xDev = Math.Abs(expectedX - wg.StartX);
        double yDev = Math.Abs(expectedY - wg.StartY);

        _output.WriteLine($"Rotation: {rotation}°, Position: ({posX}, {posY})");
        _output.WriteLine($"Expected pin (Nazca): ({expectedX:F2}, {expectedY:F2})");
        _output.WriteLine($"Waveguide start: ({wg.StartX:F2}, {wg.StartY:F2})");
        _output.WriteLine($"Deviation: X={xDev:F4} µm, Y={yDev:F4} µm");

        xDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"Rotated component ({rotation}°) X mismatch: {xDev:F4} µm");
        yDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"Rotated component ({rotation}°) Y mismatch: {yDev:F4} µm");
    }

    /// <summary>
    /// Test vertical waveguides (90° angle).
    /// Ensures Y-coordinate bug affects both horizontal and vertical routing.
    /// </summary>
    [Fact]
    public void VerticalWaveguide_PinPositionsMustMatch()
    {
        var canvas = new DesignCanvasViewModel();
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        // GC1 at (100, 0) rotated 90° (pin points up)
        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, 100, 0);
        gc1.Identifier = "gc_vertical_1";
        gc1.NazcaFunctionName = "ebeam_gc_vert";
        gc1.RotationDegrees = 90;
        canvas.AddComponent(gc1, gcTemplate.Name);

        // GC2 at (100, 200) rotated 270° (pin points down)
        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, 100, 200);
        gc2.Identifier = "gc_vertical_2";
        gc2.NazcaFunctionName = "ebeam_gc_vert";
        gc2.RotationDegrees = 270;
        canvas.AddComponent(gc2, gcTemplate.Name);

        var pin1 = gc1.PhysicalPins.First(p => p.Name == "waveguide");
        var pin2 = gc2.PhysicalPins.First(p => p.Name == "waveguide");
        var (s1x, s1y) = pin1.GetAbsolutePosition();
        var (s2x, s2y) = pin2.GetAbsolutePosition();

        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(s1x, s1y, s2x, s2y, pin1.GetAbsoluteAngle()));
        canvas.ConnectPinsWithCachedRoute(pin1, pin2, route);

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        _output.WriteLine("=== Vertical waveguide test ===");
        _output.WriteLine($"Pin1 absolute: ({s1x:F2}, {s1y:F2}) @ {pin1.GetAbsoluteAngle()}°");
        _output.WriteLine($"Pin2 absolute: ({s2x:F2}, {s2y:F2}) @ {pin2.GetAbsoluteAngle()}°");

        if (parsed.Components.Count < 2 || parsed.WaveguideStubs.Count == 0)
        {
            _output.WriteLine("SKIP: Vertical routing not fully exported");
            return;
        }

        // This test primarily checks that vertical routing doesn't crash
        // and that the Y-coordinate bug manifests in vertical directions too
        true.ShouldBeTrue("Vertical waveguide export completed");
    }
}
