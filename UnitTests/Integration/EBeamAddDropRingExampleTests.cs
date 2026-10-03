using System.Numerics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// E2E for the shipped <c>EBeam Add-Drop Ring.lun</c> example (issue #1359): two
/// <c>DC Halfring-Straight</c> couplers facing each other close a ring through two
/// straight waveguide segments; grating couplers feed In / Through / Drop from an
/// openEBL 127 µm test array. With coherent interference on, a 1500–1600 nm sweep
/// must show ≥ 3 drop-port resonances whose spacing matches the ring's free spectral
/// range λ²/(n_g·L_ring) within 5 % — L_ring measured from the loaded design (both
/// halfring arcs plus the routed ring segments), n_g derived from the dispersion
/// model the PDK wires onto the ring segments. Through dips must coincide with drop
/// peaks (±1 grid step), Through + Drop must stay ≤ 1 (passivity), and a
/// save → load round-trip must reproduce the spectrum within 0.01 dB.
/// </summary>
public class EBeamAddDropRingExampleTests
{
    private const string EBeamPdkName = "SiEPIC EBeam PDK";
    private const int SweepStartNm = 1500;
    private const int SweepEndNm = 1600;
    private const int SweepSteps = 500; // product cap (WavelengthSweepConfiguration.MaxStepCount)
    private const int MinResonanceCount = 3;
    private const double MaxFsrRelativeError = 0.05;
    private const double PassivityEpsilon = 1e-6;
    private const double SaveLoadMaxDbDeviation = 0.01;

    [Fact]
    public async Task LoadsThroughRealLoadPath_CoherentOn_OnlyEBeamPdkComponents()
    {
        var (canvas, fileOps, errorConsole) = await MziFringeAnalysis.LoadExample(
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        await fileOps.PostLoadRouting;

        canvas.Components.Count.ShouldBe(6);
        canvas.Connections.Count.ShouldBe(6);
        foreach (var compVm in canvas.Components)
        {
            compVm.TemplatePdkSource.ShouldBe(EBeamPdkName,
                $"'{compVm.Component.Identifier}' must come from the bundled EBeam PDK");
        }
        errorConsole.Entries.ShouldBeEmpty("the shipped example must load without errors");
        canvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeTrue(
            "the example ships with coherent interference on — the ring resonances need it");
    }

    [Fact]
    public async Task Sweep_DropResonances_MatchRingFsr_ThroughDipsCoincide_Passive()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        await fileOps.PostLoadRouting;

        double ringLengthUm = EBeamAddDropRingJourneyDesign.MeasureRingLength(canvas);
        var ringSegment = EBeamAddDropRingJourneyDesign.FindRingSegment(canvas, "port 4");
        ringSegment.DispersionModel.ShouldNotBeNull(
            "the PDK material dispersion must be wired onto the routed ring segments");

        var (wavelengths, through, drop) = await SweepRing(canvas);
        double gridStepNm = wavelengths[1] - wavelengths[0];

        var peaks = FindDropPeaks(wavelengths, drop);
        peaks.Count.ShouldBeGreaterThanOrEqualTo(MinResonanceCount,
            "the sweep must resolve at least three ring resonances");

        for (int k = 1; k < peaks.Count; k++)
        {
            double measuredFsr = wavelengths[peaks[k]] - wavelengths[peaks[k - 1]];
            double lambdaMid = (wavelengths[peaks[k]] + wavelengths[peaks[k - 1]]) / 2.0;
            double expectedFsr = MziFringeAnalysis.ExpectedFsrNm(
                lambdaMid, GroupIndex(ringSegment, lambdaMid), ringLengthUm);
            (Math.Abs(measuredFsr - expectedFsr) / expectedFsr).ShouldBeLessThanOrEqualTo(
                MaxFsrRelativeError,
                $"FSR between {wavelengths[peaks[k - 1]]:F2} and {wavelengths[peaks[k]]:F2} nm: " +
                $"measured {measuredFsr:F3} nm, expected {expectedFsr:F3} nm " +
                $"(n_g={GroupIndex(ringSegment, lambdaMid):F4}, L_ring={ringLengthUm:F3} µm)");
        }

        foreach (int peak in peaks)
        {
            int dip = ArgMin(through, Math.Max(0, peak - 1), Math.Min(through.Length - 1, peak + 1));
            Math.Abs(dip - peak).ShouldBeLessThanOrEqualTo(1,
                $"the through dip must coincide with the drop peak at {wavelengths[peak]:F2} nm " +
                $"(±1 grid step = {gridStepNm:F2} nm)");
        }

        for (int i = 0; i < wavelengths.Length; i++)
        {
            (through[i] + drop[i]).ShouldBeLessThanOrEqualTo(1.0 + PassivityEpsilon,
                $"Through + Drop must not fabricate power at {wavelengths[i]:F2} nm");
        }
    }

