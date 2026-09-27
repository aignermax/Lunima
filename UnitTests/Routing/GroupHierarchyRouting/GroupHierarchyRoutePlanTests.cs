using CAP_Core.Routing.GroupHierarchyRouting;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.GroupHierarchyRouting;

/// <summary>
/// The plan must bucket connections by the lowest common ancestor group of their
/// endpoints and order the buckets deepest group first (issue #1175): gate-internal
/// wires route before gate-to-gate wires, which route before top-level wires.
/// </summary>
public class GroupHierarchyRoutePlanTests
{
    [Fact]
    public void Build_UngroupedDesign_YieldsSingleTopLevelBucketWithoutHierarchy()
    {
        var a = GroupHierarchyTestDesign.CreateComponent(0, 0, 10, 10, "a");
        var b = GroupHierarchyTestDesign.CreateComponent(30, 0, 10, 10, "b");
        var wire = GroupHierarchyTestDesign.Connect(a, b);

        var plan = GroupHierarchyRoutePlan.Build(new[] { wire });

        plan.HasHierarchy.ShouldBeFalse();
        var bucket = plan.Buckets.ShouldHaveSingleItem();
        bucket.Group.ShouldBeNull();
        bucket.Depth.ShouldBe(0);
        bucket.Connections.ShouldBe(new[] { wire });
    }

    [Fact]
    public void Build_TwoGateGroupsWithCrossWire_OrdersIntraGroupBucketsBeforeTopLevel()
    {
        var a1 = GroupHierarchyTestDesign.CreateComponent(0, 0, 10, 10, "a1");
        var a2 = GroupHierarchyTestDesign.CreateComponent(30, 0, 10, 10, "a2");
        var b1 = GroupHierarchyTestDesign.CreateComponent(100, 0, 10, 10, "b1");
        var b2 = GroupHierarchyTestDesign.CreateComponent(130, 0, 10, 10, "b2");
        var gateA = GroupHierarchyTestDesign.Group("gateA", a1, a2);
        var gateB = GroupHierarchyTestDesign.Group("gateB", b1, b2);

        var crossWire = GroupHierarchyTestDesign.Connect(a2, b1);
        var insideA = GroupHierarchyTestDesign.Connect(a1, a2);
        var insideB = GroupHierarchyTestDesign.Connect(b1, b2);

        var plan = GroupHierarchyRoutePlan.Build(new[] { crossWire, insideA, insideB });

        plan.HasHierarchy.ShouldBeTrue();
        plan.Buckets.Count.ShouldBe(3);
        plan.Buckets[0].Group.ShouldBe(gateA);
        plan.Buckets[0].Connections.ShouldBe(new[] { insideA });
        plan.Buckets[1].Group.ShouldBe(gateB);
        plan.Buckets[1].Connections.ShouldBe(new[] { insideB });
        plan.Buckets[2].Group.ShouldBeNull();
        plan.Buckets[2].Connections.ShouldBe(new[] { crossWire });
    }

    [Fact]
    public void Build_NestedGroups_RoutesDeepestScopeFirstAndUsesLowestCommonAncestor()
    {
        var inner1 = GroupHierarchyTestDesign.CreateComponent(0, 0, 10, 10, "inner1");
        var inner2 = GroupHierarchyTestDesign.CreateComponent(30, 0, 10, 10, "inner2");
        var sibling = GroupHierarchyTestDesign.CreateComponent(100, 0, 10, 10, "sibling");
        var outsider = GroupHierarchyTestDesign.CreateComponent(200, 0, 10, 10, "outsider");

        var innerGroup = GroupHierarchyTestDesign.Group("inner", inner1, inner2);
        var outerGroup = GroupHierarchyTestDesign.Group("outer", innerGroup, sibling);

        var innerWire = GroupHierarchyTestDesign.Connect(inner1, inner2);
        var outerWire = GroupHierarchyTestDesign.Connect(inner2, sibling);
        var topWire = GroupHierarchyTestDesign.Connect(sibling, outsider);

        var plan = GroupHierarchyRoutePlan.Build(new[] { topWire, outerWire, innerWire });

        plan.Buckets.Count.ShouldBe(3);
        plan.Buckets[0].Group.ShouldBe(innerGroup);
        plan.Buckets[0].Depth.ShouldBe(2);
        plan.Buckets[1].Group.ShouldBe(outerGroup);
        plan.Buckets[1].Depth.ShouldBe(1);
        plan.Buckets[2].Group.ShouldBeNull();
        plan.Buckets[2].Depth.ShouldBe(0);
    }

    [Fact]
    public void Build_WireBetweenSiblingGroups_LandsInParentGroupBucket()
    {
        var a = GroupHierarchyTestDesign.CreateComponent(0, 0, 10, 10, "a");
        var b = GroupHierarchyTestDesign.CreateComponent(60, 0, 10, 10, "b");
        var gateA = GroupHierarchyTestDesign.Group("gateA", a);
        var gateB = GroupHierarchyTestDesign.Group("gateB", b);
        var parent = GroupHierarchyTestDesign.Group("adder", gateA, gateB);

        var wire = GroupHierarchyTestDesign.Connect(a, b);

        var plan = GroupHierarchyRoutePlan.Build(new[] { wire });

        var bucket = plan.Buckets.ShouldHaveSingleItem();
        bucket.Group.ShouldBe(parent);
        bucket.Depth.ShouldBe(1);
    }
}
