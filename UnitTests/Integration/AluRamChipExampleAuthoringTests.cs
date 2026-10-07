using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Logic.Isa;
using Moq;
using Shouldly;
using UnitTests.Integration.RamScale;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Integration;

/// <summary>
/// Authoring utility for the shipped rung-5 example
/// <c>examples/Logic Gate ALU + RAM.lun</c> (issue #1463): the first chip that holds the
/// shipped 4-bit adder (<c>Logic Gate 4-Bit Adder.lun</c>) and the shipped ISA-sized data
/// memory (<c>Logic Gate RAM 4x4.lun</c>) side by side on one canvas, so the photonic ALU
/// (<see cref="PhotonicAdderAlu"/>) and the photonic data RAM
/// (<see cref="PhotonicDataMemory"/>) run over the one assembled network. Both blocks load
/// through the real load path with their cached routes and are never re-routed: the RAM's
/// inter-cell wires (and the crossings they run through) are frozen into a wrapping <c>RAM</c> group exactly the way
/// <see cref="CreateGroupCommand"/> freezes internal connections (loaded wires reference
/// the nested leaf pins, so the command's top-level classification cannot be reused), the
/// group is translated to the right of the adder with <see cref="ComponentGroup.MoveGroup"/>
/// (which translates frozen paths without re-routing), and the merged design is written
/// through the real save command. Before grouping, every persisted signal name of the RAM
/// gates is moved under the <c>RAM.</c> prefix
/// (<see cref="IsaDataMemorySignalMap.WithPrefix"/>), so the RAM's <c>A0</c>/<c>A1</c>/
/// <c>LOAD</c>/<c>D0</c>–<c>D3</c>/<c>Q0</c>–<c>Q3</c> cannot collide with the adder's
/// plain operand names on the shared network. A verification reload proves the saved file
/// needs no routing, assembles through the production assembler and is accepted by both
/// photonic units.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 python3 tools/smart_test.py AluRamChipExampleAuthoring</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class AluRamChipExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Logic Gate ALU + RAM.lun";

    /// <summary>Group name of the wrapped RAM block on the combined canvas.</summary>
    public const string RamGroupName = "RAM";

    /// <summary>Prefix every RAM signal name carries on the combined network.</summary>
    public const string RamSignalPrefix = "RAM.";

    private const string AdderFileName = "Logic Gate 4-Bit Adder.lun";
    private const string RamFileName = "Logic Gate RAM 4x4.lun";
    private const int RamTopLevelGroupCount = 55;
    private const int RamTopLevelCrossingCount = 11;
    private const int RamTopLevelWireCount = 106;
    private const double BlockGapMicrometers = 500.0;

    private readonly ITestOutputHelper _output;

    /// <summary>Attaches the test output sink.</summary>
    public AluRamChipExampleAuthoringTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Author_LogicGateAluPlusRam_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var (canvas, fileOps) = await LoadExampleOntoCanvas(AdderFileName);
        double adderChipWidth = canvas.ChipMaxX;
        double adderChipHeight = canvas.ChipMaxY;

        var ramGroup = await LoadPrefixedRamGroup();

        // Place the RAM block to the right of the adder with a clear gap; MoveGroup
        // translates the frozen inter-cell routes along, so nothing re-routes.
        var ramChildren = ramGroup.GetAllComponentsRecursive();
        double ramMinX = ramChildren.Min(c => c.PhysicalX);
        double ramMinY = ramChildren.Min(c => c.PhysicalY);
        double ramMaxX = ramChildren.Max(c => c.PhysicalX + c.WidthMicrometers);
        double ramMaxY = ramChildren.Max(c => c.PhysicalY + c.HeightMicrometers);
        ramGroup.MoveGroup(adderChipWidth + BlockGapMicrometers - ramMinX, -ramMinY);

        canvas.AddComponent(ramGroup, null, null);
        var ramVm = canvas.Components.Single(vm => vm.Component == ramGroup);
        ramVm.X = ramGroup.PhysicalX;
        ramVm.Y = ramGroup.PhysicalY;

        double chipWidth = ramMaxX - ramMinX + adderChipWidth + BlockGapMicrometers;
        double chipHeight = Math.Max(adderChipHeight, ramMaxY - ramMinY);
        Ram4x4FeasibilityTests.ApplyChipSize(canvas, chipWidth, chipHeight);

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        await SaveThroughRealPath(canvas, examplePath);
        File.Exists(examplePath).ShouldBeTrue($"the example must be written to {examplePath}");

        await VerifySavedExample(examplePath, chipWidth, chipHeight);
    }

    /// <summary>
    /// Loads the shipped RAM 4x4 through the real load path, prefixes every persisted
    /// signal name of its gates with <see cref="RamSignalPrefix"/> and wraps the whole
    /// block — the 55 top-level groups, the crossings between them and their inter-cell wires — into one
    /// <see cref="ComponentGroup"/> whose internal paths freeze the loaded routes.
    /// </summary>
    private async Task<ComponentGroup> LoadPrefixedRamGroup()
    {
        var (scratch, fileOps) = await LoadExampleOntoCanvas(RamFileName);
        var topLevel = scratch.Components.Select(vm => vm.Component).ToList();
        var topLevelGroups = topLevel.OfType<ComponentGroup>().ToList();
        topLevelGroups.Count.ShouldBe(RamTopLevelGroupCount,
            "the RAM 4x4 loads its address stage, copy trees, read-MUX combines and four cell instances");
        (topLevel.Count - topLevelGroups.Count).ShouldBe(RamTopLevelCrossingCount,
            "the only loose components are the crossings the inter-cell wires run through");
        scratch.Connections.Count.ShouldBe(RamTopLevelWireCount, "only the inter-cell wires load as connections");
        PrefixSignalNames(topLevelGroups);

        var group = new ComponentGroup(RamGroupName)
        {
            PhysicalX = topLevel.Min(c => c.PhysicalX),
            PhysicalY = topLevel.Min(c => c.PhysicalY),
            Description = "The shipped Logic Gate RAM 4x4 block, signal names under the 'RAM.' prefix, " +
                "placed next to the 4-bit adder so ALU and data memory share one logic network (#1463).",
        };
        group.AddChildren(topLevel);

        // Mirror CreateGroupCommand's freeze step; the loaded wires reference the nested
        // leaf pins, which the command's ParentComponent classification cannot see.
        var frozenPaths = scratch.Connections.Select(vm => vm.Connection).Select(conn =>
        {
            var frozen = new FrozenWaveguidePath
            {
                Path = conn.RoutedPath?.DeepCopy() ?? new CAP_Core.Routing.RoutedPath(),
                StartPin = conn.StartPin,
                EndPin = conn.EndPin,
            };
            frozen.CaptureSettingsFrom(conn);
            return frozen;
        }).ToList();
        group.AddInternalPaths(frozenPaths);
        return group;
    }

    /// <summary>Moves every persisted signal name of the block's gates under the RAM prefix.</summary>
    private static void PrefixSignalNames(List<ComponentGroup> topLevelGroups)
    {
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
                .ToDictionary(pair => pair.Key, pair => RamSignalPrefix + pair.Value);
            assignment.OutputSignalNames = assignment.OutputSignalNames?
                .ToDictionary(pair => pair.Key, pair => RamSignalPrefix + pair.Value);
        }
    }

    /// <summary>Loads one shipped example through the real load path (cached routes: no routing).</summary>
    private static async Task<(DesignCanvasViewModel Canvas, FileOperationsViewModel FileOps)> LoadExampleOntoCanvas(
        string fileName)
    {
        var canvas = new DesignCanvasViewModel();
        var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
        fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), fileName);
        (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
            $"'{fileName}' must load through the real load path");
        await fileOps.PostLoadRouting;
        canvas.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0,
            $"'{fileName}' must load fully routed from its cache — the merge must never re-route");
        return (canvas, fileOps);
    }

    /// <summary>Writes the merged design through the real save command (byte-for-byte product output).</summary>
    private static async Task SaveThroughRealPath(DesignCanvasViewModel canvas, string examplePath)
    {
        var saveOps = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(examplePath);
        saveOps.FileDialogService = dialog.Object;
        await saveOps.SaveDesignAsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Reloads the saved file: it must need no routing, assemble through the production
    /// assembler and be accepted by both photonic units; reports the blocked-wire census
    /// for the ExampleLoadRoutingTests/ExampleFrozenBlockedPathTests pins.
    /// </summary>
    private async Task VerifySavedExample(string examplePath, double chipWidth, double chipHeight)
    {
        var (canvas, _) = await LoadExampleOntoCanvas(ExampleFileName);
        canvas.ChipMaxX.ShouldBe(chipWidth, "the saved chip width must cover adder + gap + RAM");
        canvas.ChipMaxY.ShouldBe(chipHeight, "the saved chip height must cover both blocks");

        var network = await LogicGateFourBitAdderExampleTests.AssembleNetwork(canvas);
        PhotonicAdderAlu.Accepts(network).ShouldBeTrue(
            "the combined network must expose the adder's plain operand names");
        PhotonicDataMemory.Accepts(network, IsaDataMemorySignalMap.WithPrefix(RamSignalPrefix)).ShouldBeTrue(
            "the combined network must expose the RAM signals under the 'RAM.' prefix");
        network.RegisterState.Count.ShouldBe(IsaMachine.RamWords * IsaMachine.DataBits,
            "the sixteen RAM register bits stay nested inside the wrapped block");

        var topLevelConnections = canvas.Connections.Select(vm => vm.Connection).ToList();
        int blockedTopLevel = topLevelConnections.Count(c => c.RoutedPath?.IsBlockedFallback == true);
        var issues = new DesignValidator().Validate(
            topLevelConnections,
            canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList());
        int blockedTotal = issues.Count(i => i.Type == DesignIssueType.BlockedPath);
        Report($"[author] {ExampleFileName}: blockedTopLevel={blockedTopLevel} blockedTotal={blockedTotal} " +
            "— pin these in ExampleLoadRoutingTests.KnownBlockedWires / ExampleFrozenBlockedPathTests.KnownBlockedPathCounts");
        blockedTopLevel.ShouldBe(69,
            "the top level carries the 4-bit adder's pinned blocked wires alone; " +
            "the RAM's wires are frozen inside the RAM group");
        blockedTotal.ShouldBe(139,
            "69 adder top-level + the RAM 4x4's 70 (38 inter-cell + 4 × 8 intra-cell) frozen inside the RAM group");
    }

    private void Report(string line)
    {
        _output.WriteLine(line);
        Console.WriteLine(line);
    }
}
