using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Integration;

/// <summary>
/// Wiring proof for issue #1327: the PDK's <c>materialDispersion</c> must reach routed
/// waveguide connections through the real load path, so the coherent propagation-phase
/// mode uses the cited SiEPIC EBeam strip indices instead of the flat 2.45 fallback.
/// Cited values (SiEPIC EBeam PDK <c>ebeam_wg_integral_1550</c> compact model source
/// data, 500×220 nm SOI strip, TE, 1550 nm): n_eff = 2.44553, n_g = 4.19088.
/// </summary>
public class PdkWaveguideDispersionWiringTests
{
    private const string EBeamExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const string DemoExampleFileName = "Mach-Zehnder Interferometer.lun";
    private const double CenterWavelengthNm = 1550.0;
    private const double SiepicNEff = 2.44553;
    private const double SiepicGroupIndex = 4.19088;
    private const double CitedValueTolerance = 1e-3;
    private const double FsrTolerance = 0.10;
    private const int MinFringeMinima = 3;

    [Fact]
    public async Task SiepicConnection_LoadedFromLun_InheritsCitedDispersionModel()
    {
        var (canvas, fileOps, _) = await LoadExample(EBeamExampleFileName);
        await fileOps.PostLoadRouting;

        canvas.Connections.ShouldNotBeEmpty();
        foreach (var connVm in canvas.Connections)
        {
            WaveguideConnection connection = connVm.Connection;
            connection.DispersionModel.ShouldNotBeNull(
                $"connection {connection.StartPin?.Name}→{connection.EndPin?.Name} joins SiEPIC " +
                "strip pins whose PDK declares materialDispersion");
            connection.DispersionModel.NEffAt(CenterWavelengthNm)
                .ShouldBe(SiepicNEff, CitedValueTolerance);
            connection.DispersionModel.GroupIndexAt(CenterWavelengthNm)
                .ShouldBe(SiepicGroupIndex, CitedValueTolerance);
        }
    }

    [Fact]
    public async Task DemoPdkConnection_WithoutMaterialDispersion_KeepsNullModel()
    {
        var (canvas, fileOps, _) = await LoadExample(DemoExampleFileName);
        await fileOps.PostLoadRouting;

        canvas.Connections.ShouldNotBeEmpty();
        foreach (var connVm in canvas.Connections)
        {
            connVm.Connection.DispersionModel.ShouldBeNull(
                "the Demo PDK declares no materialDispersion — the fallback stays unchanged");
        }
    }

    [Fact]
    public async Task CoherentMode_WithoutInjectedModel_FringesMatchPdkGroupIndex()
    {
        var (canvas, fileOps, _) = await LoadExample(EBeamExampleFileName);
        await fileOps.PostLoadRouting;

        // No test dispersion model is injected: the sweep must run on whatever the
        // real load path wired from the PDK.
        double deltaL = MeasureArmLengthDifference(canvas);
        var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");

        var coherent = await SweepOutputPowerAsync(canvas, outputPin, coherent: true);
        var minima = FindFringeMinimaIndices(coherent.Power);
        minima.Count.ShouldBeGreaterThanOrEqualTo(MinFringeMinima,
            "the meander arm-length difference must produce interference fringes");

        var minimaWavelengths = minima
            .Select(i => RefineMinimumWavelength(coherent.WavelengthsNm, coherent.Power, i))
            .ToArray();
        for (int m = 0; m < minimaWavelengths.Length - 1; m++)
        {
            double spacing = minimaWavelengths[m + 1] - minimaWavelengths[m];
            double meanWavelengthNm = (minimaWavelengths[m + 1] + minimaWavelengths[m]) / 2.0;
            double expectedFsr = ExpectedFsrNm(meanWavelengthNm, SiepicGroupIndex, deltaL);
            spacing.ShouldBe(expectedFsr, expectedFsr * FsrTolerance,
                $"FSR at {meanWavelengthNm:F1} nm must follow λ²/(n_g·ΔL) with the cited SiEPIC n_g");
        }
    }
}
