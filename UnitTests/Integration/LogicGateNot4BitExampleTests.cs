using UnitTests.Helpers;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pinned tests for the shipped <c>examples/Logic Gate NOT 4-bit.lun</c> (issue #1254,
/// rung 5 datapath stone for the ISA <c>NOT</c>): four instances of the shipped NOT/NAND
/// gate (#946) side by side, each persisted at its NOT reading (input A only, threshold
/// 0.375). No wires join the slices — the operand bits arrive through the persisted
/// signal names (issues #1025/#1034), which merge the four unconnected A pins into the
/// four network inputs A0–A3, and the outputs carry the names Y0–Y3 (#1046). All sixteen
/// input words must give Y = ~A &amp; 0xF, matching the ISA's <c>ACC := ~ACC &amp; 0xF</c>.
/// </summary>
public class LogicGateNot4BitExampleTests : IClassFixture<LogicGateNot4BitExampleTests.Not4BitFixture>
{
    private const double NotThreshold = 0.375;

    /// <summary>The four network inputs the operand pins merge into.</summary>
    private static readonly string[] NetworkInputs = { "A0", "A1", "A2", "A3" };

    /// <summary>The four named output taps.</summary>
    private static readonly string[] NetworkOutputs = { "Y0", "Y1", "Y2", "Y3" };

    private readonly Not4BitFixture _fixture;

    /// <summary>Attaches the shared NOT-4-bit fixture.</summary>
    public LogicGateNot4BitExampleTests(Not4BitFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsFourNotSlices_EachWithPersistedNotRoles()
    {
        _fixture.Canvas.Components.ShouldAllBe(
            c => c.Component is ComponentGroup || CrossingComponentCatalog.IsCrossing(c.Component), "the 4-bit NOT contains only top-level gate groups (and the crossings between their wires)");
        ExampleWires.LogicalWireCount(_fixture.Canvas).ShouldBe(0,
            "the four slices stand side by side without wires — inputs arrive via signal names");
        var groups = _fixture.Groups;
        groups.Count.ShouldBe(4, "one NOT slice per bit of the 4-bit word");
        groups.Select(g => g.GroupName).ShouldBe(
            Enumerable.Range(0, 4).Select(i => $"NOT{i}").ToArray(), ignoreOrder: true);
        foreach (var index in Enumerable.Range(0, 4))
        {
            var group = groups.Single(g => g.GroupName == $"NOT{index}");
            var roles = group.TruthTablePinAssignment.ShouldNotBeNull(
                $"group '{group.GroupName}' must ship its persisted roles");
            roles.InputPinNames.ShouldBe(new[] { "A" });
            roles.OutputPinNames.ShouldBe(new[] { "Y" });
            roles.BiasPinNames.ShouldBe(new[] { "BIAS" });
            roles.Threshold.ShouldBe(NotThreshold);
            roles.InputSignalNames.ShouldBe(new Dictionary<string, string> { ["A"] = $"A{index}" },
                $"slice {index} reads the operand bit A{index} (issues #1025/#1034)");
            roles.OutputSignalNames.ShouldBe(new Dictionary<string, string> { ["Y"] = $"Y{index}" },
                $"slice {index} drives the result bit Y{index} (issue #1046)");
            group.Description.ShouldContain("logic layer", Case.Sensitive,
                "every gate carries the education note about logic-layer composition");
            group.Description.ShouldContain("0.375");
        }
    }

    [Fact]
    public void AssembledNetwork_ExposesFourOperandSignalsAndFourNamedOutputs()
    {
        _fixture.Network.InputPinNames.ShouldBe(NetworkInputs, ignoreOrder: true,
            customMessage: "the signal names merge the four operand pins into exactly four " +
                "network inputs — A0–A3, the toggles the Logic panel shows");
        _fixture.Network.OutputPinNames.ShouldBe(NetworkOutputs, ignoreOrder: true,
            customMessage: "the named outputs read Y0–Y3 (issue #1046)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void LogicLayer_EveryInputWord_YieldsTheBitwiseNot(int a)
    {
        var result = _fixture.Network.Evaluate(_fixture.InputBits(a));
        var expected = ~a & 0xF;
        for (var bit = 0; bit < 4; bit++)
        {
            result[$"Y{bit}"].ShouldBe(((expected >> bit) & 1) == 1,
                $"Y{bit} of NOT {a} = {expected}");
        }
    }

    /// <summary>Shared fixture: loads the shipped example once and assembles its logic
    /// network (each extraction is a real simulation run).</summary>
    public class Not4BitFixture : IAsyncLifetime
    {
        private const string ExampleFileName = "Logic Gate NOT 4-bit.lun";

        /// <summary>The canvas the shipped example loaded onto.</summary>
        public CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel Canvas { get; private set; } = null!;

        /// <summary>The loaded top-level gate groups.</summary>
        public List<ComponentGroup> Groups { get; private set; } = null!;

        /// <summary>The logic network assembled from the loaded design.</summary>
        public LogicNetworkEvaluator Network { get; private set; } = null!;

        /// <summary>Loads the shipped example and assembles its logic network.</summary>
        public async Task InitializeAsync()
        {
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
            Canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
            Groups = LogicGateHalfAdderExampleTests.GroupsOf(Canvas);
            Network = await LogicGateFourBitAdderExampleTests.AssembleNetwork(Canvas);
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>The network input bits for one operand word — one bit per signal.</summary>
        public Dictionary<string, bool> InputBits(int a) =>
            Enumerable.Range(0, 4).ToDictionary(bit => $"A{bit}", bit => ((a >> bit) & 1) == 1);
    }
}
