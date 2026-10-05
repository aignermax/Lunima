using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.MaterialDispersion;
using Shouldly;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Integration;

/// <summary>
/// Acceptance for #1382 against the shipped examples: the FSR readout the
/// Spectrum tab computes per simulated curve must reproduce the ring's
/// λ²/(n_g·L_ring) (the value <see cref="EBeamAddDropRingExampleTests"/>
/// asserts, ≈ 9.8 nm) and the MZI's λ²/(n_g·ΔL) from
/// <see cref="EBeamMziCoherentFringeTests"/> — no eyeballing the axis needed.
/// </summary>
public class SpectrumFsrReadoutIntegrationTests
{
    private const double RingMaxFsrRelativeError = 0.02;
    private const double MziMaxFsrRelativeError = 0.05;
    private const int SweepStartNm = 1500;
    private const int SweepEndNm = 1600;
    private const int SweepSteps = 500;
    private const double CenterWavelengthNm = 1550.0;
    private const double TestNEff = 2.45;
    private const double TestGroupIndex = 4.2;

    [Fact]
    public async Task AddDropRing_ThroughReadoutFsr_MatchesRingPhysicsWithinTwoPercent()
    {
        var (canvas, fileOps, _) = await LoadExample(
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        await fileOps.PostLoadRouting;
        canvas.ConnectionManager.RecalculateAllTransmissions(null, CancellationToken.None);

        double ringLengthUm = EBeamAddDropRingJourneyDesign.MeasureRingLength(canvas);
        var ringSegment = EBeamAddDropRingJourneyDesign.FindRingSegment(canvas, "port 4");

        var circuit = SpectrumSweepCircuitFactory.Create(canvas);
        circuit.ShouldNotBeNull();
        var sweeper = new WavelengthSweeper(
            new SystemMatrixBuilder(circuit.GridManager), circuit.Ports);
        var sweep = await sweeper.RunSweepAsync(
            new WavelengthSweepConfiguration(SweepStartNm, SweepEndNm, SweepSteps),
            circuit.GridManager);

        var curves = TransmissionSpectrumBuilder.Build(sweep, circuit.OutputCouplerPinIds);
        var throughCurve = curves.Single(c =>
            circuit.PinNames.TryGetValue(c.PinId, out var name)
            && name.Contains("Through") && name.Contains("port 2"));

        var readout = CurveFsrAnalyzer.Analyze(throughCurve);

        readout.ShouldNotBeNull("the through-port comb must yield an FSR readout");
        readout.ExtremumKind.ShouldBe(SpectrumExtremumKind.Dips);
        double expectedFsr = ExpectedFsrNm(
            CenterWavelengthNm, GroupIndex(ringSegment, CenterWavelengthNm), ringLengthUm);
        double relativeError = Math.Abs(readout.MeanFsrNm - expectedFsr) / expectedFsr;
        relativeError.ShouldBeLessThanOrEqualTo(RingMaxFsrRelativeError,
            $"Through readout FSR {readout.MeanFsrNm:F3} nm vs. ring physics {expectedFsr:F3} nm " +
            $"(n_g={GroupIndex(ringSegment, CenterWavelengthNm):F4}, L_ring={ringLengthUm:F3} µm)");
    }

    [Fact]
    public async Task Mzi_CoherentReadoutFsr_AgreesWithFringePhysics()
    {
        var (canvas, fileOps, _) = await LoadExample("EBeam Mach-Zehnder Interferometer.lun");
        await fileOps.PostLoadRouting;

        // Same physically consistent dispersion model EBeamMziCoherentFringeTests wires up.
        var dispersion = new PolynomialDispersion(
            centerWavelengthNm: CenterWavelengthNm,
            n0: TestNEff,
            n1: (TestNEff - TestGroupIndex) / CenterWavelengthNm);
        foreach (var connVm in canvas.Connections)
            connVm.Connection.DispersionModel = dispersion;
        canvas.ConnectionManager.RecalculateAllTransmissions(null, CancellationToken.None);

        double deltaL = MeasureArmLengthDifference(canvas);
        var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");
        var (wavelengths, power) = await SweepOutputPowerAsync(canvas, outputPin, coherent: true);

        var curve = new TransmissionCurve(
            outputPin.LogicalPin!.IDInFlow, wavelengths, power, isAtNoiseFloor: false);
        var readout = CurveFsrAnalyzer.Analyze(curve);

        readout.ShouldNotBeNull("the coherent MZI fringes must yield an FSR readout");
        readout.ExtremumKind.ShouldBe(SpectrumExtremumKind.Dips);
        readout.ExtremumCount.ShouldBeGreaterThanOrEqualTo(3);
        double expectedFsr = ExpectedFsrNm(CenterWavelengthNm, TestGroupIndex, deltaL);
        readout.MeanFsrNm.ShouldBe(expectedFsr, expectedFsr * MziMaxFsrRelativeError,
            $"readout FSR must agree with λ²/(n_g·ΔL) for n_g={TestGroupIndex}, ΔL={deltaL:F3} µm");
    }

    /// <summary>Group index n_g = n_eff − λ·dn_eff/dλ from the segment's dispersion model.</summary>
    private static double GroupIndex(WaveguideConnection connection, double lambdaNm)
    {
        const double deltaNm = 0.5;
        double nEff = connection.GetEffectiveIndex(lambdaNm);
        double dNeff = (connection.GetEffectiveIndex(lambdaNm + deltaNm)
            - connection.GetEffectiveIndex(lambdaNm - deltaNm)) / (2 * deltaNm);
        return nEff - lambdaNm * dNeff;
    }
}
