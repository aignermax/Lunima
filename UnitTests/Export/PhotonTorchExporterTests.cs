using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Unit tests for <see cref="PhotonTorchExporter"/>.
/// Verifies script structure, component mapping, and connection formatting.
/// </summary>
public class PhotonTorchExporterTests
{
    private readonly PhotonTorchExporter _exporter = new();

    // ── Header tests ────────────────────────────────────────────────────────

    [Fact]
    public void Export_EmptyDesign_ProducesValidPythonHeader()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("import torch");
        script.ShouldContain("import photontorch as pt");
        script.ShouldContain("import numpy as np");
    }

    [Fact]
    public void Export_Default1550nm_EmbedsCorrectFrequency()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("WAVELENGTH_NM = 1550.0");
        script.ShouldContain("1.93414E+14", Case.Insensitive);  // c / 1550nm ≈ 1.934e14 Hz
    }

    [Fact]
    public void Export_Custom1310nm_EmbedsCorrectWavelength()
    {
        var options = new PhotonTorchExporter.ExportOptions { WavelengthNm = 1310.0 };

        var script = _exporter.Export([], [], options);

        script.ShouldContain("WAVELENGTH_NM = 1310.0");
    }

    // ── Component mapping tests ─────────────────────────────────────────────

    [Fact]
    public void Export_StraightWaveguide_MapsToPhotonTorchWaveguide()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp.WidthMicrometers = 250;

        var script = _exporter.Export([comp], []);

        script.ShouldContain("pt.Waveguide(");
        script.ShouldContain("length=");  // 250µm → some length value in metres
    }

    [Fact]
    public void Export_DirectionalCoupler_MapsToPhotonTorchDC()
    {
        var comp = TestComponentFactoryExtensions.CreateDirectionalCouplerWithPhysicalPins();

        var script = _exporter.Export([comp], []);

        script.ShouldContain("pt.DirectionalCoupler(coupling=0.50)");
    }

    [Fact]
    public void Export_GratingCoupler_MapsToDetector()
    {
        var comp = CreateComponentWithNazca("ebeam_gc_te1550");

        var script = _exporter.Export([comp], []);

        script.ShouldContain("pt.Detector()");
    }

    [Fact]
    public void Export_PhaseShifter_MapsToPhaseShifter()
    {
        var comp = CreateComponentWithNazca("ebeam_phase_shifter");

        var script = _exporter.Export([comp], []);

        script.ShouldContain("pt.PhaseShifter(phase=0.0)");
    }

    [Fact]
    public void Export_MmiComponent_MapsToDirectionalCoupler()
    {
        var comp = CreateComponentWithNazca("ebeam_mmi_1x2");

        var script = _exporter.Export([comp], []);

        script.ShouldContain("pt.DirectionalCoupler(");
        script.ShouldContain("MMI approximated");
    }

    // ── Network / connection tests ──────────────────────────────────────────

    [Fact]
    public void Export_TwoConnectedComponents_ContainsNetworkSection()
    {
        var wg1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg1.Identifier = "wg1";
        var wg2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg2.Identifier = "wg2";
        var conn = new WaveguideConnection
        {
            StartPin = wg1.PhysicalPins[1],  // "out"
            EndPin = wg2.PhysicalPins[0]     // "in"
        };

        var script = _exporter.Export([wg1, wg2], [conn]);

        script.ShouldContain("nw = pt.Network(");
        script.ShouldContain("connections=[");
        script.ShouldContain("'out'");
        script.ShouldContain("'in'");
    }

    [Fact]
    public void Export_SingleComponent_IncludedInNetworkArgs()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp.Identifier = "mycomp";

        var script = _exporter.Export([comp], []);

        script.ShouldContain("nw = pt.Network(");
        script.ShouldContain("wg_mycomp=wg_mycomp");
    }

    // ── Simulation section tests ────────────────────────────────────────────

    [Fact]
    public void Export_SteadyStateMode_ContainsSteadyStateCode()
    {
        var options = new PhotonTorchExporter.ExportOptions
        {
            Mode = PhotonTorchExporter.SimulationMode.SteadyState
        };

        var script = _exporter.Export([], [], options);

        script.ShouldContain("Steady-state");
        script.ShouldContain("source[0] = 1.0");
        script.ShouldContain("detected = nw(source=source)");
    }

    [Fact]
    public void Export_TimeDomainMode_ContainsBitrateAndPulse()
    {
        var options = new PhotonTorchExporter.ExportOptions
        {
            Mode = PhotonTorchExporter.SimulationMode.TimeDomain,
            BitRateGbps = 10.0,
            TimeDomainSteps = 500
        };

        var script = _exporter.Export([], [], options);

        script.ShouldContain("nw.bitrate");
        script.ShouldContain("10 Gbit/s", Case.Insensitive);
        script.ShouldContain("torch.zeros(500)");
    }

    [Fact]
    public void Export_DefaultOptions_ProduceSteadyState()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("Steady-state");
        script.ShouldNotContain("nw.bitrate");
    }

    // ── Name-map / sanitization tests ──────────────────────────────────────

    [Fact]
    public void Export_TwoWaveguides_GeneratesUniqueVariableNames()
    {
        var wg1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg1.Identifier = "abc";
        var wg2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg2.Identifier = "def";

        var script = _exporter.Export([wg1, wg2], []);

        // Component definition lines contain "pt.Waveguide(" — extract their variable names
        var defLines = script.Split('\n')
            .Where(l => l.Contains("pt.Waveguide(") && l.Contains("="))
            .Select(l => l.Split('=')[0].Trim())
            .ToList();

        defLines.Count.ShouldBeGreaterThanOrEqualTo(2);
        defLines.Distinct().Count().ShouldBe(defLines.Count);
    }

    // ── Integration test ────────────────────────────────────────────────────

    [Fact]
    public void Export_TwoWaveguidesMziLayout_ProducesRunnablePythonShape()
    {
        // Arrange: simple two-waveguide layout
        var wg1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg1.Identifier = "arm1";
        wg1.WidthMicrometers = 100;

        var wg2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg2.Identifier = "arm2";
        wg2.WidthMicrometers = 100;

        var conn = new WaveguideConnection
        {
            StartPin = wg1.PhysicalPins[1],
            EndPin = wg2.PhysicalPins[0]
        };

        // Act
        var script = _exporter.Export([wg1, wg2], [conn]);

        // Assert: all required sections present
        script.ShouldContain("import photontorch as pt");
        script.ShouldContain("pt.Waveguide(");
        script.ShouldContain("nw = pt.Network(");
        script.ShouldContain("connections=[");
        script.ShouldContain("detected = nw(");
        script.ShouldNotBeNullOrWhiteSpace();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Component CreateComponentWithNazca(string nazcaFunctionName)
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp.NazcaFunctionName = nazcaFunctionName;
        return comp;
    }
}

/// <summary>
/// Helpers for creating test components with physical pins for PhotonTorch export tests.
/// </summary>
internal static class TestComponentFactoryExtensions
{
    /// <summary>
    /// Creates a directional coupler component with physical pins.
    /// </summary>
    public static CAP_Core.Components.Core.Component CreateDirectionalCouplerWithPhysicalPins()
    {
        var comp = TestComponentFactory.CreateDirectionalCoupler();
        comp.NazcaFunctionName = "ebeam_dc_halfring_straight";
        comp.WidthMicrometers = 10;
        comp.HeightMicrometers = 10;

        var in0 = new CAP_Core.Components.Core.PhysicalPin { Name = "in0", ParentComponent = comp };
        var in1 = new CAP_Core.Components.Core.PhysicalPin { Name = "in1", ParentComponent = comp };
        var out0 = new CAP_Core.Components.Core.PhysicalPin { Name = "out0", ParentComponent = comp };
        var out1 = new CAP_Core.Components.Core.PhysicalPin { Name = "out1", ParentComponent = comp };

        comp.PhysicalPins.Add(in0);
        comp.PhysicalPins.Add(in1);
        comp.PhysicalPins.Add(out0);
        comp.PhysicalPins.Add(out1);

        return comp;
    }
}
