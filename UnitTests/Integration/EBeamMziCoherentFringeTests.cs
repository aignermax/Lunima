using CAP_Core.LightCalculation.MaterialDispersion;
using Shouldly;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Integration;

/// <summary>
/// Fringe proof for the opt-in coherent propagation-phase mode (issue #1319):
/// the shipped EBeam Mach-Zehnder — whose lower arm carries a deliberate meander —
/// must show periodic transmission minima whose spacing matches the free spectral
/// range FSR = λ²/(n_g·ΔL) once routed waveguides carry their propagation phase.
/// With the mode off (default) the same sweep shows no such fringes.
/// </summary>
public class EBeamMziCoherentFringeTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const double CenterWavelengthNm = 1550.0;
    private const double TestNEff = 2.45;
    private const double TestGroupIndex = 4.2;
    private const double FsrTolerance = 0.10;
    private const int MinFringeMinima = 3;

    [Fact]
    public async Task CoherentMode_MziOutput_ShowsFringesAtExpectedFsr()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        // A physically consistent waveguide dispersion model: n_g derived from the
        // n_eff(λ) slope equals the group index the FSR assertion below uses.
        var dispersion = new PolynomialDispersion(
            centerWavelengthNm: CenterWavelengthNm,
            n0: TestNEff,
            n1: (TestNEff - TestGroupIndex) / CenterWavelengthNm);
        foreach (var connVm in canvas.Connections)
            connVm.Connection.DispersionModel = dispersion;

        canvas.ConnectionManager.RecalculateAllTransmissions(null, CancellationToken.None);
        double deltaL = MeasureArmLengthDifference(canvas);
        double nG = dispersion.GroupIndexAt(CenterWavelengthNm);
        nG.ShouldBe(TestGroupIndex, 1e-9);

        var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");

        // Control: with the mode off the spectrum is the component bandpass only —
        // its ripple stays shallow, no deep interference nulls.
        var incoherent = await SweepOutputPowerAsync(canvas, outputPin, coherent: false);
        FindFringeMinimaIndices(incoherent.Power).ShouldBeEmpty(
            "without propagation phase there are no ΔL fringes — only the GC/Y-branch bandpass");

        var coherent = await SweepOutputPowerAsync(canvas, outputPin, coherent: true);
        var minima = FindFringeMinimaIndices(coherent.Power);
        minima.Count.ShouldBeGreaterThanOrEqualTo(MinFringeMinima,
            "the meander arm-length difference must produce interference fringes");

        // Parabolic refinement around each sampled minimum for sub-step localization.
        var minimaWavelengths = minima
            .Select(i => RefineMinimumWavelength(coherent.WavelengthsNm, coherent.Power, i))
            .ToArray();
        for (int m = 0; m < minimaWavelengths.Length - 1; m++)
        {
            double spacing = minimaWavelengths[m + 1] - minimaWavelengths[m];
            double meanWavelengthNm = (minimaWavelengths[m + 1] + minimaWavelengths[m]) / 2.0;
            double expectedFsr = ExpectedFsrNm(meanWavelengthNm, nG, deltaL);
            spacing.ShouldBe(expectedFsr, expectedFsr * FsrTolerance);
        }
    }
}
