using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Grid;
using Shouldly;
using Xunit;

namespace UnitTests.Grid.GroupCollision;

/// <summary>
/// The geometry-based placement check lets groups interleave; the DRC must still agree:
/// silent for interleaved-but-clear groups, and flagging genuine overlaps (forced in via
/// undo/import/command execution, which skip the placement check).
/// </summary>
public class GroupOverlapDesignValidatorTests
{
    private readonly DesignValidator _validator = new();

    [Fact]
    public void InterleavedGroupsAllowedByPlacement_RaiseNoOverlapIssues()
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        var (groupB, _) = CollisionScene.StraightGroup("B", -180, 230);
        new GroupCollisionDetector().CanPlaceGroup(
            groupB, groupB.PhysicalX, groupB.PhysicalY, new List<Component> { groupA, groupB }).ShouldBeTrue();

        var issues = Validate(groupA, groupB);

        issues.ShouldNotContain(i => i.Type == DesignIssueType.ComponentFootprintOverlap);
        issues.ShouldNotContain(i => i.Type == DesignIssueType.WaveguideThroughComponent);
        issues.ShouldNotContain(i => i.Type == DesignIssueType.OverlappingPaths);
    }

    [Fact]
    public void GroupPartsStackedOnEachOther_AreFlagged()
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        var (groupB, _) = CollisionScene.StraightGroup("B", 10, 10); // B's left part on A's top-left part

        Validate(groupA, groupB).ShouldContain(i => i.Type == DesignIssueType.ComponentFootprintOverlap);
    }

    [Fact]
    public void GroupWaveguideThroughOtherGroupsPart_IsFlagged()
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        var (groupB, _) = CollisionScene.StraightGroup("B", 230, 150); // left part straddles A's vertical leg

        Validate(groupA, groupB).ShouldContain(i => i.Type == DesignIssueType.WaveguideThroughComponent);
    }

    private List<DesignIssue> Validate(params ComponentGroup[] groups) =>
        _validator.Validate(
            new List<WaveguideConnection>(), groups, new List<Component>(),
            groups.SelectMany(g => g.GetAllComponentsRecursive()).SelectMany(c => c.PhysicalPins).ToList());
}
