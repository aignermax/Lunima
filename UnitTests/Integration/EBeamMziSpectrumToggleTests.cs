using System;
using System.Linq;
using System.Threading.Tasks;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using OxyPlot;
using OxyPlot.Series;
using Shouldly;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Integration;

/// <summary>
/// End-to-end proof for the user-facing half of the coherent mode (issue #1333):
/// the shipped EBeam Mach-Zehnder loads with the flag ON through the real load
/// path, the Spectrum tab's toggle drives
/// <see cref="CAP_Core.Components.Connections.WaveguideConnectionManager.EnableCoherentPropagationPhase"/>,
/// and a 1500–1600 nm sweep run through the real
/// <see cref="WavelengthSpectrumViewModel"/> pipeline shows ≥3 fringe nulls spaced
/// within 10 % of FSR = λ²/(n_g·ΔL) with the PDK-wired SiEPIC group index —
/// flipping the toggle off re-runs the sweep and the nulls vanish.
/// </summary>
public class EBeamMziSpectrumToggleTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const double CenterWavelengthNm = 1550.0;
    private const double SiepicGroupIndex = 4.19088;
    private const double CitedValueTolerance = 1e-3;
    private const double FsrTolerance = 0.10;
    private const int MinFringeMinima = 3;

    [Fact]
    public async Task SpectrumTabToggle_EBeamMzi_FringesFollowCoherentMode()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        // The shipped example persists the flag on (#1333) — it must come back on load.
        canvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeTrue(
            "the EBeam MZI example saves the coherent mode on");

        var vm = new WavelengthSpectrumViewModel { AutoRefreshDelay = TimeSpan.Zero };
        vm.Configure(canvas);
        vm.IsCoherentInterference.ShouldBeTrue("Configure syncs the toggle from the loaded design");
        vm.StartNm = 1500;
        vm.EndNm = 1600;
        vm.StepCount = 201;

        await vm.RunSweepCommand.ExecuteAsync(null);
        vm.HasResult.ShouldBeTrue($"sweep failed: {vm.StatusText}");

        var curves = ExtractPlottedCurves(vm);
        // Both flow directions of the waveguide pin are plotted; the fringes live
        // on the one carrying the combiner output — pick the curve with nulls.
        var (wavelengths, power, minima) = curves
            .Select(c => (c.Wavelengths, c.Power, Minima: FindFringeMinimaIndices(c.Power)))
            .OrderByDescending(c => c.Minima.Count)
            .First();
        minima.Count.ShouldBeGreaterThanOrEqualTo(MinFringeMinima,
            "coherent mode on: the ΔL between the arms must show as interference fringes");

        double deltaL = MeasureArmLengthDifference(canvas);
        double nG = FindConnection(canvas, "mzi_splitter", "port 2")
            .DispersionModel!.GroupIndexAt(CenterWavelengthNm);
        nG.ShouldBe(SiepicGroupIndex, CitedValueTolerance);

        var minimaWavelengths = minima
            .Select(i => RefineMinimumWavelength(wavelengths, power, i))
            .ToArray();
        for (int m = 0; m < minimaWavelengths.Length - 1; m++)
        {
            double spacing = minimaWavelengths[m + 1] - minimaWavelengths[m];
            double meanWavelengthNm = (minimaWavelengths[m + 1] + minimaWavelengths[m]) / 2.0;
            double expectedFsr = ExpectedFsrNm(meanWavelengthNm, nG, deltaL);
            spacing.ShouldBe(expectedFsr, expectedFsr * FsrTolerance);
        }

        // Toggle off: the flag drops and the sweep re-runs automatically — no nulls.
        vm.IsCoherentInterference = false;
        canvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeFalse();
        vm.PendingAutoRefresh.ShouldNotBeNull("flipping the toggle re-runs the sweep");
        await vm.PendingAutoRefresh!;
        vm.HasResult.ShouldBeTrue();

        var (_, incoherentPower) = ExtractPlottedCurves(vm)
            .OrderByDescending(c => c.Power.Max() - c.Power.Min())
            .First();
        FindFringeMinimaIndices(incoherentPower).ShouldBeEmpty(
            "coherent mode off: only the shallow GC/Y-branch bandpass ripple remains");
    }

    /// <summary>Reads the plotted gc_out waveguide-port curves back out of the VM's plot model.</summary>
    private static List<(double[] Wavelengths, double[] Power)> ExtractPlottedCurves(
        WavelengthSpectrumViewModel vm)
    {
        // Legend labels are "HumanReadableName.pin"; the waveguide output is port 2
        // (both flow directions of the pin are plotted under the same label).
        var curves = new List<(double[], double[])>();
        foreach (var series in vm.PlotModel.Series
            .OfType<LineSeries>()
            .Where(s => s.Title?.EndsWith(".port 2") == true))
        {
            var wavelengths = series.Points.Select(p => p.X).ToArray();
            var power = series.Points.Select(p => p.Y).ToArray();
            curves.Add((wavelengths, power));
        }
        curves.ShouldNotBeEmpty("the gc_out waveguide port must be plotted");
        return curves;
    }
}
