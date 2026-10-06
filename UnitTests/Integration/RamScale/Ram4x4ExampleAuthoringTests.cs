using System.Diagnostics;
using System.Text.Json.Nodes;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Authoring utility for the shipped rung-5 memory stone
/// <c>examples/Logic Gate RAM 4x4.lun</c> (issue #1408): the ISA-sized data memory —
/// four words × four bits matching <c>IsaMachine.RamWords</c> — composed hierarchically
/// exactly like the RAM 2x4 (issue #1389): the re-floorplanned 4-bit word cell (#1400)
/// routed once and frozen into the instancing <see cref="RamWordCellTemplate"/>, stamped
/// out four times (<c>CELL0</c>–<c>CELL3</c>), with the 2-bit address decode, the
/// LOAD/data copy trees and the read-mux combines plus the 84 inter-cell wires at top
/// level. The design is written into <c>examples/</c> unrouted, then loaded, routed and
/// saved through the real save path so the shipped file carries cached routes and opening
/// it never re-routes. Before saving, the spike's read taps are renamed onto the shipped
/// convention: <c>R0</c>–<c>R3</c> → <c>Q0</c>–<c>Q3</c> (the address keeps the two-bit
/// <c>A0</c>/<c>A1</c> naming). A verification reload assembles the saved file through the
/// production <see cref="LogicNetworkAssembler"/> and runs the full store/read/hold
/// behaviour proof, so a broken bake can never ship silently.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c> — CI must not route the RAM 4x4 on every run:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 python3 tools/smart_test.py Ram4x4ExampleAuthoring</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class Ram4x4ExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Logic Gate RAM 4x4.lun";

    private const int WordCount = 4;
    private const int BitCount = 4;
    private const int WavelengthNm = 1550;
    private const int TopLevelGroupCount = 55;
    private const int TopLevelWireCount = 84;
    private const int GateCount = 183;

    private static readonly TimeSpan CellRouteTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TopLevelRouteTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CancellationSettleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The spike's read-tap names mapped onto the shipped example's convention.</summary>
    private static readonly IReadOnlyDictionary<string, string> SignalRenames =
        new Dictionary<string, string>
        {
            ["R0"] = "Q0",
            ["R1"] = "Q1",
            ["R2"] = "Q2",
            ["R3"] = "Q3",
        };

    private readonly ITestOutputHelper _output;

    /// <summary>Attaches the test output sink.</summary>
    public Ram4x4ExampleAuthoringTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Author_LogicGateRam4x4_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var template = await RouteWordCellOnce();
        var design = RamHierarchicalDesignBuilder.Build(template, WordCount, BitCount);
        design.GateCount.ShouldBe(GateCount, "the hierarchical gate census is pinned by the spike");
        design.WireCount.ShouldBe(TopLevelWireCount, "only the inter-cell wires route at top level");

        var document = JsonNode.Parse(design.Json)!.AsObject();
        RenameSignals(document);

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        File.WriteAllText(examplePath, document.ToJsonString());

        int blocked = await RouteTopLevelAndSave(examplePath, design);
        await VerifySavedExample(examplePath, design);
        Report($"[author] {ExampleFileName}: topLevelBlocked={blocked} — pin this in ExampleLoadRoutingTests.KnownBlockedWires");
    }

    /// <summary>Builds the word cell, routes its intra-cell wires once and freezes the instancing template.</summary>
    private async Task<RamWordCellTemplate> RouteWordCellOnce()
    {
        var cellDesign = RamWordCellBuilder.Build(BitCount);
        var tempPath = cellDesign.WriteToTempFile();
        try
        {
            var canvas = new DesignCanvasViewModel();
            Ram4x4FeasibilityTests.ApplyChipSize(canvas, cellDesign.ChipWidthMicrometers, cellDesign.ChipHeightMicrometers);
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
            fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
            (await fileOps.LoadDesignFromPathAsync(tempPath)).ShouldBeTrue("the word cell must load onto the canvas");

            var watch = Stopwatch.StartNew();
            try
            {
                await fileOps.PostLoadRouting.WaitAsync(CellRouteTimeout);
            }
            catch (TimeoutException)
            {
                canvas.Routing.CancelRouting();
                try { await fileOps.PostLoadRouting.WaitAsync(CancellationSettleTimeout); }
                catch (TimeoutException) { /* the counts below report the half-routed state */ }
            }
            watch.Stop();

            int unrouted = canvas.Connections.Count(c => c.Connection.RoutedPath == null);
            int blocked = canvas.Connections.Count(c => c.Connection.IsBlockedFallback);
            unrouted.ShouldBe(0, "every intra-cell wire must carry a route before the template is frozen");
            Report($"[author] word cell: route={watch.Elapsed.TotalSeconds:F1}s blocked={blocked}");
            return RamWordCellTemplate.Extract(canvas, cellDesign);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>Loads the unrouted example file, routes the inter-cell wires and saves the cached routes back.</summary>
    private async Task<int> RouteTopLevelAndSave(string examplePath, RamScaleDesign design)
    {
        var canvas = new DesignCanvasViewModel();
        Ram4x4FeasibilityTests.ApplyChipSize(canvas, design.ChipWidthMicrometers, design.ChipHeightMicrometers);
        var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
        fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
        (await fileOps.LoadDesignFromPathAsync(examplePath)).ShouldBeTrue("the unrouted example must load");
        canvas.Components.Count.ShouldBe(TopLevelGroupCount,
            "address stage, copy trees, read mux and the four cell instances");
        canvas.Connections.Count.ShouldBe(TopLevelWireCount, "only the inter-cell wires load as connections");

        var watch = Stopwatch.StartNew();
        try
        {
            await fileOps.PostLoadRouting.WaitAsync(TopLevelRouteTimeout);
        }
        catch (TimeoutException)
        {
            Report($"[author] top level: route exceeded {TopLevelRouteTimeout.TotalMinutes:F0} min — cancelling and reporting the honest state");
            canvas.Routing.CancelRouting();
            try { await fileOps.PostLoadRouting.WaitAsync(CancellationSettleTimeout); }
            catch (TimeoutException) { /* the counts below report the half-routed state */ }
        }
        await canvas.RecalculateRoutesAsync();
        watch.Stop();

        int unrouted = canvas.Connections.Count(c => c.Connection.RoutedPath == null);
        int blocked = canvas.Connections.Count(c => c.Connection.IsBlockedFallback);
        unrouted.ShouldBe(0, "every inter-cell wire must carry a route after the top-level pass");
        Report($"[author] top level: route={watch.Elapsed.TotalSeconds:F1}s blocked={blocked}");

        await fileOps.SaveDesignCommand.ExecuteAsync(null);
        return blocked;
    }

    /// <summary>Reloads the saved file and proves the production assembly path plus the RAM behaviour.</summary>
    private async Task VerifySavedExample(string examplePath, RamScaleDesign design)
    {
        var canvas = new DesignCanvasViewModel();
        Ram4x4FeasibilityTests.ApplyChipSize(canvas, design.ChipWidthMicrometers, design.ChipHeightMicrometers);
        var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
        fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
        (await fileOps.LoadDesignFromPathAsync(examplePath)).ShouldBeTrue("the saved example must reload");
        await fileOps.PostLoadRouting;
        canvas.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0,
            "the saved file must reload fully routed from its cache");

        var network = await new LogicNetworkAssembler().AssembleAsync(
            canvas.Components.Select(c => c.Component).ToList(),
            canvas.Connections.Select(c => c.Connection).ToList(),
            WavelengthNm);
        network.RegisterState.Count.ShouldBe(WordCount * BitCount,
            "the register bits hidden inside the cell instances are committed register state");

        var renamed = new RamScaleDesign
        {
            Json = design.Json,
            GateCount = design.GateCount,
            WireCount = design.WireCount,
            ChipWidthMicrometers = design.ChipWidthMicrometers,
            ChipHeightMicrometers = design.ChipHeightMicrometers,
            AddressSignals = design.AddressSignals,
            DataSignals = design.DataSignals,
            ReadTaps = new[] { "Q0", "Q1", "Q2", "Q3" },
        };
        Ram4x4FeasibilityTests.AssertNetworkShape(network, renamed, WordCount, BitCount);
        Ram4x4FeasibilityTests.AssertStoreReadHold(network, renamed, WordCount, BitCount);
    }

    /// <summary>Renames the spike's read-tap names onto the shipped convention throughout the document.</summary>
    private static void RenameSignals(JsonObject document)
    {
        foreach (var entry in document["Groups"]!.AsArray())
        {
            if (entry?["TruthTablePinAssignment"] is not JsonObject assignment)
                continue;
            RenameValues(assignment, "InputSignalNames");
            RenameValues(assignment, "OutputSignalNames");
        }
    }

    private static void RenameValues(JsonObject assignment, string property)
    {
        if (assignment[property] is not JsonObject signals)
            return;
        foreach (var pin in signals.ToList())
        {
            if (pin.Value != null && SignalRenames.TryGetValue(pin.Value.GetValue<string>(), out var renamed))
                signals[pin.Key] = renamed;
        }
    }

    private void Report(string line)
    {
        _output.WriteLine(line);
        Console.WriteLine(line);
    }
}
