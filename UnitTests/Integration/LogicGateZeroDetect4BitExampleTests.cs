using UnitTests.Helpers;
using CAP_Core.Routing.CrossingInsertion;
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
/// Pinned tests for the shipped <c>examples/Logic Gate Zero Detect 4-bit.lun</c> (issue
/// #1307, rung 5: the photonic zero flag the ISA <c>JZ</c> branches on): three OR slices
/// in a tree — OR01 reads A0 and A1, OR23 reads A2 and A3, ORALL combines them — feeding
/// one NOT slice, so the flag tap Z reads NOT(A0 OR A1 OR A2 OR A3). The operand bits
/// arrive through the persisted signal names (issues #1025/#1034), three routed wires
/// carry the cascade, and the output carries the name Z (#1046). All 16 inputs must give
/// Z == (A == 0) — the branch condition of the shipped <c>count-to-5.asm</c> loop.
/// </summary>
public class LogicGateZeroDetect4BitExampleTests
    : IClassFixture<LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture>
{
    private const double OrThreshold = 0.25;
    private const double NotThreshold = 0.375;

    /// <summary>The four network inputs the operand pins merge into.</summary>
    private static readonly string[] NetworkInputs = { "A0", "A1", "A2", "A3" };

    /// <summary>The output taps: one per slice, the NOT slice's named Z (#1046).</summary>
    private static readonly string[] NetworkOutputs = { "OR01.Y", "OR23.Y", "ORALL.Y", "Z" };

    private readonly ZeroDetectFixture _fixture;

    /// <summary>Attaches the shared zero-detect fixture.</summary>
    public LogicGateZeroDetect4BitExampleTests(ZeroDetectFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsFourSlices_EachWithPersistedRoles()
    {
        _fixture.Canvas.Components.ShouldAllBe(
            c => c.Component is ComponentGroup || CrossingComponentCatalog.IsCrossing(c.Component), "the zero detect contains only top-level gate groups (and the crossings between their wires)");
        ExampleWires.LogicalWireCount(_fixture.Canvas).ShouldBe(3,
            "three wires carry the cascade: OR01.Y → ORALL.A, OR23.Y → ORALL.B, ORALL.Y → NOTZ.A");
        var groups = _fixture.Groups;
        groups.Count.ShouldBe(4, "three OR slices plus one NOT slice");
        groups.Select(g => g.GroupName).ShouldBe(
            new[] { "OR01", "OR23", "ORALL", "NOTZ" }, ignoreOrder: true);

        var expectedInputs = new Dictionary<string, Dictionary<string, string>?>
        {
            ["OR01"] = new() { ["A"] = "A0", ["B"] = "A1" },
            ["OR23"] = new() { ["A"] = "A2", ["B"] = "A3" },
            ["ORALL"] = null,
            ["NOTZ"] = null,
        };
        foreach (var group in groups)
        {
            var roles = group.TruthTablePinAssignment.ShouldNotBeNull(
                $"group '{group.GroupName}' must ship its persisted roles");
            var isNot = group.GroupName == "NOTZ";
            roles.InputPinNames.ShouldBe(isNot ? new[] { "A" } : new[] { "A", "B" });
            roles.OutputPinNames.ShouldBe(new[] { "Y" });
            roles.BiasPinNames.ShouldBe(isNot ? new[] { "BIAS" } : Array.Empty<string>());
            roles.Threshold.ShouldBe(isNot ? NotThreshold : OrThreshold);
            roles.InputSignalNames.ShouldBe(expectedInputs[group.GroupName],
                $"the wired pins of '{group.GroupName}' need no signal identity (#1025)");
            group.Description.ShouldContain("logic layer", Case.Sensitive,
                "every gate carries the education note about logic-layer composition");
            group.Description.ShouldContain(isNot ? "0.375" : "0.25");
        }

        var notZ = groups.Single(g => g.GroupName == "NOTZ");
        notZ.TruthTablePinAssignment!.OutputSignalNames.ShouldBe(
            new Dictionary<string, string> { ["Y"] = "Z" },
            "the NOT slice's output carries the flag name Z (issue #1046)");
    }

    [Fact]
    public void AssembledNetwork_ExposesFourOperandSignalsAndTheFlagTap()
    {
        _fixture.Network.InputPinNames.ShouldBe(NetworkInputs, ignoreOrder: true,
            customMessage: "the signal names merge the operand pins into exactly four " +
                "network inputs — A0–A3, the toggles the Logic panel shows");
        _fixture.Network.OutputPinNames.ShouldBe(NetworkOutputs, ignoreOrder: true,
            customMessage: "every slice output is a tap; the NOT slice's reads Z (issue #1046)");
    }

    [Fact]
    public void LogicLayer_All16Inputs_RaiseZExactlyWhenTheWordIsZero()
    {
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            var result = _fixture.Network.Evaluate(_fixture.InputBits(a));
            result["Z"].ShouldBe(a == 0, $"Z of A={a}");
            result["OR01.Y"].ShouldBe((a & 0b0011) != 0, $"OR01.Y of A={a} (A0 OR A1)");
            result["OR23.Y"].ShouldBe((a & 0b1100) != 0, $"OR23.Y of A={a} (A2 OR A3)");
            result["ORALL.Y"].ShouldBe(a != 0, $"ORALL.Y of A={a} (any bit lit)");
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
                new[] { "OR01", "OR23", "ORALL", "NOTZ" }, ignoreOrder: true);
            reloadedGroups.ShouldAllBe(g => g.TruthTablePinAssignment != null,
                "the persisted pin roles must survive the save → load round trip");
            ExampleWires.LogicalWireCount(reloadedCanvas).ShouldBe(3,
                "every cascade wire must survive the round trip");

            var reloaded = await LogicGateFourBitAdderExampleTests.AssembleNetwork(reloadedCanvas);
            reloaded.InputPinNames.ShouldBe(_fixture.Network.InputPinNames);
            reloaded.OutputPinNames.ShouldBe(_fixture.Network.OutputPinNames);
            for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
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
    public void DesignValidator_ReportsNoOverlapsOnTheLoadedExample()
    {
        var issues = new DesignValidator().Validate(
            _fixture.Canvas.Connections.Select(c => c.Connection).ToList(),
            _fixture.Groups);
        issues.ShouldBeEmpty("the four slices must stand clear of each other — no overlaps");
    }

    /// <summary>Shared fixture: loads the shipped example once through the Home example
    /// path and assembles its logic network (each extraction is a real simulation run).</summary>
    public class ZeroDetectFixture : IAsyncLifetime
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

        /// <summary>The network input bits for one 4-bit word — one bit per signal.</summary>
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
