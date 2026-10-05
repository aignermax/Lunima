using System.Text.Json;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.DTOs;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Issue #1410: DRC-lite must report frozen group paths whose routed geometry is a
/// blocked fallback (<see cref="RoutedPath.IsBlockedFallback"/>) — top-level
/// connections were already covered, wires frozen inside groups were invisible.
/// </summary>
public class FrozenBlockedPathCheckerTests
{
    [Fact]
    public void Check_OneBlockedOneCleanFrozenPath_ReportsExactlyOneBlockedPathIssue()
    {
        var group = new ComponentGroup("CELL0");
        group.AddInternalPath(FrozenPath(blocked: true));
        group.AddInternalPath(FrozenPath(blocked: false));

        var issues = new DesignValidator().Validate(
            Array.Empty<WaveguideConnection>(), new[] { group });

        var blocked = issues.Where(i => i.Type == DesignIssueType.BlockedPath).ToList();
        blocked.Count.ShouldBe(1);
        blocked[0].Connection.ShouldBeNull();
        blocked[0].LocalizationKey.ShouldBe("DesignChecks.BlockedPath.InGroup");
        blocked[0].Description.ShouldContain(group.Identifier);
    }

    [Fact]
    public void Check_NestedGroup_ReportsBlockedPathWithFullGroupPath()
    {
        var inner = new ComponentGroup("REG00") { Identifier = "inner_group" };
        inner.AddInternalPath(FrozenPath(blocked: true));
        var outer = new ComponentGroup("CELL0") { Identifier = "outer_group" };
        outer.AddChild(inner);

        var issues = new DesignValidator().Validate(
            Array.Empty<WaveguideConnection>(), new[] { outer });

        var blocked = issues.Single(i => i.Type == DesignIssueType.BlockedPath);
        blocked.Description.ShouldContain("outer_group/inner_group");
        blocked.LocalizationArgs.ShouldBe(new object[] { "outer_group/inner_group", "(unpinned route end)", "(unpinned route end)" });
    }

    [Fact]
    public void Check_NoBlockedFrozenPaths_ReportsNothing()
    {
        var group = new ComponentGroup("Clean");
        group.AddInternalPath(FrozenPath(blocked: false));

        var issues = new FrozenBlockedPathChecker().Check(new[] { group });

        issues.ShouldBeEmpty();
    }

    [Fact]
    public void SerializerRoundTrip_BlockedFlagSurvives()
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp1.Identifier = "comp1";
        var comp2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp2.Identifier = "comp2";
        var group = new ComponentGroup("RoundTrip");
        group.AddChild(comp1);
        group.AddChild(comp2);
        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = BlockedPath(),
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0],
        });

        var dto = ComponentGroupSerializer.ToDto(group);
        var json = JsonSerializer.Serialize(dto);
        var restoredDto = JsonSerializer.Deserialize<ComponentGroupDto>(json)!;
        var lookup = new Dictionary<string, Component> { ["comp1"] = comp1, ["comp2"] = comp2 };
        var restored = ComponentGroupSerializer.FromDto(restoredDto, lookup);

        restored.InternalPaths.Count.ShouldBe(1);
        restored.InternalPaths[0].Path.IsBlockedFallback.ShouldBeTrue(
            "a blocked frozen path must stay blocked after .lun save/load, or DRC-lite would go blind again on reload");
    }

    /// <summary>Builds a pin-less frozen path with one straight segment.</summary>
    private static FrozenWaveguidePath FrozenPath(bool blocked) =>
        new() { Path = blocked ? BlockedPath() : CleanPath() };

    private static RoutedPath CleanPath()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        return path;
    }

    private static RoutedPath BlockedPath()
    {
        var path = CleanPath();
        path.IsBlockedFallback = true;
        return path;
    }
}
