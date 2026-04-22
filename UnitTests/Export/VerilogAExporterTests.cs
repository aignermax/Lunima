using CAP_Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Unit tests for VerilogAExporter — verifies Verilog-A file generation from photonic designs.
/// </summary>
public class VerilogAExporterTests
{
    private readonly VerilogAExporter _exporter = new();

    [Fact]
    public void Export_EmptyComponentList_ReturnsFailed()
    {
        var result = _exporter.Export(new List<Component>(), new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Export_ComponentWithNoPhysicalPins_ReturnsFailureWithActionableMessage()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();  // bare — no pins added
        comp.PhysicalPins.Clear();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("no physical ports");
    }

    [Fact]
    public void Export_OrphanConnectionPin_ReturnsFailureWithActionableMessage()
    {
        // A connection whose start pin doesn't belong to any exported component.
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var orphan = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var conn = new WaveguideConnection
        {
            StartPin = orphan.PhysicalPins[0], // <- orphan, not in [comp]
            EndPin = comp.PhysicalPins[0]
        };

        var result = _exporter.Export(new[] { comp }, new[] { conn },
            new VerilogAExportOptions());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("orphan", Case.Insensitive);
    }

    [Fact]
    public void Export_SingleTwoPortComponent_GeneratesComponentFile()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { WavelengthNm = 1550 });

        result.Success.ShouldBeTrue();
        result.ComponentFiles.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Export_SingleTwoPortComponent_TopLevelNetlistIsGenerated()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { CircuitName = "MyCircuit" });

        result.Success.ShouldBeTrue();
        result.TopLevelNetlist.ShouldContain("module MyCircuit");
    }

    [Fact]
    public void Export_WithTestBench_GeneratesSpiceTestBench()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { IncludeTestBench = true });

        result.Success.ShouldBeTrue();
        result.SpiceTestBench.ShouldNotBeNullOrEmpty();
        result.SpiceTestBench.ShouldContain(".op");
    }

    [Fact]
    public void Export_WithoutTestBench_SpiceTestBenchIsEmpty()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { IncludeTestBench = false });

        result.Success.ShouldBeTrue();
        result.SpiceTestBench.ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Export_TwoConnectedComponents_InternalNodesMerged()
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp1.PhysicalX = 0;

        var comp2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp2.PhysicalX = 300;

        var connection = new WaveguideConnection
        {
            StartPin = comp1.PhysicalPins[1],  // comp1 output
            EndPin = comp2.PhysicalPins[0]     // comp2 input
        };

        var result = _exporter.Export(new[] { comp1, comp2 }, new[] { connection },
            new VerilogAExportOptions { CircuitName = "TwoComp" });

        result.Success.ShouldBeTrue();
        // Top-level netlist should have both component instances
        result.TopLevelNetlist.ShouldContain("inst_0");
        result.TopLevelNetlist.ShouldContain("inst_1");
    }

    [Fact]
    public void Export_TwoPortWaveguide_ModuleContainsPortDeclarations()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { WavelengthNm = 1550 });
        var module = result.ComponentFiles.Values.First();

        module.ShouldContain("module ");
        module.ShouldContain("electrical port0");
        module.ShouldContain("electrical port1");
        module.ShouldContain("endmodule");
    }

    [Fact]
    public void Export_TwoPortWaveguide_ModuleContainsSParameters()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());
        var module = result.ComponentFiles.Values.First();

        module.ShouldContain("s11_mag");
        module.ShouldContain("s12_mag");
        module.ShouldContain("s21_mag");
        module.ShouldContain("s22_mag");
    }

    [Fact]
    public void Export_HeuristicFallback_EmitsLoudWarningComment()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        // Wavelength not in the SMatrix map → heuristic path
        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { WavelengthNm = 9999 });
        var module = result.ComponentFiles.Values.First();

        module.ShouldContain("WARNING: No S-matrix", Case.Insensitive);
        module.ShouldContain("heuristic", Case.Insensitive);
    }

    [Fact]
    public void Export_SpecialCharsInComponentName_SanitizedInModuleName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp.NazcaFunctionName = "ebeam.y-1550";

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.ComponentFiles.Keys.ShouldContain("ebeam_y_1550.va");
    }

    [Fact]
    public void Export_TotalFileCount_MatchesExpectedCount()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { IncludeTestBench = true });

        // 1 component file + 1 netlist + 1 test bench = 3
        result.TotalFileCount.ShouldBe(3);
    }

    [Fact]
    public void Export_DuplicateComponentTypes_GeneratesSingleModuleFile()
    {
        // Two identical component types should produce one .va module, not two
        var comp1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var comp2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp1, comp2 }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.Success.ShouldBeTrue();
        // Only one component file since both have the same NazcaFunctionName
        result.ComponentFiles.Count.ShouldBe(1);
    }
}
