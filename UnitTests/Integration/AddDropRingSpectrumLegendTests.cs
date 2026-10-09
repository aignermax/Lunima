using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Acceptance test for #1374: loading the shipped
/// <c>EBeam Add-Drop Ring.lun</c> and running the spectrum sweep must produce
/// legend titles that are all distinct and read like a textbook —
/// "In → Through.port 2", "In → Drop.port 2" — instead of four identical
/// "Grating Coupler TE 1550.port 2" entries.
/// </summary>
public class AddDropRingSpectrumLegendTests
{
    private const int SweepStartNm = 1530;
    private const int SweepEndNm = 1570;
    private const int SweepSteps = 20; // titles don't depend on resolution — keep it fast

    [Fact]
    public async Task SpectrumLegend_AddDropRing_TitlesDistinct_AndNameThroughAndDrop()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        await fileOps.PostLoadRouting;

        var circuit = SpectrumSweepCircuitFactory.Create(canvas);
        circuit.ShouldNotBeNull();
        circuit.InputLabel.ShouldBe("In",
            "gc_in is the only laser-on coupler — its human name labels every curve's source end");

        var sweeper = new WavelengthSweeper(
            new SystemMatrixBuilder(circuit.GridManager), circuit.Ports);
        var result = await sweeper.RunSweepAsync(
            new WavelengthSweepConfiguration(SweepStartNm, SweepEndNm, SweepSteps),
            circuit.GridManager);

        var curves = TransmissionSpectrumBuilder.Build(result, circuit.OutputCouplerPinIds);
        var model = WavelengthSpectrumPlotBuilder.BuildPlotModel(
            curves,
            pinId => circuit.PinNames.TryGetValue(pinId, out var name)
                ? SpectrumLegendLabelBuilder.ComposeCurveLabel(circuit.InputLabel, name)
                : null,
            circuit.DesignWavelengthNm);

        var titles = model.Series.Select(s => s.Title).ToList();
        titles.Count.ShouldBeGreaterThanOrEqualTo(2,
            $"the add-drop ring must plot at least the Through and Drop curves. Titles: {string.Join(" | ", titles)}");
        titles.Distinct().Count().ShouldBe(titles.Count,
            $"every legend entry must be unique. Titles: {string.Join(" | ", titles)}");
        titles.ShouldContain(t => t.Contains("Through"),
            "the through-port curve must be identifiable");
        titles.ShouldContain(t => t.Contains("Drop"),
            "the drop-port curve must be identifiable");
    }
}
