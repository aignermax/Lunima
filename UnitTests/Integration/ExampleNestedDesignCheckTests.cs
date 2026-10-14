using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pins the Design Checks findings of every shipped example now that the checks walk into
/// groups at any nesting depth (footprints per child, frozen paths of nested groups,
/// dangling pins inside groups, real group geometry for chip bounds). The run mirrors the
/// Design Checks panel: top-level connections, top-level groups and components, no
/// external-port exemptions, plus the chip-bounds pass. The dominant finding is the
/// logic-gate template's unterminated MMI discard ports (combine/recombine/invert
/// <c>.out2</c>) and unwired bias inputs, which the top-level check already reports on the
/// ungrouped "Build your first gate" example. Counts may only change when an example, a
/// template or a check changes on purpose — examples are never edited to silence a check.
/// </summary>
public class ExampleNestedDesignCheckTests
{
    private static readonly Dictionary<string, string> PinnedFindings = new()
    {
        ["EBeam Add-Drop Ring.lun"] = "UnconnectedPin=3",
        ["EBeam Mach-Zehnder Interferometer.lun"] = "UnconnectedPin=2",
        ["Logic Gate - Build your first gate.lun"] = "UnconnectedPin=6",
        ["Logic Gate 4-Bit Adder.lun"] = "BlockedPath=2 UnconnectedPin=688",
        // Wrapped RAM 4x4: its inter-cell wires are frozen one level deeper and now meet the
        // cells' frozen wires (+99 overlaps, as on the standalone RAM 4x4); the RAM group's
        // frozen wires leave the chip by up to 38 µm (top) and 16 µm (right edge).
        ["Logic Gate ALU + RAM + ACC.lun"] = "BlockedPath=31 OutOfBounds=1 OverlappingPaths=222 UnconnectedPin=1087 WaveguideThroughComponent=1",
        ["Logic Gate ALU + RAM.lun"] = "BlockedPath=31 OutOfBounds=1 OverlappingPaths=222 UnconnectedPin=1023 WaveguideThroughComponent=1",
        ["Logic Gate ALU 1-bit.lun"] = "UnconnectedPin=18",
        ["Logic Gate AND 4-bit.lun"] = "UnconnectedPin=12",
        ["Logic Gate AND-from-NAND.lun"] = "UnconnectedPin=3",
        ["Logic Gate Across Two Chiplets.lun"] = "UnconnectedPin=5",
        ["Logic Gate Bit.lun"] = "UnconnectedPin=8",
        ["Logic Gate Counter 2-bit.lun"] = "BlockedPath=1 UnconnectedPin=10",
        ["Logic Gate Full Adder.lun"] = "UnconnectedPin=64",
        ["Logic Gate Half Adder.lun"] = "UnconnectedPin=14",
        ["Logic Gate Logic Unit 4-bit.lun"] = "UnconnectedPin=20",
        ["Logic Gate MUX.lun"] = "UnconnectedPin=8",
        ["Logic Gate NOT 4-bit.lun"] = "UnconnectedPin=8",
        ["Logic Gate NOT-NAND.lun"] = "UnconnectedPin=2",
        ["Logic Gate OR-AND.lun"] = "",
        ["Logic Gate PC 2-bit.lun"] = "BlockedPath=2 OverlappingPaths=2 UnconnectedPin=24",
        ["Logic Gate RAM 2x2.lun"] = "BlockedPath=1 UnconnectedPin=46",
        ["Logic Gate RAM 2x4.lun"] = "BlockedPath=1 OverlappingPaths=3 UnconnectedPin=120",
        ["Logic Gate RAM 4x4.lun"] = "BlockedPath=28 OverlappingPaths=100 UnconnectedPin=280",
        ["Logic Gate Register 2-bit.lun"] = "UnconnectedPin=14",
        ["Logic Gate SR-Latch.lun"] = "UnconnectedPin=4",
        ["Logic Gate Zero Detect 4-bit.lun"] = "UnconnectedPin=2",
        ["Mach-Zehnder Interferometer.lun"] = "UnconnectedPin=1",
        ["Two Chiplets - Edge-Coupler Link.lun"] = "",
    };

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleFrozenBlockedPathTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Example_ReportsPinnedDesignCheckFindings(string exampleFileName)
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await fileOps.PostLoadRouting;

        var connections = canvas.Connections.Select(vm => vm.Connection).ToList();
        var placed = canvas.Components.Select(vm => vm.Component).ToList();
        var validator = new DesignValidator();
        var issues = validator.Validate(connections, placed.OfType<ComponentGroup>(), placed, null, 1550);
        issues.AddRange(validator.ValidateComponentBounds(placed, canvas.ChipMaxX, canvas.ChipMaxY));

        var summary = string.Join(" ", issues
            .GroupBy(i => i.Type.ToString())
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}={g.Count()}"));

        PinnedFindings.ShouldContainKey(exampleFileName, "pin the Design Checks findings of every new example");
        summary.ShouldBe(PinnedFindings[exampleFileName],
            $"'{exampleFileName}': Design Checks findings changed — update the pin only on purpose. " +
            string.Join(" | ", issues.Where(i => i.Type != DesignIssueType.UnconnectedPin).Take(10).Select(i => i.Description)));
    }
}
