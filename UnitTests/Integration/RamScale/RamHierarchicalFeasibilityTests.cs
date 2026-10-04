using System.Diagnostics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
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
/// timeout is a result, not a failure. Behaviour (store/read/hold, 16-value sweep,
/// isolation) is asserted through the real assembler/evaluator via the test-side
/// <see cref="RamHierarchicalNetwork"/> adapter, and must match the flat RAM exactly —
/// same gates, same signals, same read taps. Numbers land in
/// <c>docs/logic/RAM-HIERARCHICAL-SPIKE.md</c>.
/// </summary>
[Trait("Category", "Slow")]
public class RamHierarchicalFeasibilityTests : IClassFixture<RamWordCellFixture>
{
    private const int BitCount = 4;

    private readonly RamWordCellFixture _cell;
    private readonly ITestOutputHelper _output;

    /// <summary>Attaches the shared routed cell and the test output sink.</summary>
    public RamHierarchicalFeasibilityTests(RamWordCellFixture cell, ITestOutputHelper output)
    {
        _cell = cell;
        _output = output;
    }

    [Fact]
    public Task Ram2Words4Bits_Hierarchical_AssemblyBehaviorAndRoute_Measured() =>
        MeasureAsync(words: 2, expectedGates: 71, expectedTopLevelWires: 9, expectedTopLevelGroups: 7);

    [Fact]
    public Task Ram4Words4Bits_Hierarchical_AssemblyBehaviorAndRoute_Measured() =>
        MeasureAsync(words: 4, expectedGates: 183, expectedTopLevelWires: 84, expectedTopLevelGroups: 55);

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

            var route = await Ram4x4FeasibilityTests.MeasureFullRoute(canvas, fileOps, label, Report);
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

    /// <summary>Assembles the logic network through the hierarchical adapter and times it.</summary>
    private async Task<LogicNetworkEvaluator> MeasureAssembly(DesignCanvasViewModel canvas, RamScaleDesign design, string label)
    {
        var watch = Stopwatch.StartNew();
        var network = await RamHierarchicalNetwork.Assemble(canvas, design.Json);
        watch.Stop();
        Report($"[ram-hier-spike] {label}: assemble={watch.Elapsed.TotalSeconds:F1}s "
            + $"inputs={network.InputPinNames.Count} outputs={network.OutputPinNames.Count} "
            + $"registers={network.RegisterState.Count}");
        return network;
    }

    private void Report(string line)
    {
        _output.WriteLine(line);
        Console.WriteLine(line);
    }
}

/// <summary>
/// The one-time half of the hierarchical spike: builds the word cell, loads it, routes
/// its intra-cell wires once, and freezes the routed result into the instancing
/// <see cref="RamWordCellTemplate"/> both facts share. The cell must route fully — an
/// unrouted wire would freeze into the template — so the bound is generous and a timeout
/// fails loudly instead of degrading to a partial measurement.
/// </summary>
public sealed class RamWordCellFixture : IAsyncLifetime
{
    private const int BitCount = 4;
    private const double DefaultCellRouteTimeoutSeconds = 300;
    private const string CellRouteTimeoutVariable = "CAP_RAM_SPIKE_CELL_TIMEOUT_S";

    /// <summary>The built (unrouted) word-cell design — census and roles.</summary>
    public RamWordCellDesign Design { get; private set; } = null!;

    /// <summary>The frozen instancing template extracted from the routed cell.</summary>
    public RamWordCellTemplate Template { get; private set; } = null!;

    /// <summary>The cell route measurement line for the spike report.</summary>
    public string RouteReport { get; private set; } = "";

    /// <summary>Builds, loads and routes the word cell once, then freezes the template.</summary>
    public async Task InitializeAsync()
    {
        Design = RamWordCellBuilder.Build(BitCount);
        Design.GateCount.ShouldBe(33, "the word-cell gate census is pinned");
        Design.WireCount.ShouldBe(44, "the intra-cell wire census is pinned");

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
