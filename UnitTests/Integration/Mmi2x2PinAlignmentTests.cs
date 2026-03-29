using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.CodeExporter;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Tests for Issue #347: MMI 2x2 waveguide endpoints don't align with component pins.
///
/// Root cause: rotation sign error in GetAbsoluteNazcaPosition() and CalculateOriginOffset().
/// Nazca places cells with .put(x, y, -RotationDegrees), so pin world positions must use
/// R(-RotationDegrees) — not R(+RotationDegrees) as was previously done.
///
/// These tests use the demo_pdk.mmi2x2 component (120×50 µm, NazcaOriginOffset=(0,0))
/// at all 4 rotation states to catch the specific bug reported by the user.
/// </summary>
public class Mmi2x2PinAlignmentTests
{
    private const double PinAlignmentTolerance = 0.01; // µm — tight, 10 nm

    /// <summary>
    /// Creates a ComponentTemplate that matches demo_pdk.mmi2x2 from demo-pdk.json.
    /// Width=120, Height=50, NazcaOriginOffset=(0,0), 4 pins.
    /// </summary>
    private static ComponentTemplate CreateMmi2x2Template() => new()
    {
        Name = "demo_pdk.mmi2x2",
        Category = "Couplers",
        WidthMicrometers = 120,
        HeightMicrometers = 50,
        NazcaOriginOffsetX = 0,
        NazcaOriginOffsetY = 0,
        NazcaFunctionName = "demo_pdk.mmi2x2",
        PinDefinitions = new[]
        {
            new CAP.Avalonia.ViewModels.Library.PinDefinition("a0", 0,   12.5, 180),
            new CAP.Avalonia.ViewModels.Library.PinDefinition("a1", 0,   37.5, 180),
            new CAP.Avalonia.ViewModels.Library.PinDefinition("b0", 120, 12.5, 0),
            new CAP.Avalonia.ViewModels.Library.PinDefinition("b1", 120, 37.5, 0)
        },
        CreateSMatrix = pins =>
        {
            var sMatrix = new CAP_Core.LightCalculation.SMatrix(
                pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList(), new());
            return sMatrix;
        }
    };

    /// <summary>
    /// Verifies MMI 2x2 pin world positions match the Nazca math for all 4 rotations.
    ///
    /// For a component at (physX, physY) with NazcaOriginOffset=(0,0):
    ///   cellX = physX, cellY = -physY
    ///   pinLocalX = pinOffsetX, pinLocalY = height - pinOffsetY
    ///   worldPinX = cellX + R(-rot) * (localX, localY).x
    ///   worldPinY = cellY + R(-rot) * (localX, localY).y
    /// </summary>
    [Theory]
    [InlineData(0,   100, 200)]
    [InlineData(90,  100, 200)]
    [InlineData(180, 100, 200)]
    [InlineData(270, 100, 200)]
    [InlineData(0,   0,   0)]
    [InlineData(90,  0,   0)]
    public void Mmi2x2_AllRotations_PinPositionsMatchNazcaMath(
        int rotationDegrees, double physX, double physY)
    {
        var template = CreateMmi2x2Template();
        var mmi = ComponentTemplates.CreateFromTemplate(template, physX, physY);
        mmi.RotationDegrees = rotationDegrees;

        // NazcaOriginOffset=(0,0) so cell origin is just physX, -physY
        double cellX = physX;
        double cellY = -physY;

        double rotRad = -rotationDegrees * Math.PI / 180.0; // Nazca sign

        foreach (var pin in mmi.PhysicalPins)
        {
            double localX = pin.OffsetXMicrometers;
            double localY = mmi.HeightMicrometers - pin.OffsetYMicrometers;

            double expectedX = cellX + localX * Math.Cos(rotRad) - localY * Math.Sin(rotRad);
            double expectedY = cellY + localX * Math.Sin(rotRad) + localY * Math.Cos(rotRad);

            var (actualX, actualY) = pin.GetAbsoluteNazcaPosition();

            double xDev = Math.Abs(expectedX - actualX);
            double yDev = Math.Abs(expectedY - actualY);

            xDev.ShouldBeLessThan(PinAlignmentTolerance,
                $"MMI 2x2 pin '{pin.Name}' at rot={rotationDegrees}°: X deviation {xDev:F4} µm " +
                $"(expected {expectedX:F3}, got {actualX:F3})");
            yDev.ShouldBeLessThan(PinAlignmentTolerance,
                $"MMI 2x2 pin '{pin.Name}' at rot={rotationDegrees}°: Y deviation {yDev:F4} µm " +
                $"(expected {expectedY:F3}, got {actualY:F3})");
        }
    }

