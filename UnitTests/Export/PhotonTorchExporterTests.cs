using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Unit tests for <see cref="PhotonTorchExporter"/>.
/// Verifies that the generated script uses the actual photontorch context-manager
/// Network API (`with pt.Network() as nw:`) and numeric port indices — NOT the
/// invented kwargs-based API that an earlier version used.
/// Runtime executability is covered by <see cref="PhotonTorchScriptExecutionTests"/>.
/// </summary>
public class PhotonTorchExporterTests
{
    private readonly PhotonTorchExporter _exporter = new();

    // ── Header tests ────────────────────────────────────────────────────────

    [Fact]
    public void Export_AnyDesign_ImportsPhotonTorchAndTorch()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var script = _exporter.Export([comp], []);

        script.ShouldContain("import torch");
        script.ShouldContain("import photontorch as pt");
    }

    [Fact]
    public void Export_Default1550nm_EmbedsCorrectFrequency()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var script = _exporter.Export([comp], []);

        script.ShouldContain("WAVELENGTH_NM = 1550.0");
        script.ShouldContain("1.93414E+14", Case.Insensitive);  // c / 1550nm ≈ 1.934e14 Hz
    }

    [Fact]
    public void Export_Custom1310nm_EmbedsCorrectWavelength()
    {
        var options = new PhotonTorchExporter.ExportOptions { WavelengthNm = 1310.0 };
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var script = _exporter.Export([comp], [], options);

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
        script.ShouldContain("length=");
    }

    [Fact]
    public void Export_DirectionalCoupler_MapsToPhotonTorchDC()
    {
        var comp = TestComponentFactoryExtensions.CreateDirectionalCouplerWithPhysicalPins();

        var script = _exporter.Export([comp], []);

        script.ShouldContain("pt.DirectionalCoupler(coupling=0.50)");
    }

    [Fact]
    public void Export_MmiComponent_MarksAsApproximatedDC()
    {
        var comp = CreateComponentWithNazca("ebeam_mmi_1x2");

        var script = _exporter.Export([comp], []);

        script.ShouldContain("pt.DirectionalCoupler(");
        script.ShouldContain("MMI approximated", Case.Insensitive);
    }

    // ── Network / connection tests ──────────────────────────────────────────

    [Fact]
    public void Export_UsesContextManagerNetworkApi_NotKwargs()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var script = _exporter.Export([comp], []);

        script.ShouldContain("with pt.Network() as nw:");
        script.ShouldNotContain("nw = pt.Network(",
            customMessage: "Must use 'with pt.Network() as nw:' — the kwargs-based constructor does not exist in photontorch.");
        script.ShouldNotContain("connections=[",
            customMessage: "Must use nw.link() per connection — there is no 'connections=' argument on pt.Network.");
        // Components attached as attributes
        script.ShouldContain("nw.wg_");
    }

    [Fact]
    public void Export_TwoConnectedComponents_EmitsLinkCallWithNumericPorts()
    {
        var wg1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg1.Identifier = "wg1";
        var wg2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg2.Identifier = "wg2";
        var conn = new WaveguideConnection
        {
            StartPin = wg1.PhysicalPins[1],  // second pin → port index 1
            EndPin = wg2.PhysicalPins[0]     // first pin → port index 0
        };

        var script = _exporter.Export([wg1, wg2], [conn]);

        script.ShouldContain("nw.link(");
        // Port is numeric (0 / 1), NOT the pin-name 'in'/'out'
        script.ShouldContain(":1'");
        script.ShouldContain("'0:");
        // No string-literal pin names leaking into link arguments
        script.ShouldNotContain("'in'");
        script.ShouldNotContain("'out'");
    }

    [Fact]
    public void Export_UnconnectedPins_AreTerminatedWithSourceAndDetectors()
    {
        // A single waveguide with 2 unconnected pins → 1 source + 1 detector
        var wg = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var script = _exporter.Export([wg], []);

        script.ShouldContain("nw.src = pt.Source()");
        script.ShouldContain("nw.det_0 = pt.Detector()");
    }

    // ── Simulation section tests ────────────────────────────────────────────

    [Fact]
    public void Export_SteadyStateMode_InjectsTorchOnesAndCallsNetwork()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var options = new PhotonTorchExporter.ExportOptions
        {
            Mode = PhotonTorchExporter.SimulationMode.SteadyState
        };

        var script = _exporter.Export([comp], [], options);

        script.ShouldContain("Steady-state", Case.Insensitive);
        script.ShouldContain("source = torch.ones(");
        script.ShouldContain("detected = nw(source=source)");
    }

    [Fact]
    public void Export_TimeDomainMode_ContainsBitrateAndPulseTensor()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var options = new PhotonTorchExporter.ExportOptions
        {
            Mode = PhotonTorchExporter.SimulationMode.TimeDomain,
            BitRateGbps = 10.0,
            TimeDomainSteps = 500
        };

        var script = _exporter.Export([comp], [], options);

        script.ShouldContain("nw.bitrate");
        script.ShouldContain("10 Gbit/s", Case.Insensitive);
        script.ShouldContain("torch.zeros(500,");
    }

    [Fact]
    public void Export_DefaultOptions_ProducesSteadyState()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var script = _exporter.Export([comp], []);

        script.ShouldContain("Steady-state", Case.Insensitive);
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

        var defLines = script.Split('\n')
            .Where(l => l.Contains("pt.Waveguide(") && l.Contains("="))
            .Select(l => l.Split('=')[0].Trim())
            .ToList();

        defLines.Count.ShouldBeGreaterThanOrEqualTo(2);
        defLines.Distinct().Count().ShouldBe(defLines.Count);
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
    /// <summary>Creates a directional coupler component with 4 physical pins.</summary>
    public static Component CreateDirectionalCouplerWithPhysicalPins()
    {
        var comp = TestComponentFactory.CreateDirectionalCoupler();
        comp.NazcaFunctionName = "ebeam_dc_halfring_straight";
        comp.WidthMicrometers = 10;
        comp.HeightMicrometers = 10;

        comp.PhysicalPins.Add(new PhysicalPin { Name = "in0", ParentComponent = comp });
        comp.PhysicalPins.Add(new PhysicalPin { Name = "in1", ParentComponent = comp });
        comp.PhysicalPins.Add(new PhysicalPin { Name = "out0", ParentComponent = comp });
        comp.PhysicalPins.Add(new PhysicalPin { Name = "out1", ParentComponent = comp });

        return comp;
    }
}
