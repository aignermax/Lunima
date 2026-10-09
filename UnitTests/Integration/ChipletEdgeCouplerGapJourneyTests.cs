using System.Numerics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1238 (rung 6): the longitudinal facet GAP between cross-chiplet edge couplers
/// must cost coupling — Gaussian beam divergence across the free-space gap,
/// η_gap = 1 / (1 + (z / (2·z_R))²) with z_R = π·n·w0²/λ at the simulation wavelength —
/// and the DRC-lite check must warn once the gap alone burns more than
/// <see cref="ChipletInterfaceChecker.MaxGapLossDecibels"/>. Built on the #1214 journey
/// design (<see cref="ChipletEdgeCouplerJourneyDesign"/>), the same fixture the #1228
/// lateral-offset loss and the #1219 facing/lateral/edge warnings are proven on.
/// </summary>
public class ChipletEdgeCouplerGapJourneyTests
{
    private const int WavelengthNm = 1550;
    private const double AmplitudeTolerance = 1e-6;
    private const double PowerToleranceRelative = 0.01;

    [Theory]
    [InlineData(10.0, -3.43)] // η_gap = 0.4541 → -3.43 dB (z_R ≈ 4.56 µm at 1550 nm)
    [InlineData(1.0, -0.05)]  // η_gap = 0.9881 → -0.05 dB
    public async Task FacetGap_OutputPowerDropsByGaussianDivergence(double gapMicrometers, double expectedDb)
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        double baseline = await SimulateOutputAmplitudeAsync(design);
        baseline.ShouldBe(design.ExpectedOutputAmplitude, AmplitudeTolerance,
            "the butt-coupled baseline must transmit exactly as before #1238");

        design.ChipletB.MoveGroup(gapMicrometers, 0); // along the link axis — pure gap
        double shifted = await SimulateOutputAmplitudeAsync(design);

        double eta = ChipletEdgeCouplerCoupling.PowerCouplingForGap(gapMicrometers, WavelengthNm);
        double powerRatio = shifted * shifted / (baseline * baseline);
        powerRatio.ShouldBe(eta, eta * PowerToleranceRelative,
            $"a {gapMicrometers} µm facet gap on a w0 = "
            + $"{ChipletEdgeCouplerCoupling.ModeWaistMicrometers} µm mode must cost {expectedDb} dB "
            + "(power η_gap = 1 / (1 + (z / (2·z_R))²))");
        (10 * Math.Log10(powerRatio)).ShouldBe(expectedDb, 0.05,
            "the simulated penalty in dB matches the InlineData label");
    }

    [Fact]
    public async Task FacetGap10Micrometers_DrcWarningFiresAndMatchesSimulatedLoss()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        double baseline = await SimulateOutputAmplitudeAsync(design);

        design.ChipletB.MoveGroup(10.0, 0);
        var link = design.Canvas.ConnectionManager.Connections.Single();

        var issues = new DesignValidator().Validate(
            design.Canvas.ConnectionManager.Connections,
            groups: new[] { design.ChipletA, design.ChipletB },
            components: design.Canvas.Components.Select(vm => vm.Component),
            externalPortPins: null,
            wavelengthNm: WavelengthNm,
            minWaveguideSpacingMicrometers: 0);
        var warning = issues.Where(
                i => i.Type == DesignIssueType.ChipletInterfaceGapLoss && ReferenceEquals(i.Connection, link))
            .ShouldHaveSingleItem("the #1238 DRC warning must fire on the gapped cross-chiplet link");
        warning.Description.ShouldContain(ChipletEdgeCouplerJourneyDesign.ChipletAName);
        warning.Description.ShouldContain(ChipletEdgeCouplerJourneyDesign.ChipletBName);
        warning.Description.ShouldContain("10.00 µm");

        double shifted = await SimulateOutputAmplitudeAsync(design);
        double powerRatio = shifted * shifted / (baseline * baseline);
        double eta = ChipletEdgeCouplerCoupling.PowerCouplingForGap(10.0, WavelengthNm);
        powerRatio.ShouldBe(eta, eta * PowerToleranceRelative,
            "the simulated loss must back the warning on the very same fixture (-3.43 dB at 10 µm)");
    }

    [Fact]
    public void FacetGap1Micrometer_NoDrcWarning()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        design.ChipletB.MoveGroup(1.0, 0);

        var issues = new DesignValidator().Validate(
            design.Canvas.ConnectionManager.Connections,
            groups: new[] { design.ChipletA, design.ChipletB },
            components: design.Canvas.Components.Select(vm => vm.Component),
            externalPortPins: null,
            wavelengthNm: WavelengthNm,
            minWaveguideSpacingMicrometers: 0);

        issues.ShouldNotContain(i => i.Type == DesignIssueType.ChipletInterfaceGapLoss,
            "a 1 µm gap costs 0.05 dB — far inside the 1 dB budget");
    }

    // ── Simulation helpers (same headless recipe as ChipletEdgeCouplerCouplingJourneyTests) ──

    private static async Task<double> SimulateOutputAmplitudeAsync(ChipletEdgeCouplerJourneyDesign design)
    {
        var fields = await SimulateAsync(design.Canvas, InjectLight(
            ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletA, "a_gc_fiber")));
        var outFlow = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_wg_b0").LogicalPin!.IDOutFlow;
        return fields.TryGetValue(outFlow, out var value)
            ? value.Magnitude
            : throw new ShouldAssertException($"pin flow {outFlow} missing from simulated fields");
    }

    private static (ExternalInput Input, Guid PinIdInFlow) InjectLight(PhysicalPin pin) =>
        (new ExternalInput("source", new LaserType(LightColor.Red), 0, new Complex(1.0, 0), true),
         pin.LogicalPin!.IDInFlow);

    /// <summary>Runs the S-matrix field propagation over everything currently on the canvas.</summary>
    private static async Task<Dictionary<Guid, Complex>> SimulateAsync(
        DesignCanvasViewModel canvas, params (ExternalInput Input, Guid PinIdInFlow)[] inputs)
    {
        var portManager = new PhysicalExternalPortManager();
        foreach (var (input, pinIdInFlow) in inputs)
        {
            portManager.AddLightSource(input, pinIdInFlow);
        }

        var tileManager = new ComponentListTileManager();
        foreach (var viewModel in canvas.Components)
        {
            tileManager.AddComponent(viewModel.Component);
        }

        var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
        var calculator = new GridLightCalculator(new SystemMatrixBuilder(grid), grid);
        return await calculator.CalculateFieldPropagationAsync(new CancellationTokenSource(), WavelengthNm);
    }
}