    [Fact]
    public async Task SaveThenLoad_ReproducesSpectrumWithin001Db()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        await fileOps.PostLoadRouting;
        var (_, throughBefore, dropBefore) = await SweepRing(canvas);

        string tempPath = Path.Combine(Path.GetTempPath(), $"adddrop-ring-{Guid.NewGuid():N}.lun");
        try
        {
            await MziFringeAnalysis.SaveToFileAsync(fileOps, tempPath);
            var (loadedCanvas, loadedFileOps, _) = await MziFringeAnalysis.LoadDesignFromPath(tempPath);
            await loadedFileOps.PostLoadRouting;
            var (_, throughAfter, dropAfter) = await SweepRing(loadedCanvas);

            throughBefore.Length.ShouldBe(throughAfter.Length);
            for (int i = 0; i < throughBefore.Length; i++)
            {
                DbDeviation(throughBefore[i], throughAfter[i]).ShouldBeLessThanOrEqualTo(
                    SaveLoadMaxDbDeviation, $"through port differs at grid point {i}");
                DbDeviation(dropBefore[i], dropAfter[i]).ShouldBeLessThanOrEqualTo(
                    SaveLoadMaxDbDeviation, $"drop port differs at grid point {i}");
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>Sweeps In → Through/Drop over 1500–1600 nm with the coherent solver.</summary>
    private static async Task<(double[] Wavelengths, double[] Through, double[] Drop)> SweepRing(
        DesignCanvasViewModel canvas)
    {
        canvas.ConnectionManager.RecalculateAllTransmissions(null, CancellationToken.None);
        var throughPin = Pin(canvas, "gc_through", "port 2");
        var dropPin = Pin(canvas, "gc_drop", "port 2");
        var inputPin = Pin(canvas, "gc_in", "port 1");

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
            new WavelengthSweepConfiguration(SweepStartNm, SweepEndNm, SweepSteps), grid);
        return (sweep.GetWavelengthValues(),
            sweep.GetInsertionLossSeriesForPin(throughPin.LogicalPin!.IDInFlow)
                .Select(TransmissionSpectrumBuilder.DbToLinear).ToArray(),
            sweep.GetInsertionLossSeriesForPin(dropPin.LogicalPin!.IDInFlow)
                .Select(TransmissionSpectrumBuilder.DbToLinear).ToArray());
    }

    /// <summary>Drop-port local maxima with prominence > 3× over a ±3 nm neighbourhood.</summary>
    private static List<int> FindDropPeaks(double[] wavelengths, double[] drop)
    {
        int halfWin = (int)Math.Round(3.0 / (wavelengths[1] - wavelengths[0]));
        var peaks = new List<int>();
        for (int i = 1; i < drop.Length - 1; i++)
        {
            if (drop[i] <= drop[i - 1] || drop[i] <= drop[i + 1])
                continue;
            int lo = Math.Max(0, i - halfWin), hi = Math.Min(drop.Length - 1, i + halfWin);
            double neighMedian = drop[lo..(hi + 1)].OrderBy(v => v).ElementAt((hi - lo) / 2);
            if (drop[i] > 3 * neighMedian && drop[i] > 1e-4)
                peaks.Add(i);
        }
        var merged = new List<int>();
        foreach (var i in peaks)
        {
            if (merged.Count > 0 && wavelengths[i] - wavelengths[merged[^1]] < 2.0)
            {
                if (drop[i] > drop[merged[^1]]) merged[^1] = i;
            }
            else merged.Add(i);
        }
        return merged;
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

    private static int ArgMin(double[] values, int lo, int hi)
    {
        int best = lo;
        for (int i = lo + 1; i <= hi; i++)
            if (values[i] < values[best]) best = i;
        return best;
    }

    private static double DbDeviation(double a, double b) =>
        Math.Abs(10.0 * Math.Log10(Math.Max(a, 1e-12) / Math.Max(b, 1e-12)));

    private static PhysicalPin Pin(DesignCanvasViewModel canvas, string componentId, string pinName) =>
        canvas.Components.Single(c => c.Component.Identifier == componentId)
            .Component.PhysicalPins.Single(p => p.Name == pinName);
}
