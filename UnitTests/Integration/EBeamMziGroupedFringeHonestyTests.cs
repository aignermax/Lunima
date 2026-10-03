using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis.MeasuredSpectrum;
using CAP_Core.Components.Core;
using Moq;
using Shouldly;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1343: grouping the EBeam Mach-Zehnder body (splitter, both arms, combiner)
/// must not erase its coherent fringes. The arms become frozen group-internal paths,
/// and those must carry the same propagation phase exp(-i·2π·n_eff(λ)·L/λ) as routed
/// connections — hierarchy must stay transparent to the simulation. Sweeps the real
/// example flat, grouped, and after a save → load round-trip through the real file
/// path, and pins the coherent-off spectrum as a regression guard.
/// </summary>
public class EBeamMziGroupedFringeHonestyTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const double SpectrumToleranceDb = 0.1;
    private const double IncoherentToleranceDb = 0.01;
    private const double FsrTolerance = 0.01;
    private const int MinFringeMinima = 3;

    [Fact]
    public async Task GroupedMzi_CoherentSweep_MatchesFlatDesignEvenAfterReload()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        double deltaL = MeasureArmLengthDifference(canvas);
        var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");

        var flat = await SweepOutputPowerAsync(canvas, outputPin, coherent: true);
        FindFringeMinimaIndices(flat.Power).Count.ShouldBeGreaterThanOrEqualTo(MinFringeMinima,
            "the flat reference spectrum must show fringes — otherwise this test proves nothing");

        GroupMziBody(canvas);
        await canvas.RecalculateRoutesAsync();

        var grouped = await SweepOutputPowerAsync(canvas, outputPin, coherent: true);
        AssertSpectraMatch(flat, grouped, deltaL, "grouped");

        var tempFile = Path.Combine(Path.GetTempPath(), $"mzi_grouped_{Guid.NewGuid():N}.lun");
        try
        {
            await SaveToFile(fileOps, tempFile);

            var (loadCanvas, loadFileOps, _) = await LoadDesignFromPath(tempFile);
            await loadFileOps.PostLoadRouting;
            loadCanvas.Components.Count(c => c.Component is ComponentGroup).ShouldBe(1,
                "the group must survive the .lun round-trip");

            var reloadedPin = FindPin(FindComponent(loadCanvas, "gc_out"), "port 2");
            var reloaded = await SweepOutputPowerAsync(loadCanvas, reloadedPin, coherent: true);
            AssertSpectraMatch(flat, reloaded, deltaL, "reloaded");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task NestedGroupedMzi_CoherentSweep_KeepsFringes()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        double deltaL = MeasureArmLengthDifference(canvas);
        var flatOutputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");
        var flat = await SweepOutputPowerAsync(canvas, flatOutputPin, coherent: true);

        GroupMziBody(canvas);
        await canvas.RecalculateRoutesAsync();

        var mziGroupVm = canvas.Components.Single(c => c.Component is ComponentGroup);
        var gcOutVm = canvas.Components.Single(c => c.Component.Identifier == "gc_out");
        new CreateGroupCommand(canvas, new List<ComponentViewModel> { mziGroupVm, gcOutVm }).Execute();
        await canvas.RecalculateRoutesAsync();

        var outer = (ComponentGroup)canvas.Components.Single(c => c.Component is ComponentGroup).Component;
        var nestedGroup = outer.ChildComponents.OfType<ComponentGroup>().Single();
        nestedGroup.InternalPaths.Count.ShouldBe(2,
            "the MZI arms must stay frozen inside the nested group");
        var gcOut = outer.ChildComponents.Single(c => c.Identifier == "gc_out");

        var nested = await SweepOutputPowerAsync(canvas, FindPin(gcOut, "port 2"), coherent: true);
        AssertSpectraMatch(flat, nested, deltaL, "nested-grouped");
    }

    [Fact]
    public async Task GroupedMzi_CoherentOff_SpectrumUnchangedByGrouping()
    {
        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;
        var outputPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");

        var flatOff = await SweepOutputPowerAsync(canvas, outputPin, coherent: false);

        GroupMziBody(canvas);
        await canvas.RecalculateRoutesAsync();

        var groupedOff = await SweepOutputPowerAsync(canvas, outputPin, coherent: false);
        AssertSpectraMatchDb(flatOff, groupedOff, IncoherentToleranceDb, "incoherent grouped");
    }

    /// <summary>
    /// Groups splitter and combiner via the real grouping command, so both MZI arms
    /// (connections with both endpoints inside the selection) become frozen paths.
    /// </summary>
    private static void GroupMziBody(DesignCanvasViewModel canvas)
    {
        var bodyVms = canvas.Components
            .Where(c => c.Component.Identifier is "mzi_splitter" or "mzi_combiner")
            .ToList();
        bodyVms.Count.ShouldBe(2, "the example must contain the MZI body components");

        new CreateGroupCommand(canvas, bodyVms).Execute();

        var group = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        group.InternalPaths.Count.ShouldBe(2, "both MZI arms must be frozen inside the group");
    }

    private static void AssertSpectraMatch(
        (double[] WavelengthsNm, double[] Power) reference,
        (double[] WavelengthsNm, double[] Power) actual,
        double deltaLMicrometers,
        string label)
    {
        AssertSpectraMatchDb(reference, actual, SpectrumToleranceDb, label);
        FindFringeMinimaIndices(actual.Power).Count.ShouldBeGreaterThanOrEqualTo(MinFringeMinima,
            $"the {label} spectrum must keep the interference fringes");

        double referenceFsr = AnalyzeFsr(reference, deltaLMicrometers);
        double actualFsr = AnalyzeFsr(actual, deltaLMicrometers);
        actualFsr.ShouldBe(referenceFsr, referenceFsr * FsrTolerance,
            $"the {label} FSR must match the flat design");
    }

    private static double AnalyzeFsr(
        (double[] WavelengthsNm, double[] Power) spectrum, double deltaLMicrometers) =>
        FringeAnalyzer.Analyze(
            new MeasuredSpectrum(spectrum.WavelengthsNm, spectrum.Power, "simulated"),
            deltaLMicrometers).MeanFsrNm;

    private static void AssertSpectraMatchDb(
        (double[] WavelengthsNm, double[] Power) reference,
        (double[] WavelengthsNm, double[] Power) actual,
        double toleranceDb,
        string label)
    {
        actual.WavelengthsNm.ShouldBe(reference.WavelengthsNm,
            $"the {label} sweep must use the same wavelength grid as the flat design");
        for (int i = 0; i < reference.Power.Length; i++)
        {
            double referenceDb = 10.0 * Math.Log10(Math.Max(reference.Power[i], 1e-12));
            double actualDb = 10.0 * Math.Log10(Math.Max(actual.Power[i], 1e-12));
            Math.Abs(actualDb - referenceDb).ShouldBeLessThanOrEqualTo(toleranceDb,
                $"the {label} spectrum must match the flat design at {reference.WavelengthsNm[i]} nm");
        }
    }

    private static async Task SaveToFile(FileOperationsViewModel fileOps, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        fileOps.FileDialogService = dialog.Object;
        await fileOps.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("the grouped design must be written to disk");
    }
}
