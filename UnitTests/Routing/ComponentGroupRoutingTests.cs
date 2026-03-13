using Xunit;
using Shouldly;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;

namespace UnitTests.Routing;

/// <summary>
/// Tests for A* routing behavior with ComponentGroup obstacles.
/// Validates that groups allow routing through empty space between child components.
/// </summary>
public class ComponentGroupRoutingTests
{
    /// <summary>
    /// Creates a mock component at the specified position.
    /// </summary>
    private static Component CreateMockComponent(double x, double y, double width, double height)
    {
        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            Guid.NewGuid().ToString(),
            new DiscreteRotation(),
            new List<PhysicalPin>()
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = width,
            HeightMicrometers = height
        };
        return component;
    }

    [Fact]
    public void AddComponentObstacle_RegularComponent_BlocksBoundingBox()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 100, 100, cellSize: 1.0, padding: 0);
        var component = CreateMockComponent(40, 40, 20, 20);

        // Act
        grid.AddComponentObstacle(component);

        // Assert - cells inside component bounds should be blocked
        var (gx1, gy1) = grid.PhysicalToGrid(45, 45);
        var (gx2, gy2) = grid.PhysicalToGrid(55, 55);
        grid.IsBlocked(gx1, gy1).ShouldBeTrue("Center of component should be blocked");
        grid.IsBlocked(gx2, gy2).ShouldBeTrue("Interior of component should be blocked");
    }

    [Fact]
    public void AddComponentObstacle_EmptyGroup_DoesNotBlockAnySpace()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 100, 100, cellSize: 1.0, padding: 0);
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 40,
            PhysicalY = 40,
            WidthMicrometers = 20,
            HeightMicrometers = 20
        };

        // Act
        grid.AddComponentObstacle(group);

        // Assert - no cells should be blocked (empty group has no children)
        for (int gx = 0; gx < grid.Width; gx++)
        {
            for (int gy = 0; gy < grid.Height; gy++)
            {
                grid.IsBlocked(gx, gy).ShouldBeFalse($"Empty group should not block cell ({gx}, {gy})");
            }
        }
    }

    [Fact]
    public void AddComponentObstacle_GroupWithOneChild_BlocksOnlyChildBounds()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 100, 100, cellSize: 1.0, padding: 0);

        // Create group with one child component
        var group = new ComponentGroup("TestGroup");
        var child = CreateMockComponent(40, 40, 10, 10);
        group.AddChild(child);

        // Act
        grid.AddComponentObstacle(group);

        // Assert - only child area should be blocked
        var (gxChild, gyChild) = grid.PhysicalToGrid(45, 45); // Inside child
        grid.IsBlocked(gxChild, gyChild).ShouldBeTrue("Child component area should be blocked");

        var (gxEmpty, gyEmpty) = grid.PhysicalToGrid(60, 60); // Outside child, inside group bounds
        grid.IsBlocked(gxEmpty, gyEmpty).ShouldBeFalse("Empty space in group should not be blocked");
    }

    [Fact]
    public void AddComponentObstacle_GroupWithTwoChildren_AllowsRoutingBetweenChildren()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0, padding: 2);

        // Create group with two child components separated by gap
        var group = new ComponentGroup("TestGroup");
        var child1 = CreateMockComponent(40, 40, 10, 10);  // Top-left
        var child2 = CreateMockComponent(60, 40, 10, 10);  // Top-right, with 10µm gap
        group.AddChild(child1);
        group.AddChild(child2);

        // Act
        grid.AddComponentObstacle(group);

        // Assert - gap between children should be passable
        var (gxGap, gyGap) = grid.PhysicalToGrid(55, 45); // In the gap
        grid.IsBlocked(gxGap, gyGap).ShouldBeFalse("Gap between children should not be blocked");

        // Children themselves should be blocked
        var (gxChild1, gyChild1) = grid.PhysicalToGrid(45, 45);
        var (gxChild2, gyChild2) = grid.PhysicalToGrid(65, 45);
        grid.IsBlocked(gxChild1, gyChild1).ShouldBeTrue("Child1 should be blocked");
        grid.IsBlocked(gxChild2, gyChild2).ShouldBeTrue("Child2 should be blocked");
    }

    [Fact]
    public void AddComponentObstacle_NestedGroup_BlocksOnlyLeafComponents()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0, padding: 0);

        // Create nested group structure
        var outerGroup = new ComponentGroup("OuterGroup");
        var innerGroup = new ComponentGroup("InnerGroup");
        var leaf1 = CreateMockComponent(40, 40, 10, 10);
        var leaf2 = CreateMockComponent(70, 40, 10, 10);

        innerGroup.AddChild(leaf1);
        outerGroup.AddChild(innerGroup);
        outerGroup.AddChild(leaf2);

        // Act
        grid.AddComponentObstacle(outerGroup);

        // Assert - only leaf components should be blocked
        var (gxLeaf1, gyLeaf1) = grid.PhysicalToGrid(45, 45);
        var (gxLeaf2, gyLeaf2) = grid.PhysicalToGrid(75, 45);
        grid.IsBlocked(gxLeaf1, gyLeaf1).ShouldBeTrue("Leaf1 in nested group should be blocked");
        grid.IsBlocked(gxLeaf2, gyLeaf2).ShouldBeTrue("Leaf2 should be blocked");

        // Empty space between should be passable
        var (gxGap, gyGap) = grid.PhysicalToGrid(60, 45);
        grid.IsBlocked(gxGap, gyGap).ShouldBeFalse("Gap between nested children should not be blocked");
    }

    [Fact]
    public void RemoveComponentObstacle_Group_RemovesAllChildObstacles()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0, padding: 0);

        var group = new ComponentGroup("TestGroup");
        var child1 = CreateMockComponent(40, 40, 10, 10);
        var child2 = CreateMockComponent(60, 40, 10, 10);
        group.AddChild(child1);
        group.AddChild(child2);

        grid.AddComponentObstacle(group);

        // Verify children are blocked
        var (gx1, gy1) = grid.PhysicalToGrid(45, 45);
        grid.IsBlocked(gx1, gy1).ShouldBeTrue("Child1 should be blocked before removal");

        // Act
        grid.RemoveComponentObstacle(group);

        // Assert - all child obstacles should be removed
        grid.IsBlocked(gx1, gy1).ShouldBeFalse("Child1 should be unblocked after group removal");

        var (gx2, gy2) = grid.PhysicalToGrid(65, 45);
        grid.IsBlocked(gx2, gy2).ShouldBeFalse("Child2 should be unblocked after group removal");
    }

    [Fact]
    public void UpdateComponentObstacle_Group_UpdatesChildObstacles()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 1.0, padding: 0);

        var group = new ComponentGroup("TestGroup");
        var child = CreateMockComponent(40, 40, 10, 10);
        group.AddChild(child);

        grid.AddComponentObstacle(group);

        // Verify original position is blocked
        var (gxOld, gyOld) = grid.PhysicalToGrid(45, 45);
        grid.IsBlocked(gxOld, gyOld).ShouldBeTrue("Original position should be blocked");

        // Act - move the group (which moves children)
        group.MoveGroup(30, 0); // Move right by 30µm

        grid.UpdateComponentObstacle(group);

        // Assert - old position should be free, new position should be blocked
        grid.IsBlocked(gxOld, gyOld).ShouldBeFalse("Old position should be unblocked after move");

        var (gxNew, gyNew) = grid.PhysicalToGrid(75, 45); // Original 45 + 30 = 75
        grid.IsBlocked(gxNew, gyNew).ShouldBeTrue("New position should be blocked after move");
    }

    [Fact]
    public void GroupWithGap_AllowsRoutingThroughGapBetweenChildren()
    {
        // Arrange
        var grid = new PathfindingGrid(0, 0, 200, 200, cellSize: 2.0, padding: 2);

        // Create group with two children with a wide gap
        var group = new ComponentGroup("TestGroup");
        var child1 = CreateMockComponent(40, 85, 15, 15);  // Ends at X=55
        var child2 = CreateMockComponent(80, 85, 15, 15);  // Starts at X=80
        // Gap: 55 to 80 = 25µm
        // With padding=2: child1 blocked until X=57, child2 blocked from X=78
        // Clear gap: X=57 to X=78 = 21µm
        group.AddChild(child1);
        group.AddChild(child2);

        grid.AddComponentObstacle(group);

        // Assert - gap between children should be passable (not blocked)
        // Test points in the clear gap (accounting for padding)
        for (double testX = 62; testX <= 73; testX += 5)
        {
            var (gx, gy) = grid.PhysicalToGrid(testX, 92.5); // Middle of components vertically
            grid.IsBlocked(gx, gy).ShouldBeFalse(
                $"Gap at physical ({testX:F1}, 92.5) should not be blocked by group");
        }

        // Children themselves should be blocked
        var (gxChild1, gyChild1) = grid.PhysicalToGrid(47, 92);
        var (gxChild2, gyChild2) = grid.PhysicalToGrid(87, 92);
        grid.IsBlocked(gxChild1, gyChild1).ShouldBeTrue("Child1 should be blocked");
        grid.IsBlocked(gxChild2, gyChild2).ShouldBeTrue("Child2 should be blocked");
    }
}
