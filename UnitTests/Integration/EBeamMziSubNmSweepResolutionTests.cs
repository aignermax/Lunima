using CAP_Core.Analysis.MeasuredSpectrum;
using Shouldly;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Integration;

/// <summary>
/// Resolution proof for the sub-nm sweep grid (#1349): the shipped EBeam
/// Mach-Zehnder, coherent mode on, swept 1545–1560 nm at 300 steps (0.05 nm
/// spacing) keeps every requested point distinct and resolves the interference
/// notch spectrally — the old integer-nm rounding collapses the same request
/// to 16 points and samples the notch with a single wavelength.
///
/// Note on the issue's "≥ 10 dB deeper null" expectation: in this exact band the
/// MZI's only fringe null sits at 1548.98 nm — 23 pm away from the integer
/// 1549 nm sample (ΔL = 44 µm happens to anchor the null comb near integer
/// wavelengths). The integer grid therefore gets lucky here (measured: −42.7 dB
/// vs −43.2 dB); what it cannot do is resolve the notch SHAPE — it carries one
/// point within 10 dB of the floor, the sub-nm grid carries six. A null landing
/// mid-integer (any ring resonance, or an MZI with a different ΔL) is invisible
/// to the integer grid by the same mechanism.
/// </summary>
public class EBeamMziSubNmSweepResolutionTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const double CenterWavelengthNm = 1550.0;
    private const int SweepStartNm = 1545;
    private const int SweepEndNm = 1560;
    private const int SubNmStepCount = 300;
    private const int IntegerGridStepCount = 16; // 1 nm spacing — the old rounded grid
    private const double NotchWindowDb = 10.0;
    private const int MinSubNmNotchSamples = 5;
    private const int MaxIntegerGridNotchSamples = 1;
    private const double FsrTolerance = 0.05;

    [Fact]
    public async Task SubNmSweep_KeepsAllStepsDistinct_AndResolvesTheFringeNotch()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");

        var subNm = await SweepOutputPowerAsync(
            canvas, outputPin, coherent: true, SweepStartNm, SweepEndNm, SubNmStepCount);
        subNm.WavelengthsNm.Length.ShouldBe(SubNmStepCount,
            "300 requested steps must yield 300 points — the old grid rounded them to 16 distinct nm");
        subNm.WavelengthsNm.Distinct().Count().ShouldBe(SubNmStepCount);
        for (int i = 1; i < subNm.WavelengthsNm.Length; i++)
            subNm.WavelengthsNm[i].ShouldBeGreaterThan(subNm.WavelengthsNm[i - 1]);

        var integerGrid = await SweepOutputPowerAsync(
            canvas, outputPin, coherent: true, SweepStartNm, SweepEndNm, IntegerGridStepCount);
        integerGrid.WavelengthsNm.Distinct().Count().ShouldBe(IntegerGridStepCount,
            "a 1 nm request is exactly integral — identical to the old integer grid");

        double subNmNullDb = ToDb(subNm.Power.Min());
        double integerNullDb = ToDb(integerGrid.Power.Min());
        subNmNullDb.ShouldBeLessThanOrEqualTo(integerNullDb,
            "the finer grid never resolves a null worse than the coarse one");

        // The notch shape: how many samples land within 10 dB of the null floor.
        int subNmNotchSamples = subNm.Power.Count(p => ToDb(p) < subNmNullDb + NotchWindowDb);
        int integerNotchSamples = integerGrid.Power.Count(p => ToDb(p) < subNmNullDb + NotchWindowDb);
        subNmNotchSamples.ShouldBeGreaterThanOrEqualTo(MinSubNmNotchSamples,
            "the sub-nm grid must resolve the notch spectrally, not just touch it");
        integerNotchSamples.ShouldBeLessThanOrEqualTo(MaxIntegerGridNotchSamples,
            "the old integer grid samples the notch with a single wavelength — " +
            "a null between two integer samples would be missed entirely");
    }

    [Fact]
    public async Task SubNmSweep_FringeAnalyzerFsr_MatchesGroupIndexFormula()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        // Wide enough for several fringes: the analyzer needs ≥ 3 minima.
        var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");
        var subNm = await SweepOutputPowerAsync(
            canvas, outputPin, coherent: true, 1500, 1600, 400);

        double deltaL = MeasureArmLengthDifference(canvas);
        double nG = FindConnection(canvas, "mzi_splitter", "port 2")
            .DispersionModel!.GroupIndexAt(CenterWavelengthNm);

        var result = FringeAnalyzer.Analyze(
            new MeasuredSpectrum(subNm.WavelengthsNm, subNm.Power, "simulated"), deltaL);

        result.HasFringes.ShouldBeTrue("the 0.25 nm grid must show the MZI fringes");
        double expectedFsr = ExpectedFsrNm(CenterWavelengthNm, nG, deltaL);
        result.MeanFsrNm.ShouldBe(expectedFsr, expectedFsr * FsrTolerance);
    }

    private static double ToDb(double linearPower) => 10.0 * Math.Log10(linearPower);
}
