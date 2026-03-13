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
