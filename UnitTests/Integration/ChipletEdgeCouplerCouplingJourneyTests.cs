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
/// Issue #1228 (rung 6): the simulation must stop being optimistic about misaligned
/// cross-chiplet edge-coupler links. Built on the #1214 journey design
/// (<see cref="ChipletEdgeCouplerJourneyDesign"/>): the aligned baseline must transmit
/// exactly as before, a 1 µm / 2 µm lateral offset must cost the Gaussian mode-overlap
/// power η = exp(-d²/w0²) with w0 = <see cref="ChipletEdgeCouplerCoupling.ModeWaistMicrometers"/> µm
/// (−1.93 dB / −7.72 dB), and a non-facing facet must couple nothing — agreeing with the
/// #1219 <see cref="DesignValidator"/> warning on the same fixture.
/// </summary>
public class ChipletEdgeCouplerCouplingJourneyTests
{
    private const int WavelengthNm = 1550;
    private const double AmplitudeTolerance = 1e-6;
    private const double PowerToleranceRelative = 0.01;
    private const double DbTolerance = 0.05;

    [Theory]
    [InlineData(1.0, -1.93)] // exp(-1²/1.5²) = 0.641 → -1.93 dB
    [InlineData(2.0, -7.72)] // exp(-2²/1.5²) = 0.169 → -7.72 dB
    public async Task LateralOffset_OutputPowerDropsByGaussianOverlap(double offsetMicrometers, double expectedDb)
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        double baseline = await SimulateOutputAmplitudeAsync(design);
        baseline.ShouldBe(design.ExpectedOutputAmplitude, AmplitudeTolerance,
            "the aligned baseline must transmit exactly as before #1228");

        design.ChipletB.MoveGroup(0, offsetMicrometers);
        double shifted = await SimulateOutputAmplitudeAsync(design);

        double eta = ChipletEdgeCouplerCoupling.PowerCouplingForOffset(offsetMicrometers);
        double powerRatio = shifted * shifted / (baseline * baseline);
        powerRatio.ShouldBe(eta, eta * PowerToleranceRelative,
            $"a {offsetMicrometers} µm lateral offset on a w0 = "
            + $"{ChipletEdgeCouplerCoupling.ModeWaistMicrometers} µm mode must cost {expectedDb} dB "
            + "(power η = exp(-d²/w0²))");
        (10 * Math.Log10(powerRatio)).ShouldBe(expectedDb, DbTolerance,
            "the simulated penalty in dB matches the InlineData label");
    }

    [Fact]
    public async Task NonFacingFacets_NoLightCrossesTheChipletBoundary()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();

        // The #1219 rotation fixture: turn chiplet B's facet a quarter turn off axis.
        var bFiber = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_ec_fiber");
        bFiber.AngleDegrees += 90;

        var fields = await SimulateAsync(design.Canvas, InjectLight(
            ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletA, "a_gc_fiber")));
        double output = fields.TryGetValue(
            ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_wg_b0").LogicalPin!.IDOutFlow,
            out var value)
            ? value.Magnitude
            : 0.0;

        output.ShouldBeLessThan(1e-9,
            "facets that do not face each other couple nothing (η = 0) — no invented angular model");
    }

    [Fact]
    public async Task DesignValidatorWarning_AndSimulatedLoss_AgreeOnTheSameOffsetFixture()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        double baseline = await SimulateOutputAmplitudeAsync(design);

        design.ChipletB.MoveGroup(0, 1.0);
        var link = design.Canvas.ConnectionManager.Connections.Single();

        var issues = new DesignValidator().Validate(
            design.Canvas.ConnectionManager.Connections,
            groups: new[] { design.ChipletA, design.ChipletB },
            components: design.Canvas.Components.Select(vm => vm.Component),
            externalPortPins: null,
            wavelengthNm: WavelengthNm,
            minWaveguideSpacingMicrometers: 0);
        issues.ShouldContain(
            i => i.Type == DesignIssueType.ChipletInterfaceLateralOffset && ReferenceEquals(i.Connection, link),
            "the #1219 DRC warning must fire on the offset cross-chiplet link");

        double shifted = await SimulateOutputAmplitudeAsync(design);
        double powerRatio = shifted * shifted / (baseline * baseline);
        double eta = ChipletEdgeCouplerCoupling.PowerCouplingForOffset(1.0);
        powerRatio.ShouldBe(eta, eta * PowerToleranceRelative,
            "the simulated loss must back the warning on the very same fixture (-1.93 dB at 1 µm)");
    }

    // ── Simulation helpers (same headless recipe as ChipletEdgeCouplerJourneyTests) ──

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
