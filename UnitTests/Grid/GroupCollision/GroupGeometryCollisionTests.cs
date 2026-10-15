using CAP_Core.Components.Core;
using CAP_Core.Grid;
using Shouldly;
using Xunit;

namespace UnitTests.Grid.GroupCollision;

/// <summary>
/// Group placement is judged by real geometry — leaf footprints and frozen waveguides on
/// both sides — never by a group's bounding box.
/// </summary>
public class GroupGeometryCollisionTests
{
    private readonly GroupCollisionDetector _detector = new();

    [Fact]
    public void BoundingBoxesOverlap_GeometryClear_IsAllowed()
    {
        // A: parts top-left and bottom-right, waveguide right then down (an L).
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        // B: its right part dropped into A's empty bottom-left corner, its left part outside A.
        var (groupB, _) = CollisionScene.StraightGroup("B", 500, 500);

        bool canPlace = _detector.CanPlaceGroup(groupB, -180, 230, new List<Component> { groupA, groupB });

        canPlace.ShouldBeTrue();
        CollisionScene.BoxesOverlap(groupA, groupB, -180 - 500, 230 - 500).ShouldBeTrue("scenario must overlap the boxes");
    }

    [Fact]
    public void MovedPartTouchesOtherGroupsWaveguide_IsRefused()
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        var (groupB, _) = CollisionScene.StraightGroup("B", 500, 500);

        // B's left part straddles A's vertical waveguide leg (x = 250).
        bool canPlace = _detector.CanPlaceGroup(groupB, 230, 150, new List<Component> { groupA, groupB });

        canPlace.ShouldBeFalse();
    }

    [Fact]
    public void OtherGroupsWaveguideRunningThroughMovedGroupsBox_DoesNotBlock()
    {
        // Moving A itself so B's part sits in A's empty corner: the check is symmetric.
        var (groupA, _) = CollisionScene.LGroup("A", 500, 500);
        var (groupB, _) = CollisionScene.StraightGroup("B", -180, 230);

        _detector.CanPlaceGroup(groupA, 0, 0, new List<Component> { groupA, groupB }).ShouldBeTrue();
    }

    [Theory]
    [InlineData(27.0, false)] // 2 µm centerline gap < 0.5 + 0.5 + 2 µm clearance
    [InlineData(29.0, true)]  // 4 µm centerline gap: clear
    public void MovedWaveguideNextToOtherGroupsWaveguide_RespectsSpacing(double y, bool expected)
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        var groupB = new ComponentGroup("B");
        var left = CollisionScene.Part("b1", -400, 0);
        var right = CollisionScene.Part("b2", -150, 0);
        groupB.AddChild(left);
        groupB.AddChild(right);
        // A waveguide piece running parallel to A's top leg (y = 25, x from 50 to 250).
        groupB.AddInternalPath(CollisionScene.Frozen(left, right, (100, y), (200, y)));

        bool canPlace = _detector.CanPlaceGroup(
            groupB, groupB.PhysicalX, groupB.PhysicalY, new List<Component> { groupA, groupB });

        canPlace.ShouldBe(expected);
    }

    [Fact]
    public void NestedGroupWaveguide_CollidingWithOtherGroupsPart_IsRefused()
    {
        var (inner, _) = CollisionScene.StraightGroup("inner", 0, 0);
        var outer = new ComponentGroup("outer");
        outer.AddChild(inner);
        var (obstacleGroup, _) = CollisionScene.StraightGroup("other", 1000, 1000);

        // The inner waveguide (x 50..200 at y 25) moves onto the other group's left part.
        bool canPlace = _detector.CanPlaceGroup(
            outer, outer.PhysicalX + 900, outer.PhysicalY + 990, new List<Component> { outer, obstacleGroup });

        canPlace.ShouldBeFalse();
    }

    [Fact]
    public void NestedGroups_InterleavedWithoutContact_IsAllowed()
    {
        var (innerA, _) = CollisionScene.LGroup("innerA", 0, 0);
        var outerA = new ComponentGroup("outerA");
        outerA.AddChild(innerA);
        var (innerB, _) = CollisionScene.StraightGroup("innerB", 500, 500);
        var outerB = new ComponentGroup("outerB");
        outerB.AddChild(innerB);

        bool canPlace = _detector.CanPlaceGroup(
            outerB, outerB.PhysicalX - 680, outerB.PhysicalY - 270, new List<Component> { outerA, outerB });

        canPlace.ShouldBeTrue();
    }

    [Fact]
    public void BackgroundGeometryInOtherGroup_NeverBlocks()
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        var frame = CollisionScene.Part("frame", -1000, -1000, 3000, 3000);
        frame.IsRoutingObstacle = false;
        groupA.AddChild(frame);
        var (groupB, _) = CollisionScene.StraightGroup("B", 500, 500);

        _detector.CanPlaceGroup(groupB, -180, 230, new List<Component> { groupA, groupB }).ShouldBeTrue();
    }

    [Fact]
    public void BlockedFallbackWaveguide_IsIgnored()
    {
        var (groupA, path) = CollisionScene.LGroup("A", 0, 0);
        path.Path.IsBlockedFallback = true;
        var (groupB, _) = CollisionScene.StraightGroup("B", 500, 500);

        _detector.CanPlaceGroup(groupB, 230, 150, new List<Component> { groupA, groupB }).ShouldBeTrue();
    }

    [Fact]
    public void Footprint_InGroupsEmptyCorner_IsAllowed_OnWaveguide_IsRefused()
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);

        _detector.CanPlaceFootprint(20, 230, 50, 50, new[] { groupA }).ShouldBeTrue();
        _detector.CanPlaceFootprint(230, 100, 50, 50, new[] { groupA }).ShouldBeFalse();
    }

    [Fact]
    public void PreExistingWaveguideContact_IsGrandfathered_NewContactIsNot()
    {
        var (groupA, _) = CollisionScene.LGroup("A", 0, 0);
        var (groupB, _) = CollisionScene.StraightGroup("B", 230, 150); // left part straddles A's vertical leg
        var all = new List<Component> { groupA, groupB };
        var partners = _detector.FindContacts(groupB, all);
        partners.ShouldNotBeEmpty();

        _detector.CanPlaceGroup(groupB, groupB.PhysicalX, groupB.PhysicalY + 1, all, preDragOverlapPartners: partners)
            .ShouldBeTrue("the same contact persists");
        _detector.CanPlaceGroup(groupB, 0, 0, all, preDragOverlapPartners: partners)
            .ShouldBeFalse("dropping onto A's top-left part is a new contact");
    }
}
