using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.Integration;

/// <summary>
/// Mutation gate for the component/group footprint overlap check over the shipped
/// examples: the Logic Unit 4-bit mutation from the #1304 kill-review (slice NOT0
/// shoved 120 µm up into AND0's footprint) must surface exactly the pair
/// (AND0, NOT0), and every shipped example as-is must stand clear.
/// </summary>
public class ComponentFootprintOverlapExampleTests
{
    private const string LogicUnitFileName = "Logic Gate Logic Unit 4-bit.lun";

    [Fact]
    public async Task LogicUnit4Bit_ShipsWithoutFootprintOverlaps()
    {
        var canvas = await LoadLogicUnit();

        FootprintIssuesOf(canvas).ShouldBeEmpty(
            "the eight slices must stand clear of each other as shipped");
    }

    [Fact]
    public async Task LogicUnit4Bit_Not0Shoved120UmIntoAnd0_ReportsExactlyThatPair()
    {
        var canvas = await LoadLogicUnit();
        var groups = LogicGateHalfAdderExampleTests.GroupsOf(canvas);
        var not0 = groups.Single(g => g.GroupName == "NOT0");

        // NOT0 stands below AND0; 120 µm up (negative Y, Y-down plane) shoves it
        // into AND0's footprint — the #1304 kill-review mutation that passed DRC-lite.
        not0.MoveGroup(0, -120);

        var issues = FootprintIssuesOf(canvas);
        issues.Count.ShouldBe(1, "only the (AND0, NOT0) pair may overlap after the shove");
        issues[0].Description.ShouldContain("AND0");
        issues[0].Description.ShouldContain("NOT0");
    }

    [Fact]
    public async Task EveryShippedExample_ReportsZeroFootprintOverlaps()
    {
        var findings = new List<string>();

        foreach (var examplePath in Directory.GetFiles(
                     ExampleDesignFilesTests.ExamplesDirectory(), "*.lun"))
        {
            var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(examplePath);
            foreach (var issue in FootprintIssuesOf(canvas))
            {
                findings.Add($"{Path.GetFileName(examplePath)}: {issue.Description}");
            }
        }

        findings.ShouldBeEmpty(
            "a shipped example with overlapping footprints is a real finding: "
            + string.Join("; ", findings));
    }

    /// <summary>Loads the Logic Unit 4-bit example through the real Home-screen path.</summary>
    private static async Task<DesignCanvasViewModel> LoadLogicUnit()
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), LogicUnitFileName);
        var canvas = new DesignCanvasViewModel();
        var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
        (await fileOps.OpenDesignAsCopyAsync(path)).ShouldBeTrue(
            $"'{LogicUnitFileName}' must open from the Home screen");
        await fileOps.PostLoadRouting;
        return canvas;
    }

    /// <summary>Runs the footprint check over the canvas's top-level placed items.</summary>
    private static List<DesignIssue> FootprintIssuesOf(DesignCanvasViewModel canvas)
    {
        var items = canvas.Components.Select(c => c.Component).ToList();
        return new ComponentFootprintOverlapChecker().DetectOverlaps(items);
    }
}
