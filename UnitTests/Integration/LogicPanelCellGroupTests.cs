using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for the Logic panel's per-cell collapse (issue #1399): against the
/// shipped hierarchical <c>examples/Logic Gate RAM 2x4.lun</c> the gate rows nested
/// inside the two word-cell instances fold under one collapsed header per cell
/// (<c>CELL0 — 4 registers, … gates</c>), so the top-level list shows only the named
/// interface (the Q bus, top-level gates) plus the two cell headers — NAND2TETRIS-style,
/// internals one click away. Expanding a cell reveals its rows with the cell prefix
/// stripped. The flat <c>Logic Gate 4-Bit Adder.lun</c> keeps the exact row list it had
/// before — no cell groups, same order.
/// </summary>
public class LogicPanelCellGroupTests : IClassFixture<LogicPanelCellGroupTests.LoadedExamples>
{
    private const int MaxTopLevelRows = 15;

    private readonly LoadedExamples _fixture;

    /// <summary>Attaches the shared loaded examples.</summary>
    public LogicPanelCellGroupTests(LoadedExamples fixture) => _fixture = fixture;

    [Fact]
    public async Task BuildNetwork_Ram2x4_CollapsesNestedGateRowsUnderOneHeaderPerCell()
    {
        var vm = await BuildPanel(_fixture.RamCanvas);

        vm.OutputRows.Count.ShouldBeLessThanOrEqualTo(MaxTopLevelRows,
            "the memory reads by its interface: named I/O, buses and two cell headers, not ~70 internal wires");

        var groups = vm.OutputRows.OfType<LogicCellGroupViewModel>().ToList();
        groups.Select(g => g.CellName).ShouldBe(new[] { "CELL0", "CELL1" }, ignoreOrder: true);
        groups.ShouldAllBe(g => !g.IsExpanded, "cell internals are collapsed by default");
        foreach (var group in groups)
        {
            group.RegisterCount.ShouldBe(4, "each word cell hides four register bits");
            group.HeaderText.ShouldBe($"CELL{group.CellName[^1]} — 4 registers, {group.GateCount} gates");
            group.Members.ShouldNotBeEmpty();
        }

        vm.OutputRows.OfType<LogicNetworkOutputViewModel>()
            .ShouldAllBe(row => !row.PinName.Contains('/'),
                "no hierarchical gate row stays at top level — named outputs and top-level gates only");
        vm.OutputRows.OfType<LogicSignalBusOutputViewModel>().Single(b => b.Prefix == "Q")
            .ShouldNotBeNull("the named read bus stays on top, always visible");
        vm.RegisterStates.Count.ShouldBe(8,
            "the register readout stays visible above the collapsed cell rows");
    }

    [Fact]
    public async Task ExpandCell_Ram2x4_RevealsGateRowsWithPrefixStripped()
    {
        var vm = await BuildPanel(_fixture.RamCanvas);
        var cell0 = vm.OutputRows.OfType<LogicCellGroupViewModel>().Single(g => g.CellName == "CELL0");

        cell0.IsExpanded.ShouldBeFalse();
        cell0.ToggleExpandedCommand.Execute(null);
        cell0.IsExpanded.ShouldBeTrue();

        cell0.Members.ShouldAllBe(m => m.PinName.StartsWith("CELL0/", StringComparison.Ordinal));
        cell0.Members.ShouldAllBe(m => !m.DisplayName.Contains("CELL0"),
            "the expanded rows show REG10.Y, not CELL0/REG10.Y — the column stays narrow");
        cell0.Members.Select(m => m.DisplayName).ShouldContain("REG00.Y");
    }

    [Fact]
    public async Task BuildNetwork_FlatAdder_KeepsTheExactRowListAsBefore()
    {
        var vm = await BuildPanel(_fixture.AdderCanvas);

        vm.OutputRows.OfType<LogicCellGroupViewModel>().ShouldBeEmpty(
            "a flat design has no cell instances — nothing to collapse");
        var busMembers = vm.OutputRows.OfType<LogicSignalBusOutputViewModel>()
            .SelectMany(b => b.Members).ToHashSet();
        vm.OutputRows.OfType<LogicNetworkOutputViewModel>().Select(row => row.PinName)
            .ShouldBe(vm.Outputs.Where(o => !busMembers.Contains(o)).Select(o => o.PinName),
                "the flat example keeps the identical plain-row list as before, order included");
    }

    /// <summary>Builds the panel VM over a fixture canvas and assembles its network.</summary>
    private static async Task<LogicPanelViewModel> BuildPanel(DesignCanvasViewModel canvas)
    {
        var vm = new LogicPanelViewModel();
        vm.Configure(canvas);
        await vm.BuildNetworkCommand.ExecuteAsync(null);
        vm.HasNetwork.ShouldBeTrue(vm.StatusText);
        return vm;
    }

    /// <summary>
    /// Shared fixture: loads the shipped RAM 2x4 and the flat 4-bit adder through the
    /// real load path once; every test assembles its own network from those canvases.
    /// </summary>
    public class LoadedExamples : IAsyncLifetime
    {
        /// <summary>The canvas the shipped hierarchical RAM example loaded onto.</summary>
        public DesignCanvasViewModel RamCanvas { get; private set; } = null!;

        /// <summary>The canvas the shipped flat 4-bit adder loaded onto.</summary>
        public DesignCanvasViewModel AdderCanvas { get; private set; } = null!;

        /// <summary>Loads both shipped examples through the real load path.</summary>
        public async Task InitializeAsync()
        {
            var dir = ExampleDesignFilesTests.ExamplesDirectory();
            RamCanvas = await LogicGateHalfAdderExampleTests.LoadCanvas(
                Path.Combine(dir, "Logic Gate RAM 2x4.lun"));
            AdderCanvas = await LogicGateHalfAdderExampleTests.LoadCanvas(
                Path.Combine(dir, "Logic Gate 4-Bit Adder.lun"));
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;
    }
}
