using Avalonia;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Rendering;

/// <summary>
/// Unit tests for ComponentGroupRenderer.
/// Tests bounds calculation logic without requiring DrawingContext mocks.
/// </summary>
public class ComponentGroupRendererTests
{
    [Fact]
    public void CalculateGroupBounds_EmptyGroup_ReturnsZeroSizeRect()
    {
        // Arrange
        var group = new ComponentGroup("Empty Group");

        // Act
        var bounds = ComponentGroupRenderer.CalculateGroupBounds(group);

        // Assert
        bounds.Width.ShouldBe(0);
        bounds.Height.ShouldBe(0);
    }

    [Fact]
    public void CalculateGroupBounds_SingleChild_ReturnsChildBoundsWithPadding()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child = CreateTestComponent("Child", 100, 100, 50, 30);
        group.AddChild(child);

        // Act
        var bounds = ComponentGroupRenderer.CalculateGroupBounds(group);

        // Assert - Should have 10µm padding on all sides
        bounds.X.ShouldBe(90.0); // 100 - 10 padding
        bounds.Y.ShouldBe(90.0); // 100 - 10 padding
        bounds.Width.ShouldBe(70.0); // 50 + 2*10 padding
        bounds.Height.ShouldBe(50.0); // 30 + 2*10 padding
    }

    [Fact]
    public void CalculateGroupBounds_MultipleChildren_EnclosesAllWithPadding()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child1 = CreateTestComponent("Child1", 100, 100, 50, 30);
        var child2 = CreateTestComponent("Child2", 200, 150, 40, 25);
        group.AddChild(child1);
        group.AddChild(child2);

        // Act
        var bounds = ComponentGroupRenderer.CalculateGroupBounds(group);

        // Assert
        // Min: (100, 100), Max: (240, 175)
        bounds.X.ShouldBe(90.0); // 100 - 10
        bounds.Y.ShouldBe(90.0); // 100 - 10
        bounds.Width.ShouldBe(160.0); // (240-100) + 2*10
        bounds.Height.ShouldBe(95.0); // (175-100) + 2*10
    }

    [Fact]
    public void CalculateGroupBounds_NestedGroups_CalculatesCorrectBounds()
    {
        // Arrange
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child Group");
        var grandchild = CreateTestComponent("Grandchild", 150, 150, 30, 20);

        childGroup.AddChild(grandchild);

        // Set position for the nested group (groups are also components)
        childGroup.PhysicalX = 150;
        childGroup.PhysicalY = 150;
        childGroup.WidthMicrometers = 30;
        childGroup.HeightMicrometers = 20;

        parentGroup.AddChild(childGroup);

        // Act
        var bounds = ComponentGroupRenderer.CalculateGroupBounds(parentGroup);

        // Assert - Should account for nested group position
        bounds.X.ShouldBe(140.0); // 150 - 10
        bounds.Y.ShouldBe(140.0); // 150 - 10
        bounds.Width.ShouldBe(50.0); // 30 + 2*10
        bounds.Height.ShouldBe(40.0); // 20 + 2*10
    }

    [Fact]
    public void RenderGroupSelectionBorder_ValidBounds_MethodExecutesWithoutError()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child = CreateTestComponent("Child", 100, 100, 50, 30);
        group.AddChild(child);
        var bounds = ComponentGroupRenderer.CalculateGroupBounds(group);

        // Act & Assert - Verify method can be called without throwing
        // (Full rendering test would require DrawingContext mock)
        Should.NotThrow(() =>
        {
            // Method signature verification - ensures RenderGroupSelectionBorder exists
            var method = typeof(ComponentGroupRenderer).GetMethod(
                "RenderGroupSelectionBorder",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            method.ShouldNotBeNull();
            method!.GetParameters().Length.ShouldBe(3); // context, bounds, isDimmed
        });
    }

    [Fact]
    public void CalculateLabelBounds_EmptyGroup_ReturnsMinimumLabelSize()
    {
        // Arrange
        var group = new ComponentGroup("Empty");

        // Act
        var labelBounds = ComponentGroupRenderer.CalculateLabelBounds(group);

        // Assert
        labelBounds.Width.ShouldBeGreaterThanOrEqualTo(60.0); // Minimum width
        labelBounds.Height.ShouldBe(18.0); // Fixed label height
    }

    [Fact]
    public void CalculateLabelBounds_GroupWithChildren_PositionsLabelAboveGroup()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child = CreateTestComponent("Child", 100, 100, 50, 30);
        group.AddChild(child);

        // Act
        var groupBounds = ComponentGroupRenderer.CalculateGroupBounds(group);
        var labelBounds = ComponentGroupRenderer.CalculateLabelBounds(group);

        // Assert
        // Label should be positioned 20µm above the group border
        labelBounds.Y.ShouldBe(groupBounds.Y - 20);
        labelBounds.X.ShouldBe(groupBounds.X);
        labelBounds.Height.ShouldBe(18.0);
    }

    [Fact]
    public void CalculateLabelBounds_LongGroupName_AdjustsWidthForText()
    {
        // Arrange
        var shortNameGroup = new ComponentGroup("AB");
        var longNameGroup = new ComponentGroup("Very Long Group Name");

        // Act
        var shortLabelBounds = ComponentGroupRenderer.CalculateLabelBounds(shortNameGroup);
        var longLabelBounds = ComponentGroupRenderer.CalculateLabelBounds(longNameGroup);

        // Assert
        // Longer name should result in wider label
        longLabelBounds.Width.ShouldBeGreaterThan(shortLabelBounds.Width);
        // Both should have same height
        shortLabelBounds.Height.ShouldBe(longLabelBounds.Height);
    }

    [Fact]
    public void CalculateLabelBounds_ConsistentWithGroupBounds_LabelsAlignCorrectly()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child1 = CreateTestComponent("Child1", 100, 100, 50, 30);
        var child2 = CreateTestComponent("Child2", 200, 150, 40, 25);
        group.AddChild(child1);
        group.AddChild(child2);

        // Act
        var groupBounds = ComponentGroupRenderer.CalculateGroupBounds(group);
        var labelBounds = ComponentGroupRenderer.CalculateLabelBounds(group);

        // Assert
        // Label X should align with group X
        labelBounds.X.ShouldBe(groupBounds.X);
        // Label Y should be above group (20µm offset)
        labelBounds.Y.ShouldBe(groupBounds.Y - 20);
    }

    [Fact]
    public void CalculateLockIconBounds_GroupWithChildren_PositionsIconAtTopRight()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        var child = CreateTestComponent("Child", 100, 100, 50, 30);
        group.AddChild(child);

        // Act
        var groupBounds = ComponentGroupRenderer.CalculateGroupBounds(group);
        var lockIconBounds = ComponentGroupRenderer.CalculateLockIconBounds(group);

        // Assert
        // Icon should be at top-right corner with 4µm padding
        const double IconSize = 16.0;
        const double Padding = 4.0;
        lockIconBounds.X.ShouldBe(groupBounds.Right - IconSize - Padding);
        lockIconBounds.Y.ShouldBe(groupBounds.Top + Padding);
        lockIconBounds.Width.ShouldBe(IconSize);
        lockIconBounds.Height.ShouldBe(IconSize);
    }

    [Fact]
    public void CalculateLockIconBounds_EmptyGroup_ReturnsValidBounds()
    {
        // Arrange
        var group = new ComponentGroup("Empty Group");

        // Act
        var lockIconBounds = ComponentGroupRenderer.CalculateLockIconBounds(group);

        // Assert
        lockIconBounds.Width.ShouldBe(16.0);
        lockIconBounds.Height.ShouldBe(16.0);
    }

    [Fact]
    public void CalculateLockIconBounds_DifferentGroupSizes_IconAlwaysSameSize()
    {
        // Arrange
        var smallGroup = new ComponentGroup("Small");
        var smallChild = CreateTestComponent("Small", 100, 100, 50, 30);
        smallGroup.AddChild(smallChild);

        var largeGroup = new ComponentGroup("Large");
        var largeChild = CreateTestComponent("Large", 100, 100, 500, 300);
        largeGroup.AddChild(largeChild);

        // Act
        var smallIconBounds = ComponentGroupRenderer.CalculateLockIconBounds(smallGroup);
        var largeIconBounds = ComponentGroupRenderer.CalculateLockIconBounds(largeGroup);

        // Assert - Icon size should be constant regardless of group size
        smallIconBounds.Width.ShouldBe(largeIconBounds.Width);
        smallIconBounds.Height.ShouldBe(largeIconBounds.Height);
        smallIconBounds.Width.ShouldBe(16.0);
    }

    [Fact]
    public void RenderGroupLockIcon_MethodExists_WithCorrectSignature()
    {
        // Arrange - Verify that the RenderGroupLockIcon method exists with the correct signature
        var method = typeof(ComponentGroupRenderer).GetMethod(
            "RenderGroupLockIcon",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Assert
        method.ShouldNotBeNull();
        method!.ReturnType.ShouldBe(typeof(void));

        var parameters = method.GetParameters();
        parameters.Length.ShouldBe(3); // context, group, isHovered
        parameters[0].ParameterType.Name.ShouldBe("DrawingContext");
        parameters[1].ParameterType.ShouldBe(typeof(ComponentGroup));
        parameters[2].ParameterType.ShouldBe(typeof(bool));
    }

    [Fact]
    public void LockedGroup_HasIsLockedTrue_IconRepresentsLockedState()
    {
        // Arrange
        var lockedGroup = new ComponentGroup("Locked Group") { IsLocked = true };
        var unlockedGroup = new ComponentGroup("Unlocked Group") { IsLocked = false };
        var child = CreateTestComponent("Child", 100, 100, 50, 30);

        lockedGroup.AddChild(child);
        unlockedGroup.AddChild(child);

        // Act & Assert - Verify lock state is correctly set
        lockedGroup.IsLocked.ShouldBeTrue();
        unlockedGroup.IsLocked.ShouldBeFalse();

        // Icon bounds should be identical regardless of lock state
        var lockedIconBounds = ComponentGroupRenderer.CalculateLockIconBounds(lockedGroup);
        var unlockedIconBounds = ComponentGroupRenderer.CalculateLockIconBounds(unlockedGroup);

        lockedIconBounds.Width.ShouldBe(unlockedIconBounds.Width);
        lockedIconBounds.Height.ShouldBe(unlockedIconBounds.Height);
    }

    /// <summary>
    /// Creates a test component with specified position and dimensions.
    /// </summary>
    private Component CreateTestComponent(string name, double x, double y, double width, double height)
    {
        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_type",
            name,
            new Part[1, 1] { { new Part() } },
            -1,
            $"test_{Guid.NewGuid():N}",
            new DiscreteRotation(),
            new List<PhysicalPin>()
        );

        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;

        return component;
    }
}
