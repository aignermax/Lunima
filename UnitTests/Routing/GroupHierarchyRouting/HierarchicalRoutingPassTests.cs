using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.GroupHierarchyRouting;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.GroupHierarchyRouting;

/// <summary>
/// The hierarchical routing pass must route intra-group wires first, keep them as
/// obstacles for the group-to-group wires, and leave every wire with a valid,
/// non-fallback route on a layout with enough free space (issue #1175).
/// </summary>
public class HierarchicalRoutingPassTests
{
    private static (WaveguideConnectionManager Manager, WaveguideRouter Router) CreateManagerWithGrid(
        IEnumerable<Component> topLevelComponents)
    {
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = 8.0,
            AStarCellSize = 2.0,
            ObstaclePaddingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(0, 0, 400, 200, topLevelComponents.ToList());
        return (new WaveguideConnectionManager(router), router);
    }

    [Fact]
    public void TryRouteHierarchical_TwoGatesWithCrossWire_RoutesEverythingWithoutFallback()
    {
        var a1 = GroupHierarchyTestDesign.CreateComponent(20, 80, 15, 15, "a1");
        var a2 = GroupHierarchyTestDesign.CreateComponent(70, 80, 15, 15, "a2");
        var b1 = GroupHierarchyTestDesign.CreateComponent(200, 80, 15, 15, "b1");
        var b2 = GroupHierarchyTestDesign.CreateComponent(250, 80, 15, 15, "b2");
        var gateA = GroupHierarchyTestDesign.Group("gateA", a1, a2);
        var gateB = GroupHierarchyTestDesign.Group("gateB", b1, b2);
        var (manager, router) = CreateManagerWithGrid(new Component[] { gateA, gateB });

        var crossWire = GroupHierarchyTestDesign.Connect(a2, b1);
        var insideA = GroupHierarchyTestDesign.Connect(a1, a2);
        var insideB = GroupHierarchyTestDesign.Connect(b1, b2);
        foreach (var connection in new[] { crossWire, insideA, insideB })
            manager.AddExistingConnection(connection);

        var plan = GroupHierarchyRoutePlan.Build(new[] { crossWire, insideA, insideB });
        var (failedCount, order) = manager.TryRouteHierarchical(
            plan, router, progressCallback: null, CancellationToken.None);

        failedCount.ShouldBe(0, "every wire has free space on this layout");
        order.SequenceEqual(new[] { insideA, insideB, crossWire }).ShouldBeTrue(
            "intra-group wires must be routed before the group-to-group wire");
        foreach (var connection in order)
        {
            connection.RoutedPath.ShouldNotBeNull();
            connection.RoutedPath!.IsBlockedFallback.ShouldBeFalse();
            connection.IsPathValid.ShouldBeTrue();
        }
    }

    [Fact]
    public void RecalculateAllTransmissions_GroupedDesign_LeavesNoUnroutedConnections()
    {
        var a1 = GroupHierarchyTestDesign.CreateComponent(20, 80, 15, 15, "a1");
        var a2 = GroupHierarchyTestDesign.CreateComponent(70, 80, 15, 15, "a2");
        var b1 = GroupHierarchyTestDesign.CreateComponent(200, 80, 15, 15, "b1");
        var b2 = GroupHierarchyTestDesign.CreateComponent(250, 80, 15, 15, "b2");
        var gateA = GroupHierarchyTestDesign.Group("gateA", a1, a2);
        var gateB = GroupHierarchyTestDesign.Group("gateB", b1, b2);
        var (manager, _) = CreateManagerWithGrid(new Component[] { gateA, gateB });

        manager.AddExistingConnection(GroupHierarchyTestDesign.Connect(a1, a2));
        manager.AddExistingConnection(GroupHierarchyTestDesign.Connect(b1, b2));
        manager.AddExistingConnection(GroupHierarchyTestDesign.Connect(a2, b1));

        manager.RecalculateAllTransmissions();

        manager.Connections.Count.ShouldBe(3);
        foreach (var connection in manager.Connections)
        {
            connection.RoutedPath.ShouldNotBeNull();
            connection.RoutedPath!.IsBlockedFallback.ShouldBeFalse();
        }
    }
}
