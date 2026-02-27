using CAP.Avalonia.Services;
using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Tests for the SimpleNazcaExporter segment export and component mapping.
/// </summary>
public class SimpleNazcaExporterTests
{
    [Fact]
    public void FormatSegment_StraightSegment_OutputsNdStrt()
    {
        // Arrange
        var segment = new StraightSegment(0, 0, 100, 0, 0);

        // Act
        var result = SimpleNazcaExporter.FormatSegment(segment);

        // Assert
        result.ShouldContain("nd.strt(length=100.00)");
        result.ShouldContain(".put(0.00, 0.00, 0.00)");
    }

    [Fact]
    public void FormatSegment_BendSegment_OutputsNdBend()
    {
        // Arrange
        var segment = new BendSegment(50, 0, 50, 0, 90);

        // Act
        var result = SimpleNazcaExporter.FormatSegment(segment);

        // Assert
        result.ShouldContain("nd.bend(radius=50.00, angle=90.00)");
        result.ShouldContain(".put(");
    }

    [Fact]
    public void FormatSegment_NegativeSweepAngle_PreservesSign()
    {
        // Arrange
        var segment = new BendSegment(50, 0, 25, 180, -90);

        // Act
        var result = SimpleNazcaExporter.FormatSegment(segment);

        // Assert
        result.ShouldContain("angle=-90.00");
        result.ShouldContain("radius=25.00");
    }

    [Fact]
    public void AppendSegmentExport_MixedSegments_OutputsAllSegments()
    {
        // Arrange
        var segments = new List<PathSegment>
        {
            new StraightSegment(0, 0, 50, 0, 0),
            new BendSegment(50, 50, 50, 0, 90),
            new StraightSegment(50, 100, 50, 200, 90)
        };
        var sb = new System.Text.StringBuilder();

        // Act
        SimpleNazcaExporter.AppendSegmentExport(sb, segments);
        var result = sb.ToString();

        // Assert
        result.ShouldContain("nd.strt(length=");
        result.ShouldContain("nd.bend(radius=50.00, angle=90.00)");
        var strtCount = CountOccurrences(result, "nd.strt(");
        var bendCount = CountOccurrences(result, "nd.bend(");
        strtCount.ShouldBe(2);
        bendCount.ShouldBe(1);
    }

    [Fact]
    public void GetNazcaFunction_GratingCoupler_ReturnsGrating()
    {
        // Arrange - a component with "grating coupler" in its name
        var comp = CreateComponentWithName("grating_coupler");

        // Act
        var result = SimpleNazcaExporter.GetNazcaFunction(comp);

        // Assert - should map to demo.io(), NOT demo.mmi2x2_dp()
        result.ShouldBe("demo.io()");
    }

    [Fact]
    public void GetNazcaFunction_DirectionalCoupler_ReturnsMmi2x2()
    {
        // Arrange
        var comp = CreateComponentWithName("directional_coupler");

        // Act
        var result = SimpleNazcaExporter.GetNazcaFunction(comp);

        // Assert
        result.ShouldBe("demo.mmi2x2_dp()");
    }

    [Fact]
    public void GetNazcaFunction_Splitter_ReturnsMmi1x2()
    {
        var comp = CreateComponentWithName("splitter_1x2");
        var result = SimpleNazcaExporter.GetNazcaFunction(comp);
        result.ShouldBe("demo.mmi1x2_sh()");
    }

    [Fact]
    public void GetNazcaFunction_PhaseShifter_ReturnsEopm()
    {
        var comp = CreateComponentWithName("phase_shifter");
        var result = SimpleNazcaExporter.GetNazcaFunction(comp);
        result.ShouldBe("demo.eopm_dc(length=500)");
    }

    [Fact]
    public void GetNazcaFunction_Photodetector_ReturnsPd()
    {
        var comp = CreateComponentWithName("photodetector");
        var result = SimpleNazcaExporter.GetNazcaFunction(comp);
        result.ShouldBe("demo.pd()");
    }

    [Fact]
    public void FormatSegment_StraightWithAngle_IncludesAngle()
    {
        // Arrange - angled straight segment
        var angle = 45.0;
        var segment = new StraightSegment(10, 20, 80.71, 90.71, angle);

        // Act
        var result = SimpleNazcaExporter.FormatSegment(segment);

        // Assert
        result.ShouldContain(".put(10.00, 20.00, 45.00)");
    }

    [Fact]
    public void FormatSegment_BendWithStartPoint_UsesCorrectCoordinates()
    {
        // Arrange
        var bend = new BendSegment(100, 0, 50, 0, 90);

        // Act
        var result = SimpleNazcaExporter.FormatSegment(bend);

        // Assert
        result.ShouldContain("nd.bend(radius=50.00, angle=90.00)");
        // Start angle should be 0
        result.ShouldContain(", 0.00)");
    }

    private static Component CreateComponentWithName(string nazcaFunctionName)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: nazcaFunctionName,
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: nazcaFunctionName,
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;

        return component;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
