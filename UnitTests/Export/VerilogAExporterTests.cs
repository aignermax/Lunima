using System.Numerics;
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
            new VerilogAExportOptions { IncludeTestBench = true, CircuitName = "MyCircuit" });

        result.Success.ShouldBeTrue();
        result.SpiceTestBench.ShouldNotBeNullOrEmpty();
        // NGSpice-OSDI flow: one pre_osdi per component module inside a .control
        // block, N-elements instantiate the compact models directly (OpenVAF can't
        // compile the hierarchical top-level .va, so we don't reference it).
        result.SpiceTestBench.ShouldContain("pre_osdi placeCell_StraightWG.osdi");
        result.SpiceTestBench.ShouldContain(".control");
        result.SpiceTestBench.ShouldContain(".endc");
        result.SpiceTestBench.ShouldContain("N_inst0 ");
        result.SpiceTestBench.ShouldContain(".model placeCell_StraightWG_mod placeCell_StraightWG");
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
        module.ShouldContain("port0_re");
        module.ShouldContain("port0_im");
        module.ShouldContain("port1_re");
        module.ShouldContain("port1_im");
        module.ShouldContain("endmodule");
    }

    [Fact]
    public void Export_TwoPortWaveguide_ModuleContainsSParameters()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());
        var module = result.ComponentFiles.Values.First();

        module.ShouldContain("s11_re");
        module.ShouldContain("s12_re");
        module.ShouldContain("s21_re");
        module.ShouldContain("s22_re");
    }

    [Fact]
    public void Export_HeuristicFallback_EmitsLoudWarningComment()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

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
        // Module declaration inside the file must match the sanitized file name.
        result.ComponentFiles["ebeam_y_1550.va"].ShouldContain("module ebeam_y_1550");
    }

    [Fact]
    public void Export_TwoComponentsSanitizeToSameName_ReturnsCollisionFailure()
    {
        // 'ebeam.y-1550' and 'ebeam_y_1550' both sanitize to 'ebeam_y_1550'
        // — must fail loud rather than silently share a module file.
        var a = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        a.NazcaFunctionName = "ebeam.y-1550";
        var b = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        b.NazcaFunctionName = "ebeam_y_1550";

        var result = _exporter.Export(new[] { a, b }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("collision", Case.Insensitive);
    }

    [Fact]
    public void Export_TestBenchWithNoExternalPorts_ReturnsFailure()
    {
        // Fully-connected loop → no external ports → test bench cannot be meaningful.
        var wg1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg1.Identifier = "wg1";
        var wg2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg2.Identifier = "wg2";
        var c1 = new WaveguideConnection { StartPin = wg1.PhysicalPins[0], EndPin = wg2.PhysicalPins[1] };
        var c2 = new WaveguideConnection { StartPin = wg1.PhysicalPins[1], EndPin = wg2.PhysicalPins[0] };

        var result = _exporter.Export(new[] { wg1, wg2 }, new[] { c1, c2 },
            new VerilogAExportOptions { IncludeTestBench = true });

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("external port", Case.Insensitive);
    }

    [Fact]
    public void Export_TotalFileCount_MatchesExpectedCount()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { IncludeTestBench = true });

        result.TotalFileCount.ShouldBe(3);
    }

    [Fact]
    public void Export_DuplicateComponentTypes_GeneratesSingleModuleFile()
    {
        // Two components with the same NazcaFunctionName must share one .va module,
        // otherwise the top-level netlist would emit two `include` lines for the same symbol.
        var comp1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var comp2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp1, comp2 }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.Success.ShouldBeTrue();
        result.ComponentFiles.Count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies that forward (S21) and backward (S12) S-parameters are extracted
    /// independently. A refactor that collapsed them to one key would still pass
    /// the symmetric phase-shifter test but would fail here.
    /// </summary>
    [Fact]
    public void Export_PhaseShifter_AsymmetricForwardBackward_BothDirectionsEmittedIndependently()
    {
        var comp = TestComponentFactory.CreatePhaseShifterWithPhysicalPins(
            forward:  new Complex(0, 1),   // S21 = i
            backward: new Complex(0.5, 0)  // S12 = 0.5 (real, different from S21)
        );

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { WavelengthNm = 1550 });
        var module = result.ComponentFiles.Values.First();

        result.Success.ShouldBeTrue();
        module.ShouldContain("s21_re = 0;");
        module.ShouldContain("s21_im = 1;");
        module.ShouldContain("s12_re = 0.5;");
        module.ShouldContain("s12_im = 0;");
    }

    /// <summary>
    /// Verifies that a phase-shifter with S12 = e^(iπ/2) = i emits both
    /// <c>s12_re</c> and <c>s12_im</c> parameters, preserving the full complex
    /// value rather than collapsing to the real part only.
    /// </summary>
    [Fact]
    public void Export_PhaseShifter_S12EqualToImaginaryUnit_BothReImParametersInFile()
    {
        // e^(iπ/2) = i → Re part = 0, Im part = 1
        var comp = TestComponentFactory.CreatePhaseShifterWithPhysicalPins(forward: new Complex(0, 1));

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { WavelengthNm = 1550 });
        var module = result.ComponentFiles.Values.First();

        result.Success.ShouldBeTrue();
        module.ShouldContain("s12_re");
        module.ShouldContain("s12_im");
        // cos(π/2) = 0, sin(π/2) = 1 — exact values via G6 format
        module.ShouldContain("s12_re = 0;");
        module.ShouldContain("s12_im = 1;");
    }
}
