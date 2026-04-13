using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Tests for the PicWaveExporter Python script generator.
/// </summary>
public class PicWaveExporterTests
{
    private readonly PicWaveExporter _exporter = new();

    // --- helper factories ---

    private static Component CreateMinimalComponent(string identifier, string nazcaFunctionName = "")
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            nazcaFunctionName,
            "",
            parts,
            0,
            identifier,
            DiscreteRotation.R0,
            new List<PhysicalPin>());
    }

    private static Component CreateComponentWithPhysicalPin(string identifier, string pinName)
    {
        var comp = CreateMinimalComponent(identifier);
        var pin = new PhysicalPin
        {
            Name = pinName,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 5,
            AngleDegrees = 0,
            ParentComponent = comp
        };
        comp.PhysicalPins.Add(pin);
        return comp;
    }

    // --- ToVarName ---

    [Fact]
    public void ToVarName_AlphanumericIdentifier_Unchanged()
    {
        PicWaveExporter.ToVarName("comp_1").ShouldBe("comp_1");
    }

    [Fact]
    public void ToVarName_HyphenInIdentifier_ReplacedWithUnderscore()
    {
        PicWaveExporter.ToVarName("ebeam-y-1550").ShouldBe("ebeam_y_1550");
    }

    [Fact]
    public void ToVarName_StartsWithDigit_PrefixedWithUnderscore()
    {
        PicWaveExporter.ToVarName("1comp").ShouldBe("_1comp");
    }

    [Fact]
    public void ToVarName_EmptyString_ReturnsFallback()
    {
        PicWaveExporter.ToVarName("").ShouldBe("comp");
    }

    // --- Export header ---

    [Fact]
    public void Export_EmptyDesign_ContainsRequiredImports()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("import numpy as np");
        script.ShouldContain("import matplotlib.pyplot as plt");
        script.ShouldContain("from picwave import Circuit");
        script.ShouldContain("circuit = Circuit()");
    }

    [Fact]
    public void Export_EmptyDesign_ContainsWavelengthSweep()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("wavelengths = np.linspace(");
        script.ShouldContain("circuit.simulate(");
    }

    [Fact]
    public void Export_EmptyDesign_ContainsPlotCode()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("plt.figure(");
        script.ShouldContain("plt.xlabel('Wavelength (nm)')");
        script.ShouldContain("plt.show()");
    }

    // --- Component mapping ---

    [Fact]
    public void Export_StraightWaveguideComponent_MapsToWaveguide()
    {
        var comp = CreateMinimalComponent("wg1", "ebeam_wg_strip_straight");
        var script = _exporter.Export([comp], []);

        script.ShouldContain("Waveguide(");
    }

    [Fact]
    public void Export_DirectionalCouplerComponent_MapsToDirectionalCoupler()
    {
        var comp = CreateMinimalComponent("dc1", "ebeam_dc_halfring_straight");
        var script = _exporter.Export([comp], []);

        script.ShouldContain("DirectionalCoupler(");
    }

    [Fact]
    public void Export_MmiComponent_MapsToMmi()
    {
        var comp = CreateMinimalComponent("mmi1", "ebeam_y_1550");
        var script = _exporter.Export([comp], []);

        script.ShouldContain("MMI(");
    }

    [Fact]
    public void Export_GratingCouplerComponent_MapsToGratingCoupler()
    {
        var comp = CreateMinimalComponent("gc1", "ebeam_gc_te1550");
        var script = _exporter.Export([comp], []);

        script.ShouldContain("GratingCoupler()");
    }

    [Fact]
    public void Export_UnknownComponent_MapsToCustomComponent()
    {
        var comp = CreateMinimalComponent("custom1", "unknown_device");
        var script = _exporter.Export([comp], []);

        script.ShouldContain("CustomComponent(");
    }

    [Fact]
    public void Export_Component_UsesIdentifierAsCircuitKey()
    {
        var comp = CreateMinimalComponent("my_ring", "unknown");
        var script = _exporter.Export([comp], []);

        script.ShouldContain("'my_ring'");
    }

    // --- Connections ---

    [Fact]
    public void Export_TwoConnectedComponents_ContainsCircuitConnect()
    {
        var compA = CreateComponentWithPhysicalPin("compA", "out");
        var compB = CreateComponentWithPhysicalPin("compB", "in");
        var conn = new WaveguideConnection { StartPin = compA.PhysicalPins[0], EndPin = compB.PhysicalPins[0] };

        var script = _exporter.Export([compA, compB], [conn]);

        script.ShouldContain("circuit.connect(");
        script.ShouldContain("'compA'");
        script.ShouldContain("'compB'");
        script.ShouldContain("'out'");
        script.ShouldContain("'in'");
    }

    // --- Wavelength sweep options ---

    [Fact]
    public void Export_CustomWavelengthRange_AppearsInScript()
    {
        var script = _exporter.Export([], [], wavelengthMinNm: 1310, wavelengthMaxNm: 1400, numPoints: 50);

        script.ShouldContain("1310");
        script.ShouldContain("1400");
        script.ShouldContain("50");
    }

    // --- Integration test ---

    [Fact]
    public void Export_TwoComponentCircuit_ProducesValidStructure()
    {
        var wg = CreateComponentWithPhysicalPin("wg_in", "out");
        var gc = CreateComponentWithPhysicalPin("gc_out", "in");
        var conn = new WaveguideConnection { StartPin = wg.PhysicalPins[0], EndPin = gc.PhysicalPins[0] };

        var script = _exporter.Export([wg, gc], [conn]);

        // Header
        script.ShouldContain("circuit = Circuit()");
        // Components
        script.ShouldContain("circuit.add_component('wg_in'");
        script.ShouldContain("circuit.add_component('gc_out'");
        // Connection
        script.ShouldContain("circuit.connect('wg_in', 'out', 'gc_out', 'in')");
        // Simulation
        script.ShouldContain("circuit.simulate(");
    }
}
