using System.Numerics;
using CAP_Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
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
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalPins.Clear();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("no physical ports");
    }

    [Fact]
    public void Export_OrphanConnectionPin_ReturnsFailureWithActionableMessage()
    {
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
        result.TopLevelNetlist.ShouldContain("inst_0");
        result.TopLevelNetlist.ShouldContain("inst_1");
    }

    [Fact]
    public void Export_TwoPortWaveguide_ModuleContainsReImPortPairs()
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
    public void Export_TwoPortWaveguide_ModuleContainsSParametersAsReIm()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());
        var module = result.ComponentFiles.Values.First();

        // New format: _re and _im instead of _mag and _phase
        module.ShouldContain("s11_re");
        module.ShouldContain("s11_im");
        module.ShouldContain("s12_re");
        module.ShouldContain("s12_im");
        module.ShouldContain("s21_re");
        module.ShouldContain("s21_im");
        module.ShouldContain("s22_re");
        module.ShouldContain("s22_im");
    }

    [Fact]
    public void Export_TwoPortWaveguide_TransferEquationsUseComplexMultiplication()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());
        var module = result.ComponentFiles.Values.First();

        // Complex multiplication: Y_re = s_re*X_re - s_im*X_im
        module.ShouldContain("_re) <+");
        module.ShouldContain("_im) <+");
        // Must have subtraction for the imaginary cross-term
        module.ShouldContain(" - ");
    }

    /// <summary>
    /// Phase-shifter test: s12 = e^(iπ/2) = i. The generated file must contain
    /// s12_re = 0 (cosine part) and s12_im = 1 (sine part), proving phase is preserved.
    /// With the old mag*cos(phase) formula this would collapse to zero — plain wrong.
    /// </summary>
    [Fact]
    public void Export_PhaseShifterWithPureImaginaryS12_PreservesBothReAndImParts()
    {
        var comp = CreatePhaseShifterComponent(phaseShiftRadians: Math.PI / 2);

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { WavelengthNm = StandardWaveLengths.RedNM });

        result.Success.ShouldBeTrue();
        var module = result.ComponentFiles.Values.First();

        // s12 = e^(iπ/2) = 0 + 1i → re ≈ 0, im ≈ 1
        module.ShouldContain("s12_re");
        module.ShouldContain("s12_im");

        ExtractParameterValue(module, "s12_re").ShouldBe(0.0, tolerance: 1e-6);
        ExtractParameterValue(module, "s12_im").ShouldBe(1.0, tolerance: 1e-6);
    }

    /// <summary>
    /// Verifies that a π phase shift (s12 = -1) has re=-1, im=0.
    /// The old mag*cos(phase) formula would yield |s12|*cos(π) = -1, which happens to be
    /// numerically correct in this special case — but only because the imaginary part is zero.
    /// This test proves the new formula also handles this correctly.
    /// </summary>
    [Fact]
    public void Export_PiPhaseShift_RePartIsNegativeOne()
    {
        var comp = CreatePhaseShifterComponent(phaseShiftRadians: Math.PI);

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions { WavelengthNm = StandardWaveLengths.RedNM });

        result.Success.ShouldBeTrue();
        var module = result.ComponentFiles.Values.First();

        // s12 = e^(iπ) = -1 + 0i → re = -1, im ≈ 0
        ExtractParameterValue(module, "s12_re").ShouldBe(-1.0, tolerance: 1e-6);
        ExtractParameterValue(module, "s12_im").ShouldBe(0.0, tolerance: 1e-5);
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
        var comp1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var comp2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp1, comp2 }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.Success.ShouldBeTrue();
        result.ComponentFiles.Count.ShouldBe(1);
    }

    [Fact]
    public void Export_ModuleHeader_DocumentsComplexModelConvention()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());
        var module = result.ComponentFiles.Values.First();

        module.ShouldContain("Complex-amplitude model", Case.Insensitive);
        module.ShouldContain("_re");
        module.ShouldContain("_im");
    }

    [Fact]
    public void Export_TopLevelNetlist_ExternalPortsAreReImPairs()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = _exporter.Export(new[] { comp }, new List<WaveguideConnection>(),
            new VerilogAExportOptions());

        result.TopLevelNetlist.ShouldContain("ext_port0_re");
        result.TopLevelNetlist.ShouldContain("ext_port0_im");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a two-port phase-shifter component whose s12 = e^(i*phaseShiftRadians).
    /// </summary>
    private static Component CreatePhaseShifterComponent(double phaseShiftRadians)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right)
        });

        var leftIn = parts[0, 0].GetPinAt(RectSide.Left).IDInFlow;
        var rightOut = parts[0, 0].GetPinAt(RectSide.Right).IDOutFlow;
        var rightIn = parts[0, 0].GetPinAt(RectSide.Right).IDInFlow;
        var leftOut = parts[0, 0].GetPinAt(RectSide.Left).IDOutFlow;

        var s = new Complex(Math.Cos(phaseShiftRadians), Math.Sin(phaseShiftRadians));

        var allPins = Component.GetAllPins(parts)
            .SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(allPins, new());
        sMatrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (leftIn, rightOut), s },
            { (rightIn, leftOut), s }
        });

        var waveLengths = new Dictionary<int, SMatrix>
        {
            { StandardWaveLengths.RedNM, sMatrix }
        };

        var comp = new Component(waveLengths, new(), "phase_shifter", "", parts, 0, "PhaseShifter",
            DiscreteRotation.R0);

        var logicalLeft = parts[0, 0].GetPinAt(RectSide.Left);
        var logicalRight = parts[0, 0].GetPinAt(RectSide.Right);

        comp.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in",
            ParentComponent = comp,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 125,
            AngleDegrees = 180,
            LogicalPin = logicalLeft
        });
        comp.PhysicalPins.Add(new PhysicalPin
        {
            Name = "out",
            ParentComponent = comp,
            OffsetXMicrometers = 250,
            OffsetYMicrometers = 125,
            AngleDegrees = 0,
            LogicalPin = logicalRight
        });

        return comp;
    }

    /// <summary>
    /// Extracts a parameter value from a line like: parameter real s12_re = 0.123456;
    /// </summary>
    private static double ExtractParameterValue(string moduleText, string paramName)
    {
        foreach (var line in moduleText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith($"parameter real {paramName} =", StringComparison.Ordinal))
            {
                var eqIdx = trimmed.IndexOf('=');
                var semi = trimmed.IndexOf(';');
                if (eqIdx >= 0 && semi > eqIdx)
                {
                    var valueStr = trimmed[(eqIdx + 1)..semi].Trim();
                    return double.Parse(valueStr, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        throw new InvalidOperationException($"Parameter '{paramName}' not found in module text.");
    }
}
