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
/// Pinned tests for the shipped <c>examples/Logic Gate AND 4-bit.lun</c> (issue #1265,
/// rung 5 datapath stone for the ISA <c>AND</c>): four instances of the shipped
/// AND-from-NAND gate (#971) side by side, each persisted at its AND reading (inputs A
/// and B, biases BIAS and BIAS2, threshold 0.25). No wires join the slices — the operand
/// bits arrive through the persisted signal names (issues #1025/#1034), which merge the
/// eight unconnected operand pins into the eight network inputs A0–A3 and B0–B3, and the
/// outputs carry the names Y0–Y3 (#1046). All 256 operand pairs must give
/// Y = A &amp; B, matching the ISA's <c>ACC := ACC &amp; RAM[addr]</c>.
/// </summary>
public class LogicGateAnd4BitExampleTests : IClassFixture<LogicGateAnd4BitExampleTests.And4BitFixture>
{
    private const double AndThreshold = 0.25;

    /// <summary>The eight network inputs the operand pins merge into.</summary>
    private static readonly string[] NetworkInputs =
        { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" };

    /// <summary>The four named output taps.</summary>
    private static readonly string[] NetworkOutputs = { "Y0", "Y1", "Y2", "Y3" };

    private readonly And4BitFixture _fixture;

    /// <summary>Attaches the shared AND-4-bit fixture.</summary>
    public LogicGateAnd4BitExampleTests(And4BitFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsFourAndSlices_EachWithPersistedAndRoles()
    {
        _fixture.Canvas.Components.ShouldAllBe(
            c => c.Component is ComponentGroup, "the 4-bit AND contains only top-level gate groups");
        _fixture.Canvas.Connections.Count.ShouldBe(0,
            "the four slices stand side by side without wires — inputs arrive via signal names");
        var groups = _fixture.Groups;
        groups.Count.ShouldBe(4, "one AND slice per bit of the two 4-bit words");
        groups.Select(g => g.GroupName).ShouldBe(
            Enumerable.Range(0, 4).Select(i => $"AND{i}").ToArray(), ignoreOrder: true);
        foreach (var index in Enumerable.Range(0, 4))
        {
            var group = groups.Single(g => g.GroupName == $"AND{index}");
            var roles = group.TruthTablePinAssignment.ShouldNotBeNull(
                $"group '{group.GroupName}' must ship its persisted roles");
            roles.InputPinNames.ShouldBe(new[] { "A", "B" });
            roles.OutputPinNames.ShouldBe(new[] { "Y" });
            roles.BiasPinNames.ShouldBe(new[] { "BIAS", "BIAS2" });
            roles.Threshold.ShouldBe(AndThreshold);
            roles.InputSignalNames.ShouldBe(new Dictionary<string, string>
                { ["A"] = $"A{index}", ["B"] = $"B{index}" },
                $"slice {index} reads the operand bits A{index} and B{index} (issues #1025/#1034)");
            roles.OutputSignalNames.ShouldBe(new Dictionary<string, string> { ["Y"] = $"Y{index}" },
                $"slice {index} drives the result bit Y{index} (issue #1046)");
            group.Description.ShouldContain("logic layer", Case.Sensitive,
                "every gate carries the education note about logic-layer composition");
            group.Description.ShouldContain("0.25");
        }
    }

    [Fact]
    public void AssembledNetwork_ExposesEightOperandSignalsAndFourNamedOutputs()
    {
        _fixture.Network.InputPinNames.ShouldBe(NetworkInputs, ignoreOrder: true,
            customMessage: "the signal names merge the eight operand pins into exactly eight " +
                "network inputs — A0–A3 and B0–B3, the toggles the Logic panel shows");
        _fixture.Network.OutputPinNames.ShouldBe(NetworkOutputs, ignoreOrder: true,
            customMessage: "the named outputs read Y0–Y3 (issue #1046)");
    }

    [Fact]
    public void LogicLayer_All256OperandPairs_YieldTheBitwiseAnd()
    {
        for (var a = 0; a < 16; a++)
        for (var b = 0; b < 16; b++)
        {
            var result = _fixture.Network.Evaluate(_fixture.InputBits(a, b));
            var expected = a & b;
            for (var bit = 0; bit < 4; bit++)
            {
                result[$"Y{bit}"].ShouldBe(((expected >> bit) & 1) == 1,
                    $"Y{bit} of {a} AND {b} = {expected}");
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
                Enumerable.Range(0, 4).Select(i => $"AND{i}").ToArray(), ignoreOrder: true);
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
        issues.ShouldBeEmpty("the four slices must stand clear of each other — no overlaps");
    }

    /// <summary>Shared fixture: loads the shipped example once through the Home example
    /// path and assembles its logic network (each extraction is a real simulation run).</summary>
    public class And4BitFixture : IAsyncLifetime
    {
        private const string ExampleFileName = "Logic Gate AND 4-bit.lun";

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
            var path = Path.Combine(Path.GetTempPath(), $"and-4bit-{Guid.NewGuid():N}.lun");
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
