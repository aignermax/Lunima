using System.Globalization;
using System.Text;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core.Analysis.MeasuredSpectrum;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Integration;

/// <summary>
/// Priya's lab loop end-to-end (issue #1334): simulate the shipped EBeam
/// Mach-Zehnder with the PDK's own dispersion (#1327), export the spectrum as
/// a measured-style CSV, and let the <see cref="FringeAnalyzer"/> (#1323)
/// recover the group index. Simulator and extractor must agree with the cited
/// SiEPIC n_g(1550) — if they do not, one of them lies. Also pins the
/// fiber-pin-only injection of two-pin grating couplers (#1326) on the real
/// example, and the honesty of the CSV round-trip under a comma-decimal
/// culture (de-DE).
/// </summary>
public class EBeamMziPriyaLoopHonestyTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";

    /// <summary>Cited SiEPIC EBeam 500x220 nm TE strip group index (siepic-ebeam-pdk.json).</summary>
    private const double PdkGroupIndex = 4.19088;

    private const double CenterWavelengthNm = 1550.0;
    private const int SweepStartNm = 1500;
    private const int SweepEndNm = 1600;
    private const int SweepStepCount = 400;
    private const int MinFringeMinima = 3;
    private const double GroupIndexTolerance = 0.03;
    private const double FsrTolerance = 0.05;
    private const double CsvRoundTripTolerance = 0.001;

    [Fact]
    public async Task SimulatedMziSpectrum_FringeAnalyzerRecoversPdkGroupIndex()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        // #1327 honesty gate: the PDK alone must have wired dispersion onto every
        // routed waveguide — this test injects no dispersion model of its own.
        canvas.Connections.ShouldNotBeEmpty();
        canvas.Connections.ShouldAllBe(
            c => c.Connection.DispersionModel != null,
            "the loaded PDK must wire materialDispersion onto every routed waveguide");

        AssertFiberPinOnlyInjection(canvas);

        double deltaL = MeasureArmLengthDifference(canvas);
        var (wavelengthsNm, powerDb) = await SweepOutputFiberPowerAsync(canvas);
        wavelengthsNm.Length.ShouldBeGreaterThanOrEqualTo(100,
            "the integer-nm sweep grid deduplicates 400 steps to ~101 distinct wavelengths");

        var directResult = Analyze(wavelengthsNm, powerDb, deltaL);
        AssertRecoversPdkIndices(directResult, deltaL);

        var csvResult = RoundTripThroughCsv(wavelengthsNm, powerDb, deltaL);
        csvResult.GroupIndex.ShouldNotBeNull("the CSV round-trip must preserve the fringes");
        double directNg = directResult.GroupIndex!.Value;
        double csvNg = csvResult.GroupIndex!.Value;
        Math.Abs(csvNg - directNg).ShouldBeLessThan(directNg * CsvRoundTripTolerance,
            "CSV write/read must not move the extracted n_g (InvariantCulture honesty)");
    }

    /// <summary>
    /// #1326 on the loaded example: exactly one component injects (gc_in), and
    /// every registered source sits on its unconnected fiber-side pin only.
    /// </summary>
    private static void AssertFiberPinOnlyInjection(DesignCanvasViewModel canvas)
    {
        var portManager = new PhysicalExternalPortManager();
        var sources = new SimulationService().ConfigureLightSources(canvas, portManager);
        sources.Count.ShouldBe(1, "only gc_in has its laser switched on");

        var gcIn = FindComponent(canvas, "gc_in");
        var fiberPinId = FindPin(gcIn, "port 1").LogicalPin!.IDInFlow;
        var attachedPins = portManager.GetUsedExternalInputs()
            .Select(i => i.AttachedComponentPinId).Distinct().ToList();
        attachedPins.Count.ShouldBe(1, "injection must happen on one pin only");
        attachedPins[0].ShouldBe(fiberPinId,
            "the two-pin grating coupler must inject on its fiber-side pin, not the wired waveguide pin");
    }

    /// <summary>
    /// Sweeps 1500–1600 nm with coherent propagation phase through the same
    /// circuit factory and sweeper the Wavelength Spectrum tab uses, and reads
    /// the power leaving the output coupler's fiber port toward the detector
    /// (the pin's out-flow — its in-flow id sits at the noise floor because the
    /// fiber is off-chip).
    /// </summary>
    private static async Task<(double[] WavelengthsNm, double[] PowerDb)> SweepOutputFiberPowerAsync(
        DesignCanvasViewModel canvas)
    {
        canvas.ConnectionManager.EnableCoherentPropagationPhase = true;
        try
        {
            var circuit = SpectrumSweepCircuitFactory.Create(canvas);
            circuit.ShouldNotBeNull("the loaded example must yield a sweep circuit");

            var sweeper = new WavelengthSweeper(new SystemMatrixBuilder(circuit.GridManager), circuit.Ports);
            var sweep = await sweeper.RunSweepAsync(
                new WavelengthSweepConfiguration(SweepStartNm, SweepEndNm, SweepStepCount),
                circuit.GridManager);

            var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 1");
            var wavelengths = sweep.GetWavelengthValues().Select(w => (double)w).ToArray();
            var powerDb = sweep.GetInsertionLossSeriesForPin(outputPin.LogicalPin!.IDOutFlow).ToArray();
            // The sweep grid rounds to integer nm, so sub-nm steps revisit
            // wavelengths; collapse the duplicates for the fringe analysis.
            return Deduplicate(wavelengths, powerDb);
        }
        finally
        {
            canvas.ConnectionManager.EnableCoherentPropagationPhase = false;
        }
    }

    private static FringeAnalysisResult Analyze(double[] wavelengthsNm, double[] powerDb, double deltaL)
    {
        var linear = powerDb.Select(MeasuredSpectrumCsvReader.DecibelToLinear).ToArray();
        return FringeAnalyzer.Analyze(new MeasuredSpectrum(wavelengthsNm, linear, "simulated"), deltaL);
    }

    private static void AssertRecoversPdkIndices(FringeAnalysisResult result, double deltaL)
    {
        result.MinimaWavelengthsNm.Count.ShouldBeGreaterThanOrEqualTo(MinFringeMinima,
            "the meander arm imbalance must produce interference fringes");

        result.GroupIndex.ShouldNotBeNull();
        result.GroupIndex.Value.ShouldBe(PdkGroupIndex, PdkGroupIndex * GroupIndexTolerance);

        double expectedFsr = ExpectedFsrNm(CenterWavelengthNm, PdkGroupIndex, deltaL);
        result.MeanFsrNm.ShouldBe(expectedFsr, expectedFsr * FsrTolerance);
    }

    /// <summary>
    /// Writes the sweep as a lab-style two-column CSV (nm, dB) with a header,
    /// reads it back with <see cref="MeasuredSpectrumCsvReader"/>, and re-runs
    /// the fringe analysis — all under a de-DE current culture so a locale
    /// leaking into formatting or parsing would show up as a moved n_g.
    /// </summary>
    private static FringeAnalysisResult RoundTripThroughCsv(
        double[] wavelengthsNm, double[] powerDb, double deltaL)
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"priya_loop_{Guid.NewGuid():N}.csv");
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("wavelength_nm,power_db");
            for (int i = 0; i < wavelengthsNm.Length; i++)
            {
                builder.Append(wavelengthsNm[i].ToString("F6", CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.AppendLine(powerDb[i].ToString("F6", CultureInfo.InvariantCulture));
            }
            File.WriteAllText(csvPath, builder.ToString());

            var spectrum = MeasuredSpectrumCsvReader.ReadFile(csvPath, PowerUnit.Decibel);
            return FringeAnalyzer.Analyze(spectrum, deltaL);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }
}
