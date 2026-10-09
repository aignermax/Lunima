using System.Diagnostics;
using System.Text.Json.Nodes;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Rung-5 spike (issue #1366): the RAM built hierarchically — one word cell routed once
/// and frozen into an instanced group template, only the inter-cell wires routed at top
/// level — measured against the flat <see cref="Ram4x4FeasibilityTests"/> numbers. The
/// cell route (a one-time cost, amortized over every instance) runs in the shared fixture
/// with its own bound (<c>CAP_RAM_SPIKE_CELL_TIMEOUT_S</c>, default 300 s): the cell must
/// route fully or there is no template. The top-level route of the inter-cell wires uses
/// the spike's standard bound (<c>CAP_RAM_SPIKE_ROUTE_TIMEOUT_S</c>, default 60 s) — a
/// timeout is a result, not a failure — and only runs with <c>CAP_RAM_SPIKE_MEASURE_ROUTE=1</c>;
/// by default it is cancelled at once, the logic never depends on it. Behaviour (store/read/hold, 16-value sweep,
/// isolation) is asserted through the real assembler/evaluator over the canvas exactly
/// as the Logic panel runs it, and must match the flat RAM exactly —
/// same gates, same signals, same read taps. Numbers land in
/// <c>docs/logic/RAM-HIERARCHICAL-SPIKE.md</c>.
/// </summary>
[Trait("Category", "Slow")]
public class RamHierarchicalFeasibilityTests : IClassFixture<RamWordCellFixture>
{
    private const int BitCount = 4;
    private const int WavelengthNm = 1550;

    private readonly RamWordCellFixture _cell;
    private readonly ITestOutputHelper _output;

    /// <summary>Attaches the shared routed cell and the test output sink.</summary>
    public RamHierarchicalFeasibilityTests(RamWordCellFixture cell, ITestOutputHelper output)
    {
        _cell = cell;
        _output = output;
    }

    /// <summary>
    /// The word-cell floorplan budget (issue #1400): the blocked intra-cell wires of the
    /// routed cell, replicated into every instance. The re-floorplan (channel row per bit
    /// slice, hand-placed tree copies, wires emitted in route-priority order) brought the
    /// count from 17 of 44 down to 9 in the five iterations the issue budgets; the ≤3
    /// target was not reached — the remaining blocks are contention-repair stamps on
    /// forced crossings (the load-tree trunks, the leaf down-hops and the one
    /// cell-spanning select wire), not gate obstacles. The crossing bake then connected eight
    /// of them through placed crossings or freed-up routes, leaving 1. The pin guards
    /// against regressions.
    /// </summary>
    [Fact]
    public void WordCell_BlockedIntraCellWires_WithinFloorplanBudget()
    {
        _cell.BlockedCount.ShouldBeGreaterThan(0,
            "a zero count means the shipped example lost the blocked flags again — the pin must never pass vacuously");
        _cell.BlockedCount.ShouldBe(1,
            "the crossing-baked word cell ships exactly 1 blocked intra-cell wire (17 before the re-floorplan, 9 before the bake)");
    }

    [Fact]
    public Task Ram2Words4Bits_Hierarchical_AssemblyAndBehavior() =>
        MeasureAsync(words: 2, expectedGates: 71, expectedTopLevelWires: 9, expectedTopLevelGroups: 7);

    [Fact]
    public Task Ram4Words4Bits_Hierarchical_AssemblyAndBehavior() =>
        MeasureAsync(words: 4, expectedGates: 183, expectedTopLevelWires: 84, expectedTopLevelGroups: 55);

    /// <summary>
    /// The production-path proof of the spike's product gap: the hierarchical 2×4
    /// loads, and the plain <see cref="LogicNetworkAssembler"/> over the canvas's
    /// top-level components and connections exposes the nested word-cell gates —
    /// no test-side adapter. The 8 register bits hidden inside the two cell
    /// instances are committed register state, and the network shape matches the
    /// flat RAM exactly (same inputs, same outputs, same read taps).
    /// </summary>
    [Fact]
    public async Task ProductionAssemblyPath_HierarchicalRam_ExposesNestedRegisters()
    {
        const int words = 2;
        var design = RamHierarchicalDesignBuilder.Build(_cell.Template, words, BitCount);
        var tempPath = design.WriteToTempFile();
        try
        {
            var canvas = new DesignCanvasViewModel();
            Ram4x4FeasibilityTests.ApplyChipSize(canvas, design.ChipWidthMicrometers, design.ChipHeightMicrometers);
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
            fileOps.ApplyChipSizeAfterLoad = (width, height) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, width, height);
            (await fileOps.LoadDesignFromPathAsync(tempPath)).ShouldBeTrue(
                "the hierarchical RAM must load onto the canvas");
            await Ram4x4FeasibilityTests.SettlePostLoadRouting(canvas, fileOps, "RAM 2x4 hierarchical", Report);

            var cellInstances = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>()
                .Where(g => g.TruthTablePinAssignment == null).ToList();
            cellInstances.ShouldNotBeEmpty("the cell instances load as plain groups");
            cellInstances.SelectMany(g => g.ChildComponents.OfType<ComponentGroup>())
                .ShouldAllBe(g => g.TruthTablePinAssignment != null,
                    "every gate nested inside a cell instance keeps its persisted pin roles after load");

            var network = await AssembleProductionPath(canvas);

            network.Gates.Count.ShouldBe(design.GateCount,
                "every gate nested inside the cell instances joins the network");
            network.RegisterState.Count.ShouldBe(words * BitCount,
                "the register bits hidden inside the cell instances are committed register state");
            Ram4x4FeasibilityTests.AssertNetworkShape(network, design, words, BitCount);
            Ram4x4FeasibilityTests.AssertStoreReadHold(network, design, words, BitCount);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private async Task MeasureAsync(int words, int expectedGates, int expectedTopLevelWires, int expectedTopLevelGroups)
    {
        string label = $"RAM {words}x{BitCount} hierarchical";
        var buildWatch = Stopwatch.StartNew();
        var design = RamHierarchicalDesignBuilder.Build(_cell.Template, words, BitCount);
        buildWatch.Stop();
        design.GateCount.ShouldBe(expectedGates, "the hierarchical gate census matches the flat RAM gate for gate");
        design.WireCount.ShouldBe(expectedTopLevelWires, "only the inter-cell wires remain at top level");

        var tempPath = design.WriteToTempFile();
        try
        {
            var canvas = new DesignCanvasViewModel();
            Ram4x4FeasibilityTests.ApplyChipSize(canvas, design.ChipWidthMicrometers, design.ChipHeightMicrometers);
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
            fileOps.ApplyChipSizeAfterLoad = (width, height) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, width, height);

            var loadWatch = Stopwatch.StartNew();
            (await fileOps.LoadDesignFromPathAsync(tempPath)).ShouldBeTrue($"'{label}' must load onto the canvas");
            loadWatch.Stop();
            canvas.Components.Count.ShouldBe(expectedTopLevelGroups,
                "top level: address stage, distribution, read mux and the cell instances — the word gates are nested");
            canvas.Connections.Count.ShouldBe(design.WireCount, "only the inter-cell wires load as connections");

            var route = await Ram4x4FeasibilityTests.SettlePostLoadRouting(canvas, fileOps, label, Report);
            var network = await MeasureAssembly(canvas, design, label);
            Ram4x4FeasibilityTests.AssertNetworkShape(network, design, words, BitCount);
            Ram4x4FeasibilityTests.AssertStoreReadHold(network, design, words, BitCount);

            Report($"[ram-hier-spike] {label}: gates={design.GateCount} topWires={design.WireCount} "
                + $"cellGates={_cell.Design.GateCount} cellWires={_cell.Design.WireCount} "
                + $"chip={design.ChipWidthMicrometers:F0}x{design.ChipHeightMicrometers:F0}um "
                + $"build={buildWatch.Elapsed.TotalSeconds:F1}s load={loadWatch.Elapsed.TotalSeconds:F1}s "
                + $"{_cell.RouteReport} {route}");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>Assembles the logic network through the production path and times it.</summary>
    private async Task<LogicNetworkEvaluator> MeasureAssembly(DesignCanvasViewModel canvas, RamScaleDesign design, string label)
    {
        var watch = Stopwatch.StartNew();
        var network = await AssembleProductionPath(canvas);
        watch.Stop();
        Report($"[ram-hier-spike] {label}: assemble={watch.Elapsed.TotalSeconds:F1}s "
            + $"inputs={network.InputPinNames.Count} outputs={network.OutputPinNames.Count} "
            + $"registers={network.RegisterState.Count}");
        return network;
    }

    /// <summary>
    /// The shipped logic pipeline, exactly as the Logic panel runs it: the plain
    /// assembler over the canvas's top-level components and connections.
    /// </summary>
    private static async Task<LogicNetworkEvaluator> AssembleProductionPath(DesignCanvasViewModel canvas)
    {
        var components = canvas.Components.Select(c => c.Component).ToList();
        var connections = canvas.Connections.Select(c => c.Connection).ToList();
        return await new LogicNetworkAssembler().AssembleAsync(components, connections, WavelengthNm);
    }

    private void Report(string line)
    {
        _output.WriteLine(line);
        Console.WriteLine(line);
    }
}

/// <summary>
/// The one-time half of the hierarchical spike: the frozen instancing
/// <see cref="RamWordCellTemplate"/> both facts share. Default runs lift the pre-routed
/// cell from the shipped <c>examples/Logic Gate RAM 2x4.lun</c> (its <c>CELL0</c> group
/// is exactly the routed + frozen cell since #1405), so no routing runs at all (#1409 —
/// the live re-route timed out on loaded CI runners). Setting
/// <c>CAP_RAM_SPIKE_REROUTE=1</c> restores the spike's measurement mode: the cell is
/// loaded, routed once (bound <c>CAP_RAM_SPIKE_CELL_TIMEOUT_S</c>, default 300 s — the
/// cell must route fully or there is no template) and the routed result frozen.
/// </summary>
public sealed class RamWordCellFixture : IAsyncLifetime
{
    private const int BitCount = 4;
    private const double DefaultCellRouteTimeoutSeconds = 300;
    private const string CellRouteTimeoutVariable = "CAP_RAM_SPIKE_CELL_TIMEOUT_S";
    private const string RerouteVariable = "CAP_RAM_SPIKE_REROUTE";

    /// <summary>The built (unrouted) word-cell design — census and roles.</summary>
    public RamWordCellDesign Design { get; private set; } = null!;

    /// <summary>The frozen instancing template extracted from the routed cell.</summary>
    public RamWordCellTemplate Template { get; private set; } = null!;

    /// <summary>The cell route measurement line for the spike report.</summary>
    public string RouteReport { get; private set; } = "";

    /// <summary>The cell's blocked-fallback wire count — the number every instance replicates.</summary>
    public int BlockedCount { get; private set; }

    /// <summary>Freezes the word-cell template — from the shipped example by default, live-routed on demand.</summary>
    public async Task InitializeAsync()
    {
        Design = RamWordCellBuilder.Build(BitCount);
        Design.GateCount.ShouldBe(33, "the word-cell gate census is pinned");
        Design.WireCount.ShouldBe(44, "the intra-cell wire census is pinned");

        if (Environment.GetEnvironmentVariable(RerouteVariable) == "1")
        {
            await RouteCellLive();
            return;
        }

        var examplePath = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), Ram2x4ExampleAuthoringTests.ExampleFileName);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(examplePath))!.AsObject();
        Template = RamWordCellTemplate.FromExample(document, "CELL0", Design);
        BlockedCount = Template.BlockedFallbackCount;
        RouteReport = $"cellTemplate=shipped-example cellBlocked={BlockedCount}";
    }

    /// <summary>Loads and routes the word cell once, then freezes the routed template.</summary>
    private async Task RouteCellLive()
    {
        var tempPath = Design.WriteToTempFile();
        try
        {
            var canvas = new DesignCanvasViewModel();
            Ram4x4FeasibilityTests.ApplyChipSize(canvas, Design.ChipWidthMicrometers, Design.ChipHeightMicrometers);
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
            fileOps.ApplyChipSizeAfterLoad = (width, height) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, width, height);
            (await fileOps.LoadDesignFromPathAsync(tempPath)).ShouldBeTrue("the word cell must load onto the canvas");
            canvas.Components.Count.ShouldBe(Design.GateCount, "every cell gate group must load");
            canvas.Connections.Count.ShouldBe(Design.WireCount, "every intra-cell wire must load");

            var timeout = CellRouteTimeout;
            var watch = Stopwatch.StartNew();
            bool timedOut = false;
            try
            {
                await fileOps.PostLoadRouting.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                timedOut = true;
                canvas.Routing.CancelRouting();
                try
                {
                    await fileOps.PostLoadRouting.WaitAsync(TimeSpan.FromMinutes(2));
                }
                catch (TimeoutException)
                {
                    // The cancellation did not land; the counts below report the half-routed state.
                }
            }
            watch.Stop();

            int unrouted = canvas.Connections.Count(c => c.Connection.RoutedPath == null);
            int blocked = canvas.Connections.Count(c => c.Connection.IsBlockedFallback);
            timedOut.ShouldBeFalse($"the word cell must route within {timeout.TotalSeconds:F0}s — it is frozen into the template");
            unrouted.ShouldBe(0, "every intra-cell wire must carry a route before the template is frozen");
            BlockedCount = blocked;
            RouteReport = $"cellRoute={watch.Elapsed.TotalSeconds:F1}s cellBlocked={blocked} cellBound={timeout.TotalSeconds:F0}s";
            Template = RamWordCellTemplate.Extract(canvas, Design);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>Nothing to release — the fixture's canvases are garbage-collected.</summary>
    public Task DisposeAsync() => Task.CompletedTask;

    private static TimeSpan CellRouteTimeout =>
        double.TryParse(Environment.GetEnvironmentVariable(CellRouteTimeoutVariable), out double seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultCellRouteTimeoutSeconds);
}
