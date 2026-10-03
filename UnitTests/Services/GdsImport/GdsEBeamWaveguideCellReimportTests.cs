using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Export;
using UnitTests.Export.OpenEbl;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Export/import symmetry gate for the SiEPIC <c>Waveguide_&lt;n&gt;</c> cells
/// (#1347 / #1351 review): the shipped EBeam MZI example is exported through the
/// real nazca + klayout EBeam pipeline (which wraps every route in a waveguide
/// cell) and hierarchy-explode re-imported. Both waveguide-cell sources are
/// covered — routed connections (#1347, flat example) and frozen group paths
/// (#1351, MZI body grouped) — and in both cases the import must dissolve the
/// cells as route geometry: <c>PlacedCount</c> equals the example's component
/// count, no component is named <c>Waveguide_*</c>, and the four routes come
/// back as real connections (or, degraded, as frozen route geometry — never
/// lost, never a phantom component).
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk (installed on
/// the CI runner); skips cleanly elsewhere.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class GdsEBeamWaveguideCellReimportTests : IDisposable
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const string TopCell = "ConnectAPIC_Design";

    /// <summary>The example's physical truth: 2 grating couplers + 2 Y-branches.</summary>
    private const int ExampleComponentCount = 4;

    /// <summary>gc_in→splitter, the two MZI arms, combiner→gc_out.</summary>
    private const int ExampleConnectionCount = 4;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-wg-reimport-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        catch { /* temp cleanup best effort */ }
        _host.Dispose();
    }

    [SkippableFact]
    public async Task FlatMzi_RoutedConnectionWaveguideCells_ReimportWithoutPhantoms()
    {
        await RunRoundTripAsync(groupMziBody: false, fileStem: "ebeam_mzi_flat_reimport");
    }

    [SkippableFact]
    public async Task GroupedMzi_FrozenGroupPathWaveguideCells_ReimportWithoutPhantoms()
    {
        await RunRoundTripAsync(groupMziBody: true, fileStem: "ebeam_mzi_grouped_reimport");
    }

    private async Task RunRoundTripAsync(bool groupMziBody, string fileStem)
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblCheckPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk (expected on CI).");

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;
        if (groupMziBody)
        {
            MziFringeAnalysis.GroupMziBody(canvas);
            await canvas.RecalculateRoutesAsync();
        }

        var gdsPath = await ExportWithRealEBeamPipelineAsync(python!, canvas, fileStem);
        var outcome = await ImportAsync(gdsPath);
        AssertNoWaveguidePhantoms(outcome);
        await AssertPlacementAsync(outcome);
    }

    /// <summary>
    /// Exports through the app's own exporter and renders the script under real
    /// nazca (klayout foundry-cell upgrade + waveguide spine pass included).
    /// Pins the precondition first: all four example routes — routed connections
    /// and frozen group paths alike — must leave as <c>Waveguide_&lt;n&gt;</c> cells.
    /// </summary>
    private async Task<string> ExportWithRealEBeamPipelineAsync(
        string python, DesignCanvasViewModel canvas, string fileStem)
    {
        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty();
        exportWarnings.ShouldBeEmpty();
        Enumerable.Range(0, ExampleConnectionCount).ToList().ForEach(i =>
            script.ShouldContain($"name='Waveguide_{i}'",
                customMessage: "precondition: every example route exports as its own SiEPIC waveguide cell"));

        Directory.CreateDirectory(_root);
        var scriptPath = Path.Combine(_root, fileStem + ".py");
        await File.WriteAllTextAsync(scriptPath, script);
        var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, _root, scriptPath);
        run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
        var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
        File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");
        return gdsPath;
    }

    /// <summary>Hierarchy-explode import, first generation: every foundry cell unknown.</summary>
    private async Task<GdsImportOutcome> ImportAsync(string gdsPath) =>
        await _host.CreateService(() => Array.Empty<ComponentTemplate>())
            .ImportAsync(gdsPath, TopCell, new GdsHierarchyImportOptions());

    /// <summary>
    /// The waveguide cells dissolve into route geometry: only the example's four
    /// devices come back as instances/drafts, and the four routes are restored as
    /// connections or preserved as frozen route geometry — never dropped.
    /// </summary>
    private static void AssertNoWaveguidePhantoms(GdsImportOutcome outcome)
    {
        outcome.Instances.Count.ShouldBe(ExampleComponentCount,
            "every placed instance must be one of the example's devices");
        outcome.Instances.ShouldAllBe(i => !IsWaveguideCellName(i.CellName));
        outcome.RegisteredComponents.ShouldAllBe(r => !IsWaveguideCellName(r.CellDraftName));
        outcome.Infos.ShouldContain(i => i.Contains("dissolved"),
            "the Waveguide_<n> cells must go through route-cell dissolution");

        // The census also carries two top-cell fiber-port links (gc port labels);
        // the four device↔device links are the restored routes.
        var dump = string.Join("\n", outcome.Connections.Select(c =>
            $"{Describe(outcome, c.A)} <-> {Describe(outcome, c.B)}"));
        outcome.Connections.Count(c => !c.A.IsTopLevelPort && !c.B.IsTopLevelPort)
            .ShouldBe(ExampleConnectionCount,
                $"all four dissolved routes bridge exactly two device pins and reconnect:\n{dump}");
        outcome.TopCellWaveguidePolygons.ShouldBeEmpty(
            "every route polygon was consumed by the connection matcher — nothing froze");
    }

    /// <summary>
    /// Placement through the same executor the import dialog uses: PlacedCount
    /// equals the example's component count and no canvas component carries a
    /// <c>Waveguide_*</c> cell name.
    /// </summary>
    private async Task AssertPlacementAsync(GdsImportOutcome outcome)
    {
        var canvas = new DesignCanvasViewModel();
        var executor = new GdsPlacementExecutor(
            canvas, new CommandManager(), () => _host.Templates.ToList());
        var report = await executor.ExecuteAsync(GdsPlacementPlan.FromOutcome(outcome));

        report.PlacedCount.ShouldBe(ExampleComponentCount,
            "PlacedCount must equal the example's component count — no waveguide phantoms");
        report.SkippedPlacements.ShouldBeEmpty();
        report.ConnectedCount.ShouldBe(ExampleConnectionCount,
            "the four example routes are restored as real connections");
        report.FrozenRoutePathCount.ShouldBe(0);

        var group = canvas.Components.ShouldHaveSingleItem()
            .Component.ShouldBeOfType<ComponentGroup>();
        var children = group.GetAllComponentsRecursive().ToList();
        children.Count.ShouldBe(ExampleComponentCount);
        children.ShouldAllBe(c => !IsWaveguideCellName(c.HumanReadableName));
    }

    private static string Describe(GdsImportOutcome outcome, GdsPinEndpoint e) =>
        e.IsTopLevelPort
            ? $"PORT:{e.PinName}"
            : $"{outcome.Instances[e.InstanceIndex].InstanceName}.{e.PinName}";

    private static bool IsWaveguideCellName(string? name) =>
        name != null && name.StartsWith("Waveguide_", StringComparison.OrdinalIgnoreCase);
}
