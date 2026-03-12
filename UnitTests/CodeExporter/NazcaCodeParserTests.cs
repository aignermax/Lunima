using CAP_Core.CodeExporter;
using Shouldly;
using Xunit;

namespace UnitTests.CodeExporter;

/// <summary>
/// Unit tests for NazcaCodeParser - validates parsing of Nazca Python code.
/// </summary>
public class NazcaCodeParserTests
{
    [Fact]
    public void Parse_SingleComponent_ExtractsPlacement()
    {
        var nazcaCode = @"
            comp_0 = ebeam_mmi_1x2().put(100.00, -50.00, 0)  # MMI_1x2
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.Components.Count.ShouldBe(1);
        result.Components[0].VariableName.ShouldBe("comp_0");
        result.Components[0].FunctionName.ShouldBe("ebeam_mmi_1x2()");
        result.Components[0].X.ShouldBe(100.00);
        result.Components[0].Y.ShouldBe(-50.00);
        result.Components[0].RotationDegrees.ShouldBe(0);
    }

    [Fact]
    public void Parse_MultipleComponents_ExtractsAllPlacements()
    {
        var nazcaCode = @"
            comp_0 = ebeam_mmi_1x2().put(0.00, -25.00, 0)
            comp_1 = ebeam_mmi_2x2().put(150.00, -60.00, 90)
            comp_2 = demo.pd().put(300.00, -25.00, 180)
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.Components.Count.ShouldBe(3);

        result.Components[0].VariableName.ShouldBe("comp_0");
        result.Components[0].X.ShouldBe(0.00);

        result.Components[1].VariableName.ShouldBe("comp_1");
        result.Components[1].X.ShouldBe(150.00);
        result.Components[1].RotationDegrees.ShouldBe(90);

        result.Components[2].VariableName.ShouldBe("comp_2");
        result.Components[2].RotationDegrees.ShouldBe(180);
    }

    [Fact]
    public void Parse_RotatedComponent_ExtractsRotation()
    {
        var nazcaCode = @"
            comp_0 = ebeam_gc_te1550().put(0.00, -10.00, -90)
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.Components.Count.ShouldBe(1);
        result.Components[0].RotationDegrees.ShouldBe(-90);
    }

    [Fact]
    public void Parse_StraightWaveguideStub_ExtractsPosition()
    {
        var nazcaCode = @"
            nd.strt(length=100.00).put(50.00, -25.00, 0.00)
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.WaveguideStubs.Count.ShouldBe(1);
        result.WaveguideStubs[0].Type.ShouldBe("straight");
        result.WaveguideStubs[0].StartX.ShouldBe(50.00);
        result.WaveguideStubs[0].StartY.ShouldBe(-25.00);
        result.WaveguideStubs[0].StartAngle.ShouldBe(0.00);
        result.WaveguideStubs[0].Length.ShouldBe(100.00);
    }

    [Fact]
    public void Parse_BendWaveguideStub_ExtractsRadiusAndSweep()
    {
        var nazcaCode = @"
            nd.bend(radius=50.00, angle=90.00).put(150.00, -25.00, 0.00)
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.WaveguideStubs.Count.ShouldBe(1);
        result.WaveguideStubs[0].Type.ShouldBe("bend");
        result.WaveguideStubs[0].StartX.ShouldBe(150.00);
        result.WaveguideStubs[0].StartY.ShouldBe(-25.00);
        result.WaveguideStubs[0].StartAngle.ShouldBe(0.00);
        result.WaveguideStubs[0].Radius.ShouldBe(50.00);
        result.WaveguideStubs[0].SweepAngle.ShouldBe(90.00);
    }

    [Fact]
    public void Parse_ChainedSegments_ExtractsFirstStubOnly()
    {
        var nazcaCode = @"
            nd.strt(length=100.00).put(50.00, -25.00, 0.00)
            nd.bend(radius=50.00, angle=90.00).put()
            nd.strt(length=150.00).put()
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        // Only first segment with coordinates should be parsed as a stub
        result.WaveguideStubs.Count.ShouldBe(1);
        result.WaveguideStubs[0].StartX.ShouldBe(50.00);
    }

