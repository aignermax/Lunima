using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
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
    public void FormatSegment_StraightSegment_FirstHasCoordinates()
    {
        var segment = new StraightSegment(0, 0, 100, 0, 0);

        var result = SimpleNazcaExporter.FormatSegment(segment, isFirst: true);

        result.ShouldContain("nd.strt(length=100.00)");
        result.ShouldContain(".put(0.00, 0.00, 0.00)");
    }

    [Fact]
    public void FormatSegment_StraightSegment_ChainedHasEmptyPut()
    {
        var segment = new StraightSegment(100, 0, 200, 0, 0);

        var result = SimpleNazcaExporter.FormatSegment(segment, isFirst: false);

        result.ShouldContain("nd.strt(length=100.00)");
        result.ShouldContain(".put()");
        result.ShouldNotContain(".put(100");
    }

    [Fact]
    public void FormatSegment_BendSegment_FirstHasCoordinates()
    {
        // Sweep angle 90 → negated to -90 for Y-axis flip
        var segment = new BendSegment(50, 0, 50, 0, 90);

        var result = SimpleNazcaExporter.FormatSegment(segment, isFirst: true);

        result.ShouldContain("nd.bend(radius=50.00, angle=-90.00)");
        result.ShouldContain(".put(");
    }

    [Fact]
    public void FormatSegment_BendSegment_ChainedHasEmptyPut()
    {
        // Sweep angle 90 → negated to -90 for Y-axis flip
        var segment = new BendSegment(50, 0, 50, 0, 90);

        var result = SimpleNazcaExporter.FormatSegment(segment, isFirst: false);

        result.ShouldContain("nd.bend(radius=50.00, angle=-90.00)");
        result.ShouldEndWith(".put()");
    }

    [Fact]
    public void FormatSegment_NegativeSweepAngle_GetsNegated()
    {
        // -90 sweep → negated to +90 for Y-axis flip
        var segment = new BendSegment(50, 0, 25, 180, -90);

        var result = SimpleNazcaExporter.FormatSegment(segment, isFirst: true);

        result.ShouldContain("angle=90.00");
        result.ShouldContain("radius=25.00");
    }

    [Fact]
    public void AppendSegmentExport_MixedSegments_FirstHasCoordsRestChained()
    {
        var segments = new List<PathSegment>
        {
            new StraightSegment(0, 0, 50, 0, 0),
            new BendSegment(50, 50, 50, 0, 90),
            new StraightSegment(50, 100, 50, 200, 90)
        };
        var sb = new System.Text.StringBuilder();

        SimpleNazcaExporter.AppendSegmentExport(sb, segments);
        var result = sb.ToString();

        // First segment should have coordinates (Y=0 negated is still 0)
        result.ShouldContain("nd.strt(length=");
        result.ShouldContain(".put(0.00, 0.00, 0.00)");

        // Subsequent segments should chain
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(3);

        // Line 0: first segment with coords
        lines[0].ShouldContain(".put(0.00, 0.00, 0.00)");

        // Lines 1 & 2: chained with empty .put()
        lines[1].Trim().ShouldEndWith(".put()");
        lines[2].Trim().ShouldEndWith(".put()");
    }

    [Fact]
    public void AppendSegmentExport_SingleSegment_HasCoordinates()
    {
        // Y=20 → negated to -20
        var segments = new List<PathSegment>
        {
            new StraightSegment(10, 20, 110, 20, 0)
        };
        var sb = new System.Text.StringBuilder();

        SimpleNazcaExporter.AppendSegmentExport(sb, segments);
        var result = sb.ToString();

        result.ShouldContain(".put(10.00, -20.00, 0.00)");
    }

    [Fact]
    public void GetNazcaFunction_GratingCoupler_ReturnsGrating()
    {
        var comp = CreateComponentWithName("grating_coupler");
        var result = SimpleNazcaExporter.GetNazcaFunction(comp);
        result.ShouldBe("demo.io()");
    }

    [Fact]
    public void GetNazcaFunction_DirectionalCoupler_ReturnsMmi2x2()
    {
        var comp = CreateComponentWithName("directional_coupler");
        var result = SimpleNazcaExporter.GetNazcaFunction(comp);
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
    public void FormatSegment_StraightWithAngle_IncludesNegatedAngle()
    {
        // angle=45 → negated to -45, Y=20 → negated to -20
        var angle = 45.0;
        var segment = new StraightSegment(10, 20, 80.71, 90.71, angle);

        var result = SimpleNazcaExporter.FormatSegment(segment, isFirst: true);

        result.ShouldContain(".put(10.00, -20.00, -45.00)");
    }

    [Fact]
    public void FormatSegment_DefaultIsFirst_HasCoordinates()
    {
        // Y=10 → negated to -10
        var segment = new StraightSegment(5, 10, 55, 10, 0);

        var result = SimpleNazcaExporter.FormatSegment(segment);

        result.ShouldContain(".put(5.00, -10.00, 0.00)");
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

    [Fact]
    public void IsPdkFunction_RealPdkFunction_ReturnsTrue()
    {
        var result = SimpleNazcaExporter.IsPdkFunction("ebeam_y_1550");
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsPdkFunction_DemoPdkFunction_ReturnsFalse()
    {
        var result = SimpleNazcaExporter.IsPdkFunction("demo_pdk.mmi1x2");
        result.ShouldBeFalse();
    }

    [Fact]
    public void IsPdkFunction_ExternalPdkWithDot_ReturnsTrue()
    {
        var result = SimpleNazcaExporter.IsPdkFunction("siepic.gc_te1550");
        result.ShouldBeTrue();
    }

    [Fact]
    public void GetNazcaFunction_DemoPdkStraightWithLength100_ReturnsCorrectCall()
    {
        var comp = CreateComponentWithName("demo_pdk.straight");
        comp.NazcaFunctionParameters = "length=100";
        comp.WidthMicrometers = 100;
        comp.HeightMicrometers = 10;

        var result = SimpleNazcaExporter.GetNazcaFunction(comp);

        result.ShouldBe("demo_pdk_straight(length=100)");
    }

    [Fact]
    public void GetNazcaFunction_DemoPdkStraightWithoutParams_UsesComponentWidth()
    {
        var comp = CreateComponentWithName("straight_waveguide");
        comp.WidthMicrometers = 150;
        comp.HeightMicrometers = 10;

        var result = SimpleNazcaExporter.GetNazcaFunction(comp);

        result.ShouldBe("demo.shallow.strt(length=150)");
    }

    [Fact]
    public void GetNazcaFunction_UnknownComponent_UsesWidth()
    {
        var comp = CreateComponentWithName("unknown_component");
        comp.WidthMicrometers = 75;
        comp.HeightMicrometers = 25;

        var result = SimpleNazcaExporter.GetNazcaFunction(comp);

        result.ShouldBe("demo.shallow.strt(length=75)");
    }

    [Fact]
    public void GetNazcaFunction_RealPdkFunction_IncludesParameters()
    {
        var comp = CreateComponentWithName("ebeam_y_1550");
        comp.NazcaFunctionParameters = "wg_width=0.5";

        var result = SimpleNazcaExporter.GetNazcaFunction(comp);

        result.ShouldBe("ebeam_y_1550(wg_width=0.5)");
    }

    [Fact]
    public void Export_StraightWaveguide100um_ExportsCorrectLength()
    {
        // Arrange: Create a canvas with a 100µm straight waveguide
        var canvas = new DesignCanvasViewModel();
        var comp = CreateDemoPdkStraightWaveguide(100);
        var compVm = new ComponentViewModel(comp);
        canvas.Components.Add(compVm);

        // Act: Export to Nazca Python
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert: Verify that the exported code includes the correct length parameter
        result.ShouldContain("demo_pdk_straight(length=100)");

        // Verify stub function is parametric
        result.ShouldContain("def demo_pdk_straight(length=100, **kwargs):");
        result.ShouldContain("nd.strt(length=length");

        // Verify component placement uses the stub
        result.ShouldContain("comp_0 = demo_pdk_straight(length=100).put(");
    }

    [Fact]
    public void Export_StraightWaveguide200um_ExportsCorrectLength()
    {
        // Arrange: Create a canvas with a 200µm straight waveguide
        var canvas = new DesignCanvasViewModel();
        var comp = CreateDemoPdkStraightWaveguide(200);
        var compVm = new ComponentViewModel(comp);
        canvas.Components.Add(compVm);

        // Act: Export to Nazca Python
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert: Verify correct length
        result.ShouldContain("demo_pdk_straight(length=200)");
        result.ShouldContain("comp_0 = demo_pdk_straight(length=200).put(");
    }

    private static Component CreateDemoPdkStraightWaveguide(double lengthMicrometers)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "demo_pdk.straight",
            nazcaFunctionParams: $"length={lengthMicrometers}",
            parts: parts,
            typeNumber: 0,
            identifier: $"Straight Waveguide {lengthMicrometers}µm",
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = lengthMicrometers;
        component.HeightMicrometers = 10;
        component.PhysicalX = 0;
        component.PhysicalY = 0;

        // Add physical pins (input at x=0, output at x=length)
        var inputPin = new PhysicalPin
        {
            Name = "a0",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 5,
            AngleDegrees = 180,
            ParentComponent = component
        };

        var outputPin = new PhysicalPin
        {
            Name = "b0",
            OffsetXMicrometers = lengthMicrometers,
            OffsetYMicrometers = 5,
            AngleDegrees = 0,
            ParentComponent = component
        };

        component.PhysicalPins.Add(inputPin);
        component.PhysicalPins.Add(outputPin);

        return component;
    }
}
