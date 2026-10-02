using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Logic.Isa;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pinned tests for the shipped <c>examples/Logic Gate Zero Detect 4-bit.lun</c>
/// (issue #1307, rung 5 zero flag for the ISA <c>JZ</c>): three OR slices of the
/// shipped OR-AND gate as a two-stage tree (OR0 = A0 OR A1, OR1 = A2 OR A3,
/// OR2 = OR0 OR OR1) feeding one NOT slice of the shipped NOT-NAND gate, so
/// Z = NOT(A0 OR A1 OR A2 OR A3) — Z shines exactly when the word is 0. The operand
/// bits arrive through the persisted signal names on the first-stage slices (issues
/// #1025/#1034); the tree stages are wired and the NOT output carries the tap name Z
/// (issue #1046). All 16 words must give Z == (A == 0), the check the ISA
/// <c>JZ</c> branches on.
/// </summary>
public class LogicGateZeroDetect4BitExampleTests
    : IClassFixture<LogicGateZeroDetect4BitExampleTests.ZeroDetect4BitFixture>
{
    /// <summary>The four network inputs the first-stage operand pins merge into.</summary>
    private static readonly string[] NetworkInputs = { "A0", "A1", "A2", "A3" };

    private readonly ZeroDetect4BitFixture _fixture;

    /// <summary>Attaches the shared zero-detect fixture.</summary>
    public LogicGateZeroDetect4BitExampleTests(ZeroDetect4BitFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsTheOrTreeAndNotSlice_WithPersistedRolesAndWires()
    {
        _fixture.Canvas.Components.ShouldAllBe(
            c => c.Component is ComponentGroup, "the zero detect contains only top-level gate groups");
        _fixture.Canvas.Connections.Count.ShouldBe(3,
            "three wires join the tree: OR0→OR2, OR1→OR2, OR2→NOT");
        var groups = _fixture.Groups;
        groups.Count.ShouldBe(4, "three OR slices and one NOT slice");
        groups.Select(g => g.GroupName).ShouldBe(
            new[] { "OR0", "OR1", "OR2", "NOTZ" }, ignoreOrder: true);

        foreach (var (name, inputs) in new[] { ("OR0", new[] { "A0", "A1" }), ("OR1", new[] { "A2", "A3" }) })
        {
            var roles = groups.Single(g => g.GroupName == name).TruthTablePinAssignment
                .ShouldNotBeNull($"group '{name}' must ship its persisted roles");
            roles.InputPinNames.ShouldBe(new[] { "A", "B" });
            roles.OutputPinNames.ShouldBe(new[] { "Y" });
            roles.Threshold.ShouldBe(0.25, "the OR reading of the OR-AND gate");
            roles.InputSignalNames.ShouldBe(new Dictionary<string, string>
                { ["A"] = inputs[0], ["B"] = inputs[1] },
                $"'{name}' reads the operand bits {inputs[0]} and {inputs[1]} (issues #1025/#1034)");
        }

        var or2 = groups.Single(g => g.GroupName == "OR2").TruthTablePinAssignment
            .ShouldNotBeNull("the second-stage OR must ship its persisted roles");
        or2.InputSignalNames.ShouldBeNull("OR2 is wire-driven by the first stage — no external inputs");
        or2.Threshold.ShouldBe(0.25);

        var notz = groups.Single(g => g.GroupName == "NOTZ").TruthTablePinAssignment
            .ShouldNotBeNull("the NOT slice must ship its persisted roles");
        notz.InputPinNames.ShouldBe(new[] { "A" });
        notz.BiasPinNames.ShouldBe(new[] { "BIAS" });
        notz.Threshold.ShouldBe(0.375, "the NOT reading of the NOT-NAND gate");
        notz.InputSignalNames.ShouldBeNull("the NOT input is wired to the OR tree");
        notz.OutputSignalNames.ShouldBe(new Dictionary<string, string> { ["Y"] = "Z" },
            "the flag tap reads Z (issue #1046)");
        groups.ShouldAllBe(g => g.Description.Contains("logic layer"),
            "every gate carries the education note about logic-layer composition");
    }

    [Fact]
    public void AssembledNetwork_ExposesFourOperandSignalsAndTheFlagTap()
    {
        _fixture.Network.InputPinNames.ShouldBe(NetworkInputs, ignoreOrder: true,
            customMessage: "the signal names merge the first-stage operand pins into exactly " +
                "four network inputs — A0–A3, the toggles the Logic panel shows");
        _fixture.Network.OutputPinNames.ShouldContain("Z",
            customMessage: "the flag tap reads Z (issue #1046)");
    }

    [Fact]
    public void LogicLayer_All16Words_YieldTheZeroFlag()
    {
        for (var a = 0; a < 16; a++)
        {
            _fixture.Network.Evaluate(_fixture.InputBits(a))["Z"].ShouldBe(a == 0, $"A={a}");
        }
    }

    [Fact]
    public void PhotonicZeroFlag_AcceptsTheExampleNetwork()
    {
        PhotonicZeroFlag.Accepts(_fixture.Network).ShouldBeTrue(
            "the shipped example must expose A0–A3 and Z for the ISA zero flag");
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
                new[] { "OR0", "OR1", "OR2", "NOTZ" }, ignoreOrder: true);
            reloadedGroups.ShouldAllBe(g => g.TruthTablePinAssignment != null,
                "the persisted pin roles must survive the save → load round trip");

            var reloaded = await LogicGateFourBitAdderExampleTests.AssembleNetwork(reloadedCanvas);
            reloaded.InputPinNames.ShouldBe(_fixture.Network.InputPinNames);
            reloaded.OutputPinNames.ShouldBe(_fixture.Network.OutputPinNames);
            for (var a = 0; a < 16; a++)
            {
                reloaded.Evaluate(_fixture.InputBits(a)).ShouldBe(
                    _fixture.Network.Evaluate(_fixture.InputBits(a)),
                    $"the re-assembled network must evaluate identically for A={a}");
            }
        }
        finally
        {
            if (File.Exists(savedPath)) File.Delete(savedPath);
        }
    }

    [Fact]
    public void DesignValidator_ReportsNoIssuesOnTheLoadedExample()
    {
        var issues = new DesignValidator().Validate(
            _fixture.Canvas.Connections.Select(c => c.Connection).ToList(),
            _fixture.Groups);
        issues.ShouldBeEmpty("the four slices must stand clear of each other — no overlaps: " +
            string.Join(" | ", issues.Select(i => $"{i.Type}: {i.Description}")));
    }

    /// <summary>Shared fixture: loads the shipped example once through the Home example
    /// path and assembles its logic network (each extraction is a real simulation run).</summary>
    public class ZeroDetect4BitFixture : IAsyncLifetime
    {
        private const string ExampleFileName = "Logic Gate Zero Detect 4-bit.lun";

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

        /// <summary>The network input bits for one word — one bit per signal.</summary>
        public Dictionary<string, bool> InputBits(int a)
        {
            var bits = new Dictionary<string, bool>();
            for (var bit = 0; bit < 4; bit++)
            {
                bits[$"A{bit}"] = ((a >> bit) & 1) == 1;
            }

            return bits;
        }

        /// <summary>Saves the loaded design through the real save path and returns the file path.</summary>
        public async Task<string> SaveToTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"zero-detect-4bit-{Guid.NewGuid():N}.lun");
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
