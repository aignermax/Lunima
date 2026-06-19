using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.CodeExporter;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// MMI 2x2 pin/waveguide alignment (issues #347, #565).
///
/// Pin Nazca positions are the plain Y negation of the app world position
/// (<see cref="CAP_Core.Export.NazcaCoordinateMapper.GetPinNazcaPosition"/>): the app
/// pre-rotates pin offsets via RotateComponentCommand, so no rotation term may appear
/// in the pin formula. These tests drive the REAL rotate command and compare against
/// hand-derived offsets, so a sign error in the command or a reintroduced rotation
/// term in the pin math both surface as tens-of-µm deviations.
///
/// Fixture: demo_pdk.mmi2x2 (120×50 µm, NazcaOriginOffset=(0,0), 4 pins).
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
    /// Verifies pin Nazca world positions for all 4 app rotation states, rotating via the
    /// real <see cref="RotateComponentCommand"/>.
    ///
    /// One CCW app rotation step maps an offset (ox, oy) in a w×h box to (h − oy, ox)
    /// and swaps the dimensions; Nazca position = (physX + ox, −(physY + oy)).
    /// Hand-derived offsets for the 120×50 box:
    ///   a0 (0, 12.5):  k=1 → (37.5, 0);   k=2 → (120, 37.5); k=3 → (12.5, 120)
    ///   b1 (120, 37.5): k=1 → (12.5, 120); k=2 → (0, 12.5);   k=3 → (37.5, 0)
    /// </summary>
    [Theory]
    [InlineData(0, 0,    12.5,  120,  37.5)]
    [InlineData(1, 37.5, 0,     12.5, 120)]
    [InlineData(2, 120,  37.5,  0,    12.5)]
    [InlineData(3, 12.5, 120,   37.5, 0)]
    public void Mmi2x2_AllRotations_PinPositionsAreYNegatedAppPositions(
        int rotationSteps, double a0OffX, double a0OffY, double b1OffX, double b1OffY)
    {
        const double physX = 100;
        const double physY = 200;
        var canvas = new DesignCanvasViewModel();
        var template = CreateMmi2x2Template();
        var mmi = ComponentTemplates.CreateFromTemplate(template, physX, physY);
        var vm = canvas.AddComponent(mmi, template.Name);

        for (int i = 0; i < rotationSteps; i++)
        {
            var command = new RotateComponentCommand(canvas, vm);
            command.Execute();
            command.WasApplied.ShouldBeTrue($"rotation step {i + 1} must not be blocked");
        }

        AssertPinNazcaPosition(mmi, "a0", physX + a0OffX, -(physY + a0OffY), rotationSteps);
        AssertPinNazcaPosition(mmi, "b1", physX + b1OffX, -(physY + b1OffY), rotationSteps);
    }

    private static void AssertPinNazcaPosition(
        CAP_Core.Components.Core.Component comp, string pinName,
        double expectedX, double expectedY, int rotationSteps)
    {
        var pin = comp.PhysicalPins.First(p => p.Name == pinName);
        var (actualX, actualY) = pin.GetAbsoluteNazcaPosition();

        Math.Abs(expectedX - actualX).ShouldBeLessThan(PinAlignmentTolerance,
            $"MMI 2x2 pin '{pinName}' after {rotationSteps}×90°: " +
            $"X expected {expectedX:F3}, got {actualX:F3}");
        Math.Abs(expectedY - actualY).ShouldBeLessThan(PinAlignmentTolerance,
            $"MMI 2x2 pin '{pinName}' after {rotationSteps}×90°: " +
            $"Y expected {expectedY:F3}, got {actualY:F3}");
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
    /// Regression guard for Issue #347 (pin a0 ended up 75 µm off in X after one rotation):
    /// a 90° CCW rotation of the 120×50 box maps a0's offset (0, 12.5) to (37.5, 0),
    /// so its Nazca position must be (physX + 37.5, −physY). A wrong rotation sign in
    /// the command (or a rotation term sneaking back into the pin formula) shifts X
    /// by tens of µm and fails the 10 nm tolerance.
    /// </summary>
    [Fact]
    public void Mmi2x2_Rotation90_PinA0_NotShiftedNegatively()
    {
        var canvas = new DesignCanvasViewModel();
        var template = CreateMmi2x2Template();
        var mmi = ComponentTemplates.CreateFromTemplate(template, 100, 100);
        var vm = canvas.AddComponent(mmi, template.Name);

        var command = new RotateComponentCommand(canvas, vm);
        command.Execute();
        command.WasApplied.ShouldBeTrue("rotation must not be blocked");

        AssertPinNazcaPosition(mmi, "a0", 100.0 + 37.5, -100.0, rotationSteps: 1);
    }
}
