using UnitTests.Helpers;
using CAP_Core.Routing.CrossingInsertion;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pinned tests for the shipped <c>examples/Logic Gate Logic Unit 4-bit.lun</c> (issue
/// #1286, rung 5 step to a real ALU): one chip that computes two ISA operations from the
/// same operands — four AND-from-NAND slices AND0–AND3 in the top row and four NOT-NAND
/// slices NOT0–NOT3 below them, one column per bit. No wires join the slices — the operand
/// bits arrive through the persisted signal names (issues #1025/#1034), which merge the
/// twelve unconnected operand pins into the eight network inputs A0–A3 and B0–B3 (every
/// A{bit} names the A pin of both slices of its bit), and the outputs carry the names
/// Y0–Y3 (AND results) and N0–N3 (NOT results) (#1046). All 256 operand pairs must give
/// Y = A &amp; B and N = ~A &amp; 0xF — the ISA's <c>AND</c> and <c>NOT</c> on one chip.
/// </summary>
public class LogicGateLogicUnit4BitExampleTests
    : IClassFixture<LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture>
{
    private const double AndThreshold = 0.25;
    private const double NotThreshold = 0.375;

    /// <summary>The eight network inputs the operand pins merge into.</summary>
    private static readonly string[] NetworkInputs =
        { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" };

    /// <summary>The eight named output taps: AND results Y, NOT results N.</summary>
    private static readonly string[] NetworkOutputs =
        { "Y0", "Y1", "Y2", "Y3", "N0", "N1", "N2", "N3" };

    private readonly LogicUnit4BitFixture _fixture;

    /// <summary>Attaches the shared logic-unit fixture.</summary>
    public LogicGateLogicUnit4BitExampleTests(LogicUnit4BitFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsEightSlices_EachWithPersistedRoles()
    {
        _fixture.Canvas.Components.ShouldAllBe(
            c => c.Component is ComponentGroup || CrossingComponentCatalog.IsCrossing(c.Component), "the 4-bit logic unit contains only top-level gate groups (and the crossings between their wires)");
        ExampleWires.LogicalWireCount(_fixture.Canvas).ShouldBe(0,
            "the eight slices stand side by side without wires — inputs arrive via signal names");
        var groups = _fixture.Groups;
        groups.Count.ShouldBe(8, "one AND slice and one NOT slice per bit of the 4-bit words");
        foreach (var index in Enumerable.Range(0, 4))
        {
            var and = groups.Single(g => g.GroupName == $"AND{index}");
            var andRoles = and.TruthTablePinAssignment.ShouldNotBeNull(
                $"group '{and.GroupName}' must ship its persisted roles");
            andRoles.InputPinNames.ShouldBe(new[] { "A", "B" });
            andRoles.OutputPinNames.ShouldBe(new[] { "Y" });
            andRoles.BiasPinNames.ShouldBe(new[] { "BIAS", "BIAS2" });
            andRoles.Threshold.ShouldBe(AndThreshold);
            andRoles.InputSignalNames.ShouldBe(new Dictionary<string, string>
                { ["A"] = $"A{index}", ["B"] = $"B{index}" },
                $"AND slice {index} reads the operand bits A{index} and B{index} (issues #1025/#1034)");
            andRoles.OutputSignalNames.ShouldBe(new Dictionary<string, string> { ["Y"] = $"Y{index}" },
                $"AND slice {index} drives the AND result bit Y{index} (issue #1046)");
            and.Description.ShouldContain("logic layer", Case.Sensitive,
                "every gate carries the education note about logic-layer composition");
            and.Description.ShouldContain("0.25");

            var not = groups.Single(g => g.GroupName == $"NOT{index}");
            var notRoles = not.TruthTablePinAssignment.ShouldNotBeNull(
                $"group '{not.GroupName}' must ship its persisted roles");
            notRoles.InputPinNames.ShouldBe(new[] { "A" });
            notRoles.OutputPinNames.ShouldBe(new[] { "Y" });
            notRoles.BiasPinNames.ShouldBe(new[] { "BIAS" });
            notRoles.Threshold.ShouldBe(NotThreshold);
            notRoles.InputSignalNames.ShouldBe(new Dictionary<string, string> { ["A"] = $"A{index}" },
                $"NOT slice {index} reads the same operand bit A{index} as its AND slice — the shared fan-out");
            notRoles.OutputSignalNames.ShouldBe(new Dictionary<string, string> { ["Y"] = $"N{index}" },
                $"NOT slice {index} drives the NOT result bit N{index}");
            not.Description.ShouldContain("logic layer", Case.Sensitive);
            not.Description.ShouldContain("0.375");
        }
    }

    [Fact]
    public void AssembledNetwork_ExposesEightOperandSignalsAndEightNamedOutputs()
    {
        _fixture.Network.InputPinNames.ShouldBe(NetworkInputs, ignoreOrder: true,
            customMessage: "the signal names merge the twelve operand pins into exactly eight " +
                "network inputs — A0–A3 (shared by each bit's AND and NOT slice) and B0–B3");
        _fixture.Network.OutputPinNames.ShouldBe(NetworkOutputs, ignoreOrder: true,
            customMessage: "the named outputs read Y0–Y3 (AND) and N0–N3 (NOT)");
    }

    [Fact]
    public void LogicLayer_All256OperandPairs_YieldTheBitwiseAndAndNot()
    {
        for (var a = 0; a < 16; a++)
        for (var b = 0; b < 16; b++)
        {
            var result = _fixture.Network.Evaluate(_fixture.InputBits(a, b));
            var expectedAnd = a & b;
            var expectedNot = ~a & 0xF;
            for (var bit = 0; bit < 4; bit++)
            {
                result[$"Y{bit}"].ShouldBe(((expectedAnd >> bit) & 1) == 1,
                    $"Y{bit} of {a} AND {b} = {expectedAnd}");
                result[$"N{bit}"].ShouldBe(((expectedNot >> bit) & 1) == 1,
                    $"N{bit} of NOT {a} = {expectedNot}");
            }
        }
    }

    [Fact]
    public async Task SaveLoadRoundTrip_ReassembledNetwork_YieldsTheIdenticalTruthTable()
    {
        var savedPath = await _fixture.SaveToTempFile();
        try
        {
            var reloadedCanvas = await LogicGateHalfAdderExampleTests.LoadCanvas(savedPath);
            var reloadedGroups = LogicGateHalfAdderExampleTests.GroupsOf(reloadedCanvas);
            reloadedGroups.Select(g => g.GroupName).ShouldBe(
                Enumerable.Range(0, 4).SelectMany(i => new[] { $"AND{i}", $"NOT{i}" }).ToArray(),
                ignoreOrder: true);
            reloadedGroups.ShouldAllBe(g => g.TruthTablePinAssignment != null,
                "the persisted pin roles must survive the save → load round trip");

            var reloaded = await LogicGateFourBitAdderExampleTests.AssembleNetwork(reloadedCanvas);
            reloaded.InputPinNames.ShouldBe(_fixture.Network.InputPinNames);
            reloaded.OutputPinNames.ShouldBe(_fixture.Network.OutputPinNames);
            for (var a = 0; a < 16; a++)
            for (var b = 0; b < 16; b++)
            {
                reloaded.Evaluate(_fixture.InputBits(a, b)).ShouldBe(
                    _fixture.Network.Evaluate(_fixture.InputBits(a, b)),
                    $"the re-assembled network must evaluate identically for A={a}, B={b}");
            }
        }
        finally
        {
            if (File.Exists(savedPath)) File.Delete(savedPath);
        }
    }

    [Fact]
    public void DesignValidator_ReportsNoOverlapsOnTheLoadedExample()
    {
        var issues = new DesignValidator().Validate(
            _fixture.Canvas.Connections.Select(c => c.Connection).ToList(),
            _fixture.Groups);
        issues.ShouldBeEmpty("the eight slices must stand clear of each other — no overlaps");
    }

    /// <summary>Shared fixture: loads the shipped example once through the Home example
    /// path and assembles its logic network (each extraction is a real simulation run).</summary>
    public class LogicUnit4BitFixture : IAsyncLifetime
    {
        private const string ExampleFileName = "Logic Gate Logic Unit 4-bit.lun";

        /// <summary>The canvas the shipped example loaded onto.</summary>
        public DesignCanvasViewModel Canvas { get; private set; } = null!;

        /// <summary>The loaded top-level gate groups.</summary>
        public List<ComponentGroup> Groups { get; private set; } = null!;

        /// <summary>The logic network assembled from the loaded design.</summary>
        public LogicNetworkEvaluator Network { get; private set; } = null!;

        /// <summary>Loads the shipped example and assembles its logic network.</summary>
        public async Task InitializeAsync()
        {
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
            var canvas = new DesignCanvasViewModel();
            var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
            (await fileOps.OpenDesignAsCopyAsync(path)).ShouldBeTrue(
                $"'{ExampleFileName}' must open from the Home screen");
            await fileOps.PostLoadRouting;
            Canvas = canvas;
            Groups = LogicGateHalfAdderExampleTests.GroupsOf(Canvas);
            Network = await LogicGateFourBitAdderExampleTests.AssembleNetwork(Canvas);
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>The network input bits for one operand pair — one bit per signal.</summary>
        public Dictionary<string, bool> InputBits(int a, int b)
        {
            var bits = new Dictionary<string, bool>();
            for (var bit = 0; bit < 4; bit++)
            {
                bits[$"A{bit}"] = ((a >> bit) & 1) == 1;
                bits[$"B{bit}"] = ((b >> bit) & 1) == 1;
            }
            return bits;
        }

        /// <summary>Saves the loaded design through the real save path and returns the file path.</summary>
        public async Task<string> SaveToTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"logic-unit-4bit-{Guid.NewGuid():N}.lun");
            var saveVm = LogicGateHalfAdderExampleTests.CreateFileOperations(Canvas);
            var dialog = new Mock<IFileDialogService>();
            dialog.Setup(f => f.ShowSaveFileDialogAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(path);
            saveVm.FileDialogService = dialog.Object;
            await saveVm.SaveDesignAsCommand.ExecuteAsync(null);
            File.Exists(path).ShouldBeTrue("the real save path must write the temp .lun");
            return path;
        }
    }
}
