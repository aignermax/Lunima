using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Tests for Grating Coupler rotation bug (Issue #68).
/// Verifies that component rotation is correctly exported to Nazca Python/GDS.
/// </summary>
public class GratingCouplerRotationTests
{
    /// <summary>
    /// Creates a grating coupler component matching the Demo PDK definition.
    /// </summary>
    private static Component CreateGratingCoupler(double rotationDegrees = 0)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var physicalPins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "opt",
                OffsetXMicrometers = 50,  // Right edge
                OffsetYMicrometers = 15,  // Middle (height=30)
                AngleDegrees = 0          // Pointing right
            }
        };

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "demo_pdk.grating_coupler",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 1,
            identifier: "Grating Coupler",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: physicalPins
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 30;
        component.PhysicalX = 0;
        component.PhysicalY = 0;
        component.RotationDegrees = rotationDegrees;

        return component;
    }

    [Fact]
    public void GratingCoupler_AtZeroRotation_PinAngleIsZero()
    {
        // Arrange
        var component = CreateGratingCoupler(rotationDegrees: 0);
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "Grating Coupler");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert
        // The stub should define the pin with angle -0 = 0 (Y-flip)
        result.ShouldContain("nd.Pin('opt').put(50.00, 15.00, 0)");

        // The component instance should be placed with rotation -0 = 0
        // Placement at (0, -30) because cell origin (0,0) is at top-left in cell-local,
        // and we want it at Nazca world (0, -30) to match editor bbox (0,0)-(50,30)
        result.ShouldContain("demo_pdk_grating_coupler().put(0.00, -30.00, 0)");
    }

    [Fact]
    public void GratingCoupler_At180Rotation_ComponentRotatedCorrectly()
    {
        // Arrange
        var component = CreateGratingCoupler(rotationDegrees: 180);
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "Grating Coupler");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert
        // The stub pin definition should be the same (defined once)
        result.ShouldContain("nd.Pin('opt').put(50.00, 15.00, 0)");

        // The component instance should be rotated: -180 = -180 (or 180)
        // In Nazca, -180 and 180 are equivalent
        result.ShouldMatch(@"demo_pdk_grating_coupler\(\)\.put\([^,]+,\s*[^,]+,\s*-?180\)");
    }

    [Fact]
    public void GratingCoupler_At90Rotation_PinRotatesCorrectly()
    {
        // Arrange
        var component = CreateGratingCoupler(rotationDegrees: 90);
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "Grating Coupler");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert
        // The component should be rotated -90 degrees for Y-axis flip
        result.ShouldContain("demo_pdk_grating_coupler().put(");
        result.ShouldMatch(@"demo_pdk_grating_coupler\(\)\.put\([^,]+,\s*[^,]+,\s*-90\)");
    }

    [Fact]
    public void GratingCoupler_PinStub_HasCorrectYFlip()
    {
        // Arrange
        var component = CreateGratingCoupler();
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "Grating Coupler");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert
        // Pin at (50, 15) in Avalonia (Y-down) should be at (50, 30-15=15) in Nazca (Y-up)
        result.ShouldContain("nd.Pin('opt').put(50.00, 15.00, 0)");
    }

    [Theory]
    [InlineData(0, 0)]      // 0° in Avalonia → 0° in Nazca
    [InlineData(90, -90)]   // 90° CCW in Avalonia (Y-down) → -90° in Nazca (Y-up)
    [InlineData(180, -180)] // 180° in Avalonia → -180° in Nazca
    [InlineData(270, -270)] // 270° in Avalonia → -270° (or 90°) in Nazca
    public void ComponentRotation_TransformsCorrectlyForYAxisFlip(
        double avaloniaRotation, double expectedNazcaRotation)
    {
        // Arrange
        var component = CreateGratingCoupler(avaloniaRotation);
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "Test");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert
        // Allow for angle normalization (e.g., -270° = 90°)
        var normalizedExpected = expectedNazcaRotation;
        while (normalizedExpected < -180) normalizedExpected += 360;
        while (normalizedExpected > 180) normalizedExpected -= 360;

        // Accept either the raw angle or the normalized angle
        var pattern1 = $@"\.put\([^,]+,\s*[^,]+,\s*{expectedNazcaRotation:F0}\)";
        var pattern2 = $@"\.put\([^,]+,\s*[^,]+,\s*{normalizedExpected:F0}\)";

        bool matchesEither = System.Text.RegularExpressions.Regex.IsMatch(result, pattern1) ||
                            System.Text.RegularExpressions.Regex.IsMatch(result, pattern2);

        matchesEither.ShouldBeTrue(
            customMessage: $"Expected Nazca rotation {expectedNazcaRotation}° (or normalized {normalizedExpected}°) for Avalonia rotation {avaloniaRotation}°.\nActual output:\n{result}");
    }
}
