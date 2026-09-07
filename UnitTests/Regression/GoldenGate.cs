using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;

namespace UnitTests.Regression;

/// <summary>
/// Golden-design gate pipeline: loads a shipped example, re-routes
/// every connection, asserts the design is clean (no red routes, no DRC violations,
/// no unintended unconnected pins, in-bounds, no overlaps), then runs the CW
/// wavelength sweep from the manifest setup and measures the declared outputs.
/// Truth-table rows come from authored expectations in the golden JSON — they are
/// evaluated here but never auto-generated.
/// </summary>
public static class GoldenGate
{
    /// <summary>Span used for the two-point probe sweep of a truth row; only the first point is read.</summary>
    private const int TruthProbeSpanNm = 2;

    /// <summary>Runs the full gate for one manifest entry.</summary>
    /// <param name="entry">Manifest entry (sweep setup + declared inputs/outputs).</param>
    /// <param name="repositoryRoot">Repository root from <see cref="GoldenManifest.LocateRepositoryRoot"/>.</param>
    /// <param name="existing">Previously pinned golden (truth rows are re-evaluated), or null.</param>
    public static async Task<GoldenGateResult> RunAsync(
        GoldenManifestEntry entry, string repositoryRoot, GoldenReference? existing)
    {
        var result = new GoldenGateResult();
        var canvas = await LoadDesignAsync(entry, repositoryRoot);
        if (canvas == null)
        {
            result.Violations.Add($"'{entry.Name}' failed to load");
            return result;
        }

        var allComponents = SimulationService.GetAllComponentsRecursively(canvas.Components);
        var inputs = GoldenPinResolver.ResolveInputs(entry.Inputs, allComponents, result);
        var outputs = GoldenPinResolver.ResolveOutputs(entry.Outputs, allComponents, result);
        if (result.Violations.Count > 0) return result;

        canvas.ConnectionManager.RecalculateAllTransmissions(null, CancellationToken.None);
        CheckDesignIntegrity(canvas, entry, result);
        if (result.Violations.Count > 0) return result;

        var tileManager = BuildTileManager(canvas);
        await RunSpectrumAsync(entry, canvas, tileManager, inputs, outputs, result);
        if (existing is { TruthTable.Count: > 0 })
            await ProbeTruthRowsAsync(existing, canvas, tileManager, allComponents, result);
        return result;
    }

    private static async Task<DesignCanvasViewModel?> LoadDesignAsync(
        GoldenManifestEntry entry, string repositoryRoot)
    {
        var library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
        var canvas = new DesignCanvasViewModel();
        var fileOps = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            library,
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!);

