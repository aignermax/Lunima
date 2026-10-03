using System.Collections.ObjectModel;
using System.Numerics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using Moq;
using Shouldly;

namespace UnitTests.Integration;

/// <summary>
/// Shared helpers for the EBeam Mach-Zehnder fringe proofs (issues #1319, #1327):
/// loading an example through the real load path, sweeping the output-coupler power
/// with the coherent propagation-phase mode toggled, and locating interference
/// nulls with sub-step parabolic refinement.
/// </summary>
internal static class MziFringeAnalysis
{
    /// <summary>A minimum counts as an interference null only when it drops below this
    /// fraction of the surrounding power — the GC/Y-branch bandpass ripple stays shallow.</summary>
    internal const double FringeDepthRatio = 0.1;

    /// <summary>Half-width (in samples) of the neighbourhood a fringe null is compared against.</summary>
    internal const int FringeNeighborhoodSamples = 7;

    internal static double ExpectedFsrNm(double wavelengthNm, double groupIndex, double deltaLMicrometers) =>
        wavelengthNm * wavelengthNm / (groupIndex * deltaLMicrometers * 1000.0);

    internal static double MeasureArmLengthDifference(DesignCanvasViewModel canvas)
    {
        double upperArm = FindConnection(canvas, "mzi_splitter", "port 2").PathLengthMicrometers;
        double lowerArm = FindConnection(canvas, "mzi_splitter", "port 3").PathLengthMicrometers;
        double deltaL = lowerArm - upperArm;
        deltaL.ShouldBeGreaterThan(0, "the meander arm must be the longer one");
        return deltaL;
    }

    internal static async Task<(double[] WavelengthsNm, double[] Power)> SweepOutputPowerAsync(
        DesignCanvasViewModel canvas, PhysicalPin outputPin, bool coherent,
        int sweepStartNm = 1500, int sweepEndNm = 1600, int sweepStepCount = 201)
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
                new WavelengthSweepConfiguration(sweepStartNm, sweepEndNm, sweepStepCount), grid);

            var power = sweep.GetInsertionLossSeriesForPin(outputPin.LogicalPin!.IDInFlow)
                .Select(TransmissionSpectrumBuilder.DbToLinear).ToArray();
            return (sweep.GetWavelengthValues(), power);
        }
        finally
        {
            canvas.ConnectionManager.EnableCoherentPropagationPhase = false;
        }
    }

    internal static List<int> FindFringeMinimaIndices(double[] power, int window = 3)
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

    internal static double RefineMinimumWavelength(double[] wavelengthsNm, double[] power, int index)
    {
        double stepNm = wavelengthsNm[index + 1] - wavelengthsNm[index - 1];
        double denominator = power[index - 1] - 2.0 * power[index] + power[index + 1];
        if (denominator <= 0)
            return wavelengthsNm[index];
        double offset = 0.5 * (power[index - 1] - power[index + 1]) / denominator;
        return wavelengthsNm[index] + offset * stepNm / 2.0;
    }

    internal static Component FindComponent(DesignCanvasViewModel canvas, string identifier) =>
        canvas.Components.Single(c => c.Component.Identifier == identifier).Component;

    internal static PhysicalPin FindPin(Component component, string pinName) =>
        component.PhysicalPins.Single(p => p.Name == pinName);

    internal static WaveguideConnection FindConnection(
        DesignCanvasViewModel canvas, string startComponentId, string startPinName) =>
        canvas.Connections.Single(c =>
            c.Connection.StartPin?.ParentComponent.Identifier == startComponentId
            && c.Connection.StartPin?.Name == startPinName).Connection;

    internal static Task<(DesignCanvasViewModel Canvas, FileOperationsViewModel FileOps, ErrorConsoleService ErrorConsole)>
        LoadExample(string exampleFileName) =>
        LoadDesignFromPath(Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName));

    internal static async Task<(DesignCanvasViewModel Canvas, FileOperationsViewModel FileOps, ErrorConsoleService ErrorConsole)>
        LoadDesignFromPath(string path)
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

        (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
            $"'{path}' must load through the real load path");
        return (canvas, fileOps, errorConsole);
    }
}