    [Fact]
    public void Parse_PinDefinitions_ExtractsPositionsAndAngles()
    {
        var nazcaCode = @"
            nd.Pin('a0').put(0.00, 37.50, -180)
            nd.Pin('a1').put(0.00, 12.50, -180)
            nd.Pin('b0').put(120.00, 37.50, 0)
            nd.Pin('b1').put(120.00, 12.50, 0)
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.PinDefinitions.Count.ShouldBe(4);

        result.PinDefinitions[0].Name.ShouldBe("a0");
        result.PinDefinitions[0].X.ShouldBe(0.00);
        result.PinDefinitions[0].Y.ShouldBe(37.50);
        result.PinDefinitions[0].AngleDegrees.ShouldBe(-180);

        result.PinDefinitions[2].Name.ShouldBe("b0");
        result.PinDefinitions[2].X.ShouldBe(120.00);
        result.PinDefinitions[2].AngleDegrees.ShouldBe(0);
    }

    [Fact]
    public void Parse_ComplexDesign_ExtractsAllElements()
    {
        var nazcaCode = @"
            import nazca as nd
            import nazca.demofab as demo

            def create_design():
                with nd.Cell(name='ConnectAPIC_Design') as design:
                    # Components
                    comp_0 = ebeam_mmi_1x2().put(0.00, -40.00, 0)
                    comp_1 = ebeam_mmi_2x2().put(150.00, -60.00, 0)

                    # Waveguide Connections
                    nd.strt(length=100.00).put(50.00, -25.00, 0.00)
                    nd.bend(radius=50.00, angle=90.00).put()
                    nd.strt(length=50.00).put()

                return design
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.Components.Count.ShouldBe(2);
        result.WaveguideStubs.Count.ShouldBe(1);
        result.Components[0].FunctionName.ShouldBe("ebeam_mmi_1x2()");
        result.Components[1].FunctionName.ShouldBe("ebeam_mmi_2x2()");
    }

    [Fact]
    public void Parse_NegativeCoordinates_HandlesCorrectly()
    {
        var nazcaCode = @"
            comp_0 = demo.mmi1x2_sh().put(-100.50, -75.25, -45)
            nd.strt(length=50.00).put(-50.00, -25.00, -90.00)
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.Components[0].X.ShouldBe(-100.50);
        result.Components[0].Y.ShouldBe(-75.25);
        result.Components[0].RotationDegrees.ShouldBe(-45);

        result.WaveguideStubs[0].StartX.ShouldBe(-50.00);
        result.WaveguideStubs[0].StartY.ShouldBe(-25.00);
        result.WaveguideStubs[0].StartAngle.ShouldBe(-90.00);
    }

    [Fact]
    public void Parse_ComponentWithParameters_ExtractsFunctionName()
    {
        var nazcaCode = @"
            comp_0 = ebeam_y_1550(params='test').put(0.00, -25.00, 0)
            comp_1 = demo.eopm_dc(length=500).put(100.00, -25.00, 0)
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.Components.Count.ShouldBe(2);
        result.Components[0].FunctionName.ShouldContain("ebeam_y_1550");
        result.Components[1].FunctionName.ShouldContain("demo.eopm_dc");
    }

    [Fact]
    public void Parse_EmptyCode_ReturnsEmptyResult()
    {
        var nazcaCode = "";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.Components.Count.ShouldBe(0);
        result.WaveguideStubs.Count.ShouldBe(0);
        result.PinDefinitions.Count.ShouldBe(0);
    }

    [Fact]
    public void Parse_MultipleWaveguideStubs_ExtractsAll()
    {
        var nazcaCode = @"
            # First waveguide
            nd.strt(length=100.00).put(50.00, -25.00, 0.00)
            nd.bend(radius=50.00, angle=90.00).put()

            # Second waveguide
            nd.strt(length=75.00).put(200.00, -50.00, 90.00)
            nd.strt(length=80.00).put()
        ";

        var parser = new NazcaCodeParser();
        var result = parser.Parse(nazcaCode);

        result.WaveguideStubs.Count.ShouldBe(2);
        result.WaveguideStubs[0].StartX.ShouldBe(50.00);
        result.WaveguideStubs[0].Length.ShouldBe(100.00);
        result.WaveguideStubs[1].StartX.ShouldBe(200.00);
        result.WaveguideStubs[1].StartAngle.ShouldBe(90.00);
    }
}
