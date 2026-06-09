using System.IO;
using System.Linq;
using System.Numerics;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.OnaAnalysis;

/// <summary>
/// End-to-end ONA simulation tests at the circuit level. Builds the canonical
/// ONA topology — analyzer.source → device-under-test → analyzer.measurement —
/// from the REAL shipped ONA Analyzer (tools-pdk.json) and verifies that light
/// injected at the analyzer's source pin actually reaches the measurement pin.
///
/// Regression guard for the "all outputs at the −120 dB floor" bug: the virtual
/// ONA Analyzer must transmit its injected source field into the connected
/// device. If the analyzer's S-matrix lacks a source self-transmission, the
/// light dies at the source pin and every measurement sits at the floor.
/// </summary>
public class OnaAnalyzerSimulationTests
{
    private const int WavelengthNm = 1550;

    private static string GetToolsPdkPath() => Path.Combine(
        Directory.GetCurrentDirectory(), "..", "..", "..", "..",
        "CAP-DataAccess", "PDKs", "tools-pdk.json");

    /// <summary>
    /// Loads the real "ONA Analyzer" component exactly as the application does:
    /// PDK JSON → loader → template converter → Component.
    /// </summary>
    private static Component LoadRealOnaAnalyzer()
    {
        var path = GetToolsPdkPath();
        File.Exists(path).ShouldBeTrue($"tools-pdk.json not found at {path}");

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(path);
        var draft = pdk.Components.First(c => c.Name == "ONA Analyzer");
        var template = PdkTemplateConverter.ConvertToTemplate(draft, pdk.Name, pdk.NazcaModuleName);
        return ComponentTemplates.CreateFromTemplate(template, 0, 0);
    }

    /// <summary>
    /// Minimal 2-port straight-waveguide device-under-test that transmits its
    /// input to its output losslessly at the given wavelength.
    /// </summary>
    private static (Component comp, PhysicalPin inPin, PhysicalPin outPin) CreateStraightDut(int wavelengthNm, double x)
    {
        var inP = new Pin("in", 0, MatterType.Light, RectSide.Left);
        var outP = new Pin("out", 1, MatterType.Light, RectSide.Right);
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin> { inP, outP });

        var pinIds = new List<Guid> { inP.IDInFlow, inP.IDOutFlow, outP.IDInFlow, outP.IDOutFlow };
        var matrix = new SMatrix(pinIds, new());
        matrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (inP.IDInFlow, outP.IDOutFlow), Complex.One },
            { (outP.IDInFlow, inP.IDOutFlow), Complex.One },
        });

        var comp = new Component(
            new Dictionary<int, SMatrix> { { wavelengthNm, matrix } },
            new List<Slider>(), "dut_strt", "", parts, 0, "DUT", DiscreteRotation.R0);
        comp.PhysicalX = x;

        var physIn = new PhysicalPin { Name = "in", ParentComponent = comp, OffsetXMicrometers = 0, OffsetYMicrometers = 5, AngleDegrees = 180, LogicalPin = inP };
        var physOut = new PhysicalPin { Name = "out", ParentComponent = comp, OffsetXMicrometers = 20, OffsetYMicrometers = 5, AngleDegrees = 0, LogicalPin = outP };
        comp.PhysicalPins.Add(physIn);
        comp.PhysicalPins.Add(physOut);
        return (comp, physIn, physOut);
    }

    [Fact]
    public async Task OnaSweep_LightFromAnalyzerSource_ReachesMeasurementThroughDut()
    {
        // === Build the canonical ONA topology ===
        var analyzer = LoadRealOnaAnalyzer();
        var sourcePin = analyzer.PhysicalPins.First(p => p.Name == "source");
        var measurementPin = analyzer.PhysicalPins.First(p =>
            p.Name.StartsWith("measurement", StringComparison.OrdinalIgnoreCase));

        var (dut, dutIn, dutOut) = CreateStraightDut(WavelengthNm, x: 100);

        var tiles = new ComponentListTileManager();
        tiles.AddComponent(analyzer);
        tiles.AddComponent(dut);

        // analyzer.source → DUT.in ; DUT.out → analyzer.measurement
        var connections = new WaveguideConnectionManager(new WaveguideRouter());
        connections.AddExistingConnection(new WaveguideConnection { StartPin = sourcePin, EndPin = dutIn });
        connections.AddExistingConnection(new WaveguideConnection { StartPin = dutOut, EndPin = measurementPin });

        // Inject light at the source pin's IDInFlow — exactly as OnaSweepViewModel does.
        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("ona_source", LaserType.Red, 0, new Complex(1.0, 0)),
            sourcePin.LogicalPin!.IDInFlow);

        var grid = GridManager.CreateForSimulation(tiles, connections, portManager);
        var sweeper = new WavelengthSweeper(new SystemMatrixBuilder(grid), portManager);
        // Sweep across a range bracketing the defined 1550 nm point.
        var config = new WavelengthSweepConfiguration(1500, 1600, 3);

        // === Run the sweep ===
        var result = await sweeper.RunSweepAsync(config, grid);

        // === Verify the VALUE, not just "above floor" ===
        // The path source → (lossless straight DUT) → measurement is lossless, so the
        // measurement pin must receive the full injected power → ~0 dB insertion loss.
        var dataPoint = result.DataPoints.First(dp => dp.WavelengthNm == WavelengthNm);
        dataPoint.InsertionLossDb.TryGetValue(measurementPin.LogicalPin!.IDInFlow, out double measDb);

        measDb.ShouldBeGreaterThan(WavelengthDataPoint.MinInsertionLossDb + 1,
            "Light injected at the ONA Analyzer 'source' pin must propagate through the DUT " +
            "to the 'measurement' pin. A −120 dB floor means the analyzer never emitted into the circuit.");
        measDb.ShouldBeInRange(-0.5, 0.5,
            "A lossless source→DUT→measurement path should yield ~0 dB insertion loss at the " +
            "measurement pin (full transmitted power).");
    }

    [Fact]
    public void RegularSim_AnalyzerSourcePin_IsTreatedAsLightSource()
    {
        // In the regular "L" simulation the analyzer's source pin must emit light
        // (mirroring the ONA sweep) so light flows visibly source → DUT → measurement.
        var analyzer = LoadRealOnaAnalyzer();

        var sourcePin = SimulationService.GetAnalyzerLightSourcePin(analyzer);

        sourcePin.ShouldNotBeNull();
        sourcePin!.Name.ShouldBe("source");
    }

    [Fact]
    public void RegularSim_NonAnalyzerComponent_HasNoAnalyzerLightSource()
    {
        // Only analysis tools get the source-pin treatment; ordinary components don't.
        var (dut, _, _) = CreateStraightDut(WavelengthNm, x: 0);

        SimulationService.GetAnalyzerLightSourcePin(dut).ShouldBeNull();
    }
}