    /// <summary>
    /// Verifies that waveguide start coordinates in the exported Nazca script
    /// match the MMI 2x2 pin world positions at rotation=0° (baseline check).
    /// </summary>
    [Fact]
    public void Mmi2x2_Rotation0_WaveguideStartMatchesPinPosition()
    {
        var canvas = new DesignCanvasViewModel();
        var template = CreateMmi2x2Template();

        var mmi = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        mmi.RotationDegrees = 0;
        canvas.AddComponent(mmi, template.Name);

        var gcTemplate = new ComponentTemplate
        {
            Name = "GC_test",
            Category = "I/O",
            WidthMicrometers = 100,
            HeightMicrometers = 19,
            NazcaOriginOffsetX = 0,
            NazcaOriginOffsetY = 9.5,
            NazcaFunctionName = "ebeam_gc_wg",
            PinDefinitions = new[] { new CAP.Avalonia.ViewModels.Library.PinDefinition("waveguide", 0, 9.5, 180) },
            CreateSMatrix = pins =>
            {
                var sm = new CAP_Core.LightCalculation.SMatrix(
                    pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList(), new());
                return sm;
            }
        };

        var gc = ComponentTemplates.CreateFromTemplate(gcTemplate, 200, 0);
        canvas.AddComponent(gc, gcTemplate.Name);

        // Connect GC waveguide → MMI b0 (right output at y=12.5)
        var startPin = gc.PhysicalPins.First(p => p.Name == "waveguide");
        var endPin = mmi.PhysicalPins.First(p => p.Name == "b0");
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(sx, sy, ex, ey, startPin.GetAbsoluteAngle()));
        canvas.ConnectPinsWithCachedRoute(startPin, endPin, route);

        var exporter = new SimpleNazcaExporter();
        var script = exporter.Export(canvas);
        var parser = new NazcaCodeParser();
        var parsed = parser.Parse(script);

        parsed.WaveguideStubs.Count.ShouldBeGreaterThan(0, "Waveguide must appear in script");

        var wg = parsed.WaveguideStubs.First();
        var (expectedX, expectedY) = startPin.GetAbsoluteNazcaPosition();

        double xDev = Math.Abs(expectedX - wg.StartX);
        double yDev = Math.Abs(expectedY - wg.StartY);

        xDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"Waveguide start X mismatch: expected {expectedX:F3}, got {wg.StartX:F3}, Δ={xDev:F4} µm");
        yDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"Waveguide start Y mismatch: expected {expectedY:F3}, got {wg.StartY:F3}, Δ={yDev:F4} µm");
    }

    /// <summary>
    /// Specific regression test for Issue #347:
    /// MMI 2x2 at rotation 90° — pin a0 was 75 µm off in X with the wrong rotation sign.
    ///
    /// Pin a0 local Nazca: (0, 37.5).
    /// At rotation=90° with correct formula R(-90°):
    ///   worldPinX = physX + 37.5
    ///   worldPinY = -physY + 0
    /// With the bug (R(+90°)):
    ///   worldPinX = physX - 37.5  ← 75 µm error!
    /// </summary>
    [Fact]
    public void Mmi2x2_Rotation90_PinA0_NotShiftedNegatively()
    {
        var template = CreateMmi2x2Template();
        var mmi = ComponentTemplates.CreateFromTemplate(template, 100, 100);
        mmi.RotationDegrees = 90;

        var pinA0 = mmi.PhysicalPins.First(p => p.Name == "a0");
        var (actualX, actualY) = pinA0.GetAbsoluteNazcaPosition();

        // localX=0, localY=37.5; R(-90°): rotatedX=+37.5, rotatedY=0
        double expectedX = 100.0 + 37.5;  // = 137.5
        double expectedY = -100.0 + 0.0;  // = -100.0

        double xDev = Math.Abs(expectedX - actualX);
        double yDev = Math.Abs(expectedY - actualY);

        xDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"MMI 2x2 rot90 a0 X: expected {expectedX:F1}, got {actualX:F1}, Δ={xDev:F4} µm (was -75 µm off with bug)");
        yDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"MMI 2x2 rot90 a0 Y: expected {expectedY:F1}, got {actualY:F1}, Δ={yDev:F4} µm");
    }
}
