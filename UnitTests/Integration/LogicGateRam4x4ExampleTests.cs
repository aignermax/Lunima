using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration.RamScale;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pinned tests for the shipped <c>examples/Logic Gate RAM 4x4.lun</c> (issue #1408,
/// rung 5 of the memory ladder — the ISA-sized data memory: <c>IsaMachine.RamWords</c>
/// is four words × four bits): four words × four bits, composed hierarchically after
/// the #1366 spike with the re-floorplanned word cell (#1400) — routed once, frozen and
/// instanced four times (<c>CELL0</c>–<c>CELL3</c>), the top level carrying only the
/// 2-bit address decode, the LOAD/data copy trees, the read-MUX combines and the 84
/// inter-cell wires, all shipped with cached routes so opening the file never re-routes.
/// The signal names persist the network identity (issue #1025): address <c>A0</c>,
/// <c>A1</c>, <c>LOAD</c>, data <c>D0</c>–<c>D3</c>, read taps <c>Q0</c>–<c>Q3</c>; the
/// sixteen register bits live nested inside the cell instances and surface through the
/// production <see cref="LogicNetworkAssembler"/> (enabled by #1383). The blocked
/// inter-cell wires of the top-level route are pinned honestly in
/// <c>ExampleLoadRoutingTests.KnownBlockedWires</c> — they degrade no logic signal: the
/// behaviour proof below runs over the assembled network.
/// </summary>
public class LogicGateRam4x4ExampleTests
    : IClassFixture<LogicGateRam4x4ExampleTests.Ram4x4Fixture>
{
    private const string LoadSignal = "LOAD";
    private const int WordCount = 4;
    private const int BitCount = 4;
    private const int TopLevelGroupCount = 55;
    private const int TopLevelWireCount = 84;

    private static readonly string[] AddressSignals = { "A0", "A1" };
    private static readonly string[] DataSignals = { "D0", "D1", "D2", "D3" };
    private static readonly string[] ReadTaps = { "Q0", "Q1", "Q2", "Q3" };
    private static readonly string[] CellNames = { "CELL0", "CELL1", "CELL2", "CELL3" };
    private static readonly LogicPinRef[] RegisterRefs = Enumerable
        .Range(0, WordCount * BitCount)
        .Select(i => new LogicPinRef($"CELL{i / BitCount}/REG{i / BitCount}{i % BitCount}", "Y"))
        .ToArray();

    private readonly Ram4x4Fixture _fixture;

    /// <summary>Attaches the shared loaded example.</summary>
    public LogicGateRam4x4ExampleTests(Ram4x4Fixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsFromCache_UnderThirtySeconds_WithOnlyInterCellWiresAtTopLevel()
    {
        _fixture.LoadDuration.ShouldBeLessThan(TimeSpan.FromSeconds(30),
            "the cached routes must keep the open under the loose CI bound of 30 s (target: 10 s)");

        _fixture.Canvas.Components.Count.ShouldBe(TopLevelGroupCount,
            "top level: the address stage, the copy trees, the read-MUX combines and the four cell instances");
        _fixture.Canvas.Connections.Count.ShouldBe(TopLevelWireCount,
            "only the inter-cell wires load as connections — the intra-cell wiring is frozen inside the cells");

        var groups = _fixture.Canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList();
        foreach (var cellName in CellNames)
            groups.Select(g => g.GroupName).ShouldContain(cellName);
        foreach (var cell in groups.Where(g => g.GroupName.StartsWith("CELL", StringComparison.Ordinal)))
        {
            cell.TruthTablePinAssignment.ShouldBeNull("a cell instance is a plain group, not a gate");
            cell.ChildComponents.OfType<ComponentGroup>().ShouldAllBe(
                g => g.TruthTablePinAssignment != null,
                $"every gate nested inside {cell.GroupName} keeps its persisted pin roles after load");
        }
    }

    [Fact]
    public void AssembledNetwork_ExposesAddressLoadDataToggles_QTaps_AndSixteenNestedRegisters()
    {
        _fixture.Network.InputPinNames.ShouldBe(
            AddressSignals.Concat(new[] { LoadSignal }).Concat(DataSignals).ToArray(),
            ignoreOrder: true,
            customMessage: "the signal names merge the unconnected pins into the address, LOAD and data inputs (#1025)");
        foreach (var tap in ReadTaps)
            _fixture.Network.OutputPinNames.ShouldContain(tap);
        _fixture.Network.RegisterState.Keys.ShouldBe(RegisterRefs, ignoreOrder: true,
            customMessage: "the sixteen register bits hidden inside the four cell instances are committed register state");
    }

    [Fact]
    public void StepSequence_StoresDistinctWords_ReadsAllBack_HoldsOnLoadLow()
    {
        var network = _fixture.Network;

        // Pin the starting state (fact order is unspecified): clear all four words.
        foreach (var address in new[] { 0, 1, 2, 3 })
            StoreWord(network, address, data: 0);
        for (var address = 0; address < WordCount; address++)
            ReadWord(network, address).ShouldBe(0, $"word {address} cleared");

        var patterns = new[] { 3, 5, 10, 12 };
        for (var address = 0; address < WordCount; address++)
        {
            StoreWord(network, address, patterns[address]);
            for (var read = 0; read < WordCount; read++)
            {
                int expected = read <= address ? patterns[read] : 0;
                ReadWord(network, read).ShouldBe(expected,
                    $"after storing word {address}: word {read} answers {expected} — the addressed word alone commits");
            }
        }

        network.Evaluate(Bits(address: 0, load: false, data: 0));
        network.Step();
        network.Step();
        for (var address = 0; address < WordCount; address++)
            ReadWord(network, address).ShouldBe(patterns[address],
                $"LOAD=0: word {address} holds {patterns[address]} across two steps");
    }

    /// <summary>Reads the 4-bit word at the address: LOAD low, one evaluate, Q as a decimal.</summary>
    private static int ReadWord(LogicNetworkEvaluator network, int address)
    {
        var read = network.Evaluate(Bits(address, load: false, data: 0));
        int value = 0;
        for (int i = 0; i < BitCount; i++)
            if (read[ReadTaps[i]])
                value |= 1 << i;
        return value;
    }

    /// <summary>Stores one 4-bit word at the address: A + LOAD + D, one clock commit.</summary>
    private static void StoreWord(LogicNetworkEvaluator network, int address, int data)
    {
        network.Evaluate(Bits(address, load: true, data));
        network.Step();
    }

    /// <summary>The network input bits for one A/LOAD/data triple — one bit per signal (issue #1025).</summary>
    private static Dictionary<string, bool> Bits(int address, bool load, int data)
    {
        var bits = new Dictionary<string, bool>
        {
            [AddressSignals[0]] = (address & 1) != 0,
            [AddressSignals[1]] = (address & 2) != 0,
            [LoadSignal] = load,
        };
        for (int i = 0; i < BitCount; i++)
            bits[DataSignals[i]] = (data & (1 << i)) != 0;
        return bits;
    }

    /// <summary>
    /// Shared fixture: loads the shipped example once through the real load path and
    /// assembles its logic network through the production
    /// <see cref="LogicNetworkAssembler"/> exactly as the Logic panel runs it, so every
    /// fact asserts against the same loaded design and network.
    /// </summary>
    public class Ram4x4Fixture : IAsyncLifetime
    {
        private const int WavelengthNm = 1550;

        /// <summary>The canvas the shipped example loaded onto.</summary>
        public DesignCanvasViewModel Canvas { get; private set; } = null!;

        /// <summary>The logic network assembled from the loaded design.</summary>
        public LogicNetworkEvaluator Network { get; private set; } = null!;

        /// <summary>Wall-clock time of the load plus the post-load pass (cached routes: no routing).</summary>
        public TimeSpan LoadDuration { get; private set; }

        /// <summary>Loads the shipped example and assembles its logic network.</summary>
        public async Task InitializeAsync()
        {
            var path = Path.Combine(
                ExampleDesignFilesTests.ExamplesDirectory(), Ram4x4ExampleAuthoringTests.ExampleFileName);
            Canvas = new DesignCanvasViewModel();
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(Canvas);
            fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(Canvas, w, h);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
                $"'{Ram4x4ExampleAuthoringTests.ExampleFileName}' must load through the real load path");
            await fileOps.PostLoadRouting;
            watch.Stop();
            LoadDuration = watch.Elapsed;

            Network = await new LogicNetworkAssembler().AssembleAsync(
                Canvas.Components.Select(c => c.Component).ToList(),
                Canvas.Connections.Select(c => c.Connection).ToList(),
                WavelengthNm);
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;
    }
}
