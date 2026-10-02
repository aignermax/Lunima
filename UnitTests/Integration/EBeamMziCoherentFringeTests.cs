using System.Collections.ObjectModel;
using System.Numerics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.MaterialDispersion;
using Moq;
using Shouldly;
using Xunit;

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
    private const int SweepStartNm = 1500;
    private const int SweepEndNm = 1600;
    private const int SweepStepCount = 201;
    private const double CenterWavelengthNm = 1550.0;
    private const double TestNEff = 2.45;
    private const double TestGroupIndex = 4.2;
    private const double FsrTolerance = 0.10;
    private const int MinFringeMinima = 3;

    /// <summary>A minimum counts as an interference null only when it drops below this
    /// fraction of the surrounding power — the GC/Y-branch bandpass ripple stays shallow.</summary>
    private const double FringeDepthRatio = 0.1;

    /// <summary>Half-width (in samples) of the neighbourhood a fringe null is compared against.</summary>
    private const int FringeNeighborhoodSamples = 7;

    [Fact]
    public async Task CoherentMode_MziOutput_ShowsFringesAtExpectedFsr()
    {
        var (canvas, fileOps, _) = await LoadExample();
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

    private static double ExpectedFsrNm(double wavelengthNm, double groupIndex, double deltaLMicrometers) =>
        wavelengthNm * wavelengthNm / (groupIndex * deltaLMicrometers * 1000.0);

    private static double MeasureArmLengthDifference(DesignCanvasViewModel canvas)
    {
        double upperArm = FindConnection(canvas, "mzi_splitter", "port 2").PathLengthMicrometers;
        double lowerArm = FindConnection(canvas, "mzi_splitter", "port 3").PathLengthMicrometers;
        double deltaL = lowerArm - upperArm;
        deltaL.ShouldBeGreaterThan(0, "the meander arm must be the longer one");
        return deltaL;
    }

    private static async Task<(double[] WavelengthsNm, double[] Power)> SweepOutputPowerAsync(
        DesignCanvasViewModel canvas, PhysicalPin outputPin, bool coherent)
    {
        canvas.ConnectionManager.EnableCoherentPropagationPhase = coherent;
        try
        {
            var inputPin = FindPin(FindComponent(canvas, "gc_in"), "port 1");
            var portManager = new PhysicalExternalPortManager();
            portManager.AddLightSource(
                new ExternalInput("laser", LaserType.Red, 0, new Complex(1.0, 0)),
                inputPin.LogicalPin!.IDInFlow);

            var tileManager = new ComponentListTileManager();
            foreach (var compVm in canvas.Components)
                tileManager.AddComponent(compVm.Component);
            var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
            var sweeper = new WavelengthSweeper(new SystemMatrixBuilder(grid), portManager);
            var sweep = await sweeper.RunSweepAsync(
                new WavelengthSweepConfiguration(SweepStartNm, SweepEndNm, SweepStepCount), grid);

            var power = sweep.GetInsertionLossSeriesForPin(outputPin.LogicalPin!.IDInFlow)
                .Select(TransmissionSpectrumBuilder.DbToLinear).ToArray();
            // The sweep grid rounds to integer nm, so sub-nm step counts revisit
            // wavelengths; collapse the duplicates for the fringe analysis.
            return Deduplicate(sweep.GetWavelengthValues().Select(w => (double)w).ToArray(), power);
        }
        finally
        {
            canvas.ConnectionManager.EnableCoherentPropagationPhase = false;
        }
    }

    private static (double[] WavelengthsNm, double[] Power) Deduplicate(double[] wavelengths, double[] power)
    {
        var distinctWavelengths = new List<double>();
        var distinctPower = new List<double>();
        for (int i = 0; i < wavelengths.Length; i++)
        {
            if (distinctWavelengths.Count > 0 && wavelengths[i] == distinctWavelengths[^1])
                continue;
            distinctWavelengths.Add(wavelengths[i]);
            distinctPower.Add(power[i]);
        }
        return (distinctWavelengths.ToArray(), distinctPower.ToArray());
    }

    private static List<int> FindFringeMinimaIndices(double[] power, int window = 3)
    {
        var minima = new List<int>();
        for (int i = window; i < power.Length - window; i++)
        {
            bool isMinimum = true;
            for (int k = 1; k <= window && isMinimum; k++)
                isMinimum = power[i] < power[i - k] && power[i] < power[i + k];
            if (!isMinimum)
                continue;

            int from = Math.Max(0, i - FringeNeighborhoodSamples);
            int to = Math.Min(power.Length - 1, i + FringeNeighborhoodSamples);
            double localMax = power[from..(to + 1)].Max();
            if (power[i] < FringeDepthRatio * localMax)
                minima.Add(i);
        }
        return minima;
    }

    private static double RefineMinimumWavelength(double[] wavelengthsNm, double[] power, int index)
    {
        double stepNm = wavelengthsNm[index + 1] - wavelengthsNm[index - 1];
        double denominator = power[index - 1] - 2.0 * power[index] + power[index + 1];
        if (denominator <= 0)
            return wavelengthsNm[index];
        double offset = 0.5 * (power[index - 1] - power[index + 1]) / denominator;
        return wavelengthsNm[index] + offset * stepNm / 2.0;
    }

    private static Component FindComponent(DesignCanvasViewModel canvas, string identifier) =>
        canvas.Components.Single(c => c.Component.Identifier == identifier).Component;

    private static PhysicalPin FindPin(Component component, string pinName) =>
        component.PhysicalPins.Single(p => p.Name == pinName);

    private static WaveguideConnection FindConnection(
        DesignCanvasViewModel canvas, string startComponentId, string startPinName) =>
        canvas.Connections.Single(c =>
            c.Connection.StartPin?.ParentComponent.Identifier == startComponentId
            && c.Connection.StartPin?.Name == startPinName).Connection;

    private static async Task<(DesignCanvasViewModel Canvas, FileOperationsViewModel FileOps, ErrorConsoleService ErrorConsole)>
        LoadExample()
    {
        var canvas = new DesignCanvasViewModel();
        var errorConsole = new ErrorConsoleService();
        var fileOps = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: errorConsole);
        fileOps.FileDialogService = new Mock<IFileDialogService>().Object;
        fileOps.ApplyChipSizeAfterLoad = (widthUm, heightUm) =>
        {
            canvas.ChipMinX = 0;
            canvas.ChipMinY = 0;
            canvas.ChipMaxX = widthUm;
            canvas.ChipMaxY = heightUm;
            canvas.InitializeAStarRouting(0, 0, widthUm, heightUm);
        };

        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
            $"'{ExampleFileName}' must load through the real load path");
        return (canvas, fileOps, errorConsole);
    }
}
