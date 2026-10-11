using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration.RamScale;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Integration;

/// <summary>
/// Authoring utility for the shipped rung-5 example
/// <c>examples/Logic Gate ALU + RAM + ACC.lun</c> (issue #1470): the shipped
/// <c>Logic Gate ALU + RAM.lun</c> chip (#1463) plus a 4-bit load-enabled register
/// built from four instances of the shipped 1-bit register cell
/// (<c>Logic Gate Bit.lun</c>), so <see cref="PhotonicAdderAlu"/>,
/// <see cref="PhotonicDataMemory"/> AND <see cref="PhotonicAccumulator"/> run over
/// the one assembled network. The base file loads with its cached routes and is
/// never re-routed or re-saved (it stays byte-identical); each bit instance has its
/// persisted signal names moved to the <see cref="IsaAccumulatorSignalMap"/> defaults
/// (<c>ACC.D0</c>–<c>ACC.D3</c>, <c>ACC.LOAD</c> → <c>ACC.Q0</c>–<c>ACC.Q3</c> — the
/// shared <c>ACC.LOAD</c> name merges the four load pins into one network input) and
/// is wrapped with its frozen inter-gate wires in one <c>ACC</c>
/// <see cref="ComponentGroup"/>, the freeze <see cref="AluRamChipExampleAuthoringTests"/>
/// applies to the RAM block. The register stacks its bits below the existing blocks;
/// a verification reload proves the saved file needs no routing, assembles through
/// the production assembler and is accepted by all three photonic units.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 python3 tools/smart_test.py AluRamAccChipExampleAuthoring</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class AluRamAccChipExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Logic Gate ALU + RAM + ACC.lun";

    /// <summary>Group name of the wrapped accumulator register on the combined canvas.</summary>
    public const string AccGroupName = "ACC";

    private const string BitFileName = "Logic Gate Bit.lun";
    private const int BitTopLevelGroupCount = 4;
    private const int BitTopLevelWireCount = 4;
    private const double BlockGapMicrometers = 500.0;
    private const double BitGapMicrometers = 200.0;

    private static readonly IsaAccumulatorSignalMap AccMap = IsaAccumulatorSignalMap.Default;

    private readonly ITestOutputHelper _output;

    /// <summary>Attaches the test output sink.</summary>
    public AluRamAccChipExampleAuthoringTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Author_LogicGateAluPlusRamPlusAcc_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var (canvas, _) = await LoadExampleOntoCanvas(AluRamChipExampleAuthoringTests.ExampleFileName);
        double baseChipWidth = canvas.ChipMaxX;
        double baseChipHeight = canvas.ChipMaxY;

        var accGroup = await BuildAccumulatorRegisterGroup();
        AluRamChipExampleAuthoringTests.RenameCollidingIdentifiers(accGroup, canvas.Components.Select(vm => vm.Component));

        // Place the register below the existing blocks; MoveGroup translates the
        // frozen intra-bit routes along, so nothing re-routes.
        var accChildren = accGroup.GetAllComponentsRecursive();
        double accMinX = accChildren.Min(c => c.PhysicalX);
        double accMinY = accChildren.Min(c => c.PhysicalY);
        double accMaxX = accChildren.Max(c => c.PhysicalX + c.WidthMicrometers);
        double accMaxY = accChildren.Max(c => c.PhysicalY + c.HeightMicrometers);
        accGroup.MoveGroup(-accMinX, baseChipHeight + BlockGapMicrometers - accMinY);

        canvas.AddComponent(accGroup, null, null);
        var accVm = canvas.Components.Single(vm => vm.Component == accGroup);
        accVm.X = accGroup.PhysicalX;
        accVm.Y = accGroup.PhysicalY;

        double chipWidth = Math.Max(baseChipWidth, accMaxX - accMinX);
        double chipHeight = baseChipHeight + BlockGapMicrometers + (accMaxY - accMinY);
        Ram4x4FeasibilityTests.ApplyChipSize(canvas, chipWidth, chipHeight);

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        await SaveThroughRealPath(canvas, examplePath);
        File.Exists(examplePath).ShouldBeTrue($"the example must be written to {examplePath}");

        await VerifySavedExample(chipWidth, chipHeight);
    }

    /// <summary>
    /// Loads the shipped 1-bit register cell four times through the real load path,
    /// renames each instance's persisted signals to the accumulator defaults for its
    /// bit (<c>Load</c> → <c>ACC.LOAD</c>, <c>Din</c> → <c>ACC.D{i}</c>,
    /// <c>Q</c> → <c>ACC.Q{i}</c>), stacks the bits vertically and wraps the sixteen
    /// gate groups plus their sixteen inter-gate wires — frozen like
    /// <see cref="AluRamChipExampleAuthoringTests"/> freezes the RAM's — into one
    /// <see cref="ComponentGroup"/>.
    /// </summary>
    private async Task<ComponentGroup> BuildAccumulatorRegisterGroup()
    {
        var bitGroups = new List<ComponentGroup>();
        double stackY = 0;
        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            var (scratch, _) = await LoadExampleOntoCanvas(BitFileName);
            scratch.Components.Count.ShouldBe(BitTopLevelGroupCount,
                "the 1-bit register cell loads its four gate groups");
            scratch.Connections.Count.ShouldBe(BitTopLevelWireCount,
                "the 1-bit register cell loads its four inter-gate wires");

            var topLevelGroups = scratch.Components.Select(vm => vm.Component).OfType<ComponentGroup>().ToList();
            RenameBitSignals(topLevelGroups, bit);

            var bitGroup = new ComponentGroup($"{AccGroupName} bit {bit}");
            bitGroup.AddChildren(topLevelGroups);
            // Freeze before the move: MoveGroup translates the group's frozen paths
            // along with its gates, so the stacked bits keep their own routes.
            bitGroup.AddInternalPaths(scratch.Connections.Select(vm => vm.Connection).Select(Freeze).ToList());
            var children = bitGroup.GetAllComponentsRecursive();
            bitGroup.MoveGroup(
                -children.Min(c => c.PhysicalX),
                stackY - children.Min(c => c.PhysicalY));
            stackY += children.Max(c => c.PhysicalY + c.HeightMicrometers)
                - children.Min(c => c.PhysicalY) + BitGapMicrometers;
            bitGroups.Add(bitGroup);
        }

        var group = new ComponentGroup(AccGroupName)
        {
            PhysicalX = bitGroups.Min(g => g.PhysicalX),
            PhysicalY = bitGroups.Min(g => g.PhysicalY),
            Description = "Four Logic Gate Bit cells as the 4-bit accumulator register, signal names at the " +
                "IsaAccumulatorSignalMap defaults (ACC.D0–ACC.D3, ACC.LOAD → ACC.Q0–ACC.Q3), placed below the " +
                "adder and the RAM so three machine units share one logic network (#1470).",
        };
        group.AddChildren(bitGroups);
        return group;
    }

    /// <summary>Moves the bit cell's persisted signal names to the accumulator names for its bit.</summary>
    private static void RenameBitSignals(List<ComponentGroup> topLevelGroups, int bit)
    {
        var rename = new Dictionary<string, string>
        {
            ["Load"] = AccMap.Load,
            ["Din"] = AccMap.DataIn[bit],
            ["Q"] = AccMap.DataOut[bit],
        };
        var gates = topLevelGroups
            .SelectMany(g => g.GetAllComponentsRecursive())
            .OfType<ComponentGroup>()
            .Concat(topLevelGroups)
            .Distinct()
            .Where(g => g.TruthTablePinAssignment != null);
        foreach (var gate in gates)
        {
            var assignment = gate.TruthTablePinAssignment!;
            assignment.InputSignalNames = assignment.InputSignalNames?
                .ToDictionary(pair => pair.Key, pair => rename[pair.Value]);
            assignment.OutputSignalNames = assignment.OutputSignalNames?
                .ToDictionary(pair => pair.Key, pair => rename[pair.Value]);
        }
    }

    /// <summary>Freezes one loaded connection's route the way CreateGroupCommand freezes internal wires.</summary>
    private static FrozenWaveguidePath Freeze(CAP_Core.Components.Connections.WaveguideConnection conn)
    {
        var frozen = new FrozenWaveguidePath
        {
            Path = conn.RoutedPath?.DeepCopy() ?? new CAP_Core.Routing.RoutedPath(),
            StartPin = conn.StartPin,
            EndPin = conn.EndPin,
        };
        frozen.CaptureSettingsFrom(conn);
        return frozen;
    }

    /// <summary>Loads one shipped example through the real load path (cached routes: no routing).</summary>
    private static Task<(DesignCanvasViewModel Canvas, FileOperationsViewModel FileOps)> LoadExampleOntoCanvas(
        string fileName) => AluRamChipExampleAuthoringTests.LoadExampleOntoCanvas(fileName);

    /// <summary>Writes the merged design through the real save command (byte-for-byte product output).</summary>
    private static Task SaveThroughRealPath(DesignCanvasViewModel canvas, string examplePath) =>
        AluRamChipExampleAuthoringTests.SaveThroughRealPath(canvas, examplePath);

    /// <summary>
    /// Reloads the saved file: it must need no routing, assemble through the production
    /// assembler, be accepted by all three photonic units and run a write/read smoke
    /// through the accumulator; reports the blocked-wire census for the
    /// ExampleLoadRoutingTests/ExampleFrozenBlockedPathTests pins.
    /// </summary>
    private async Task VerifySavedExample(double chipWidth, double chipHeight)
    {
        var (canvas, _) = await LoadExampleOntoCanvas(ExampleFileName);
        canvas.ChipMaxX.ShouldBe(chipWidth, "the saved chip width must cover the widest block row");
        canvas.ChipMaxY.ShouldBe(chipHeight, "the saved chip height must cover the blocks plus the register");

        var network = await LogicGateFourBitAdderExampleTests.AssembleNetwork(canvas);
        PhotonicAdderAlu.Accepts(network).ShouldBeTrue(
            "the combined network must still expose the adder's plain operand names");
        PhotonicDataMemory.Accepts(network,
                IsaDataMemorySignalMap.WithPrefix(AluRamChipExampleAuthoringTests.RamSignalPrefix)).ShouldBeTrue(
            "the combined network must still expose the RAM signals under the 'RAM.' prefix");
        PhotonicAccumulator.Accepts(network).ShouldBeTrue(
            "the combined network must expose the accumulator signals ACC.D0–ACC.D3, ACC.LOAD → ACC.Q0–ACC.Q3");
        network.RegisterState.Count.ShouldBe(IsaMachine.RamWords * IsaMachine.DataBits + IsaMachine.DataBits,
            "the sixteen RAM register bits plus the four accumulator register bits");

        var accumulator = new PhotonicAccumulator(network);
        accumulator.Write(5);
        accumulator.Read().ShouldBe(5, "a value written on light must read back from the shipped register");

        var topLevelConnections = canvas.Connections.Select(vm => vm.Connection).ToList();
        int blockedTopLevel = topLevelConnections.Count(c => c.RoutedPath?.IsBlockedFallback == true);
        var issues = new DesignValidator().Validate(
            topLevelConnections,
            canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList());
        int blockedTotal = issues.Count(i => i.Type == DesignIssueType.BlockedPath);
        Report($"[author] {ExampleFileName}: blockedTopLevel={blockedTopLevel} blockedTotal={blockedTotal} " +
            "— pin these in ExampleLoadRoutingTests.KnownBlockedWires / ExampleFrozenBlockedPathTests.KnownBlockedPathCounts");
        blockedTopLevel.ShouldBe(5,
            "the top level still carries the 4-bit adder's pinned blocked wires alone; " +
            "the RAM's and the register's wires are frozen inside their groups");
        blockedTotal.ShouldBe(34,
            "34 as on ALU + RAM — the Logic Gate Bit cells ship zero blocked wires to freeze");
    }

    private void Report(string line)
    {
        _output.WriteLine(line);
        Console.WriteLine(line);
    }
}