        var path = Path.Combine(GoldenManifest.ExamplesDirectory(repositoryRoot), entry.File);
        return await fileOps.LoadDesignFromPathAsync(path) ? canvas : null;
    }

    private static void CheckDesignIntegrity(
        DesignCanvasViewModel canvas, GoldenManifestEntry entry, GoldenGateResult result)
    {
        var connections = canvas.ConnectionManager.Connections;
        foreach (var connection in connections)
        {
            if (!connection.IsPathValid)
                result.Violations.Add($"red route {GoldenPinResolver.PinName(connection.StartPin)} → "
                    + $"{GoldenPinResolver.PinName(connection.EndPin)}");
        }

        var topLevel = canvas.Components.Select(c => c.Component).ToList();
        var groups = topLevel.OfType<ComponentGroup>().ToList();
        var validator = new DesignValidator();
        foreach (var issue in validator.Validate(connections, groups))
            result.Violations.Add($"DRC: {issue.Description}");
        foreach (var issue in validator.ValidateComponentBounds(topLevel, canvas.ChipMaxX, canvas.ChipMaxY))
            result.Violations.Add($"bounds: {issue.Description}");

        foreach (var compVm in canvas.Components)
        {
            if (!canvas.Placement.CanPlaceComponent(compVm.X, compVm.Y, compVm.Width, compVm.Height, compVm))
                result.Violations.Add($"overlap: '{compVm.Name}' overlaps another component");
        }

        CheckUnconnectedPins(canvas, entry, connections, result);
    }

    private static void CheckUnconnectedPins(
        DesignCanvasViewModel canvas,
        GoldenManifestEntry entry,
        List<WaveguideConnection> connections,
        GoldenGateResult result)
    {
        var connected = new HashSet<PhysicalPin>();
        foreach (var connection in connections)
        {
            connected.Add(connection.StartPin);
            connected.Add(connection.EndPin);
        }
        var declared = entry.Inputs.Select(i => i.Pin).Concat(entry.Outputs)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var component in SimulationService.GetAllComponentsRecursively(canvas.Components))
        {
            foreach (var pin in component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light) continue;
                if (connected.Contains(pin)) continue;
                string pinRef = $"{component.Identifier}.{pin.Name}";
                if (!declared.Contains(pinRef))
                    result.Violations.Add($"unconnected pin '{pinRef}' is neither wired nor declared "
                        + "in examples/examples.json inputs/outputs");
            }
        }
    }

    private static ComponentListTileManager BuildTileManager(DesignCanvasViewModel canvas)
    {
        var tileManager = new ComponentListTileManager();
        foreach (var compVm in canvas.Components)
            tileManager.AddComponent(compVm.Component);
        return tileManager;
    }

    private static async Task RunSpectrumAsync(
        GoldenManifestEntry entry,
        DesignCanvasViewModel canvas,
        ComponentListTileManager tileManager,
        List<ResolvedInput> inputs,
        List<ResolvedPin> outputs,
        GoldenGateResult result)
    {
        var portManager = new PhysicalExternalPortManager();
        GoldenPinResolver.AttachInputs(portManager, inputs);
        var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
        var config = new WavelengthSweepConfiguration(entry.StartNm, entry.EndNm, entry.StepCount);
        var sweeper = new WavelengthSweeper(new SystemMatrixBuilder(grid), portManager);
        var sweep = await sweeper.RunSweepAsync(config, grid);

        result.WavelengthsNm = sweep.GetWavelengthValues();
        foreach (var output in outputs)
        {
            result.Transmission[output.Reference] = sweep
                .GetInsertionLossSeriesForPin(output.Pin.LogicalPin!.IDInFlow)
                .Select(TransmissionSpectrumBuilder.DbToLinear)
                .ToArray();
        }
    }

    private static async Task ProbeTruthRowsAsync(
        GoldenReference existing,
        DesignCanvasViewModel canvas,
        ComponentListTileManager tileManager,
        List<Component> allComponents,
        GoldenGateResult result)
    {
        foreach (var row in existing.TruthTable)
        {
            var rowInputs = GoldenPinResolver.ResolveInputs(row.Inputs, allComponents, result);
            var rowOutputs = GoldenPinResolver.ResolveOutputs(row.Expected.Keys, allComponents, result);
            if (result.Violations.Count > 0) return;

            var portManager = new PhysicalExternalPortManager();
            GoldenPinResolver.AttachInputs(portManager, rowInputs);
            var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
            // Two-point sweep; the first sample lands exactly on the row's wavelength.
            var config = new WavelengthSweepConfiguration(
                row.WavelengthNm, row.WavelengthNm + TruthProbeSpanNm, 2);
            var sweeper = new WavelengthSweeper(new SystemMatrixBuilder(grid), portManager);
            var sweep = await sweeper.RunSweepAsync(config, grid);

            var probe = sweep.DataPoints[0];
            var actuals = new Dictionary<string, double>();
            foreach (var output in rowOutputs)
            {
                probe.InsertionLossDb.TryGetValue(output.Pin.LogicalPin!.IDInFlow, out var lossDb);
                actuals[output.Reference] = TransmissionSpectrumBuilder.DbToLinear(lossDb);
            }
            result.TruthActuals.Add(actuals);
        }
    }
}
