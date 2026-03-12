using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for ComponentGroupRenderer.
/// </summary>
public class ComponentGroupRendererTests
{
    [Fact]
    public void CalculateGroupBounds_WithSingleComponent_ReturnsCorrectBounds()
    {
        // Arrange
        var component = CreateTestComponent(100, 100, 50, 30);
        var groupInstance = CreateGroupInstance(component);

        // Act
        var (minX, minY, maxX, maxY) = ComponentGroupRenderer.CalculateGroupBounds(groupInstance);

        // Assert
        minX.ShouldBe(100);
        minY.ShouldBe(100);
        maxX.ShouldBe(150); // 100 + 50
        maxY.ShouldBe(130); // 100 + 30
    }

    [Fact]
    public void CalculateGroupBounds_WithMultipleComponents_ReturnsEncompassingBounds()
    {
        // Arrange
        var comp1 = CreateTestComponent(0, 0, 50, 50);
        var comp2 = CreateTestComponent(100, 100, 50, 50);
        var comp3 = CreateTestComponent(50, 50, 50, 50);
        var groupInstance = CreateGroupInstance(comp1, comp2, comp3);

        // Act
        var (minX, minY, maxX, maxY) = ComponentGroupRenderer.CalculateGroupBounds(groupInstance);

        // Assert
        minX.ShouldBe(0);
        minY.ShouldBe(0);
        maxX.ShouldBe(150); // 100 + 50
        maxY.ShouldBe(150); // 100 + 50
    }

    [Fact]
    public void CalculateGroupBounds_WithEmptyGroup_ReturnsZeroBounds()
    {
        // Arrange
        var groupInstance = CreateGroupInstance();

        // Act
        var (minX, minY, maxX, maxY) = ComponentGroupRenderer.CalculateGroupBounds(groupInstance);

        // Assert
        minX.ShouldBe(0);
        minY.ShouldBe(0);
        maxX.ShouldBe(0);
        maxY.ShouldBe(0);
    }

    [Fact]
    public void CalculatePaddedBounds_AppliesPaddingCorrectly()
    {
        // Arrange
        var component = CreateTestComponent(100, 100, 50, 30);
        var groupInstance = CreateGroupInstance(component);
        const double padding = 10.0;

        // Act
        var (x, y, width, height) = ComponentGroupRenderer.CalculatePaddedBounds(groupInstance, padding);

        // Assert
        x.ShouldBe(90); // 100 - 10
        y.ShouldBe(90); // 100 - 10
        width.ShouldBe(70); // 50 + 2*10
        height.ShouldBe(50); // 30 + 2*10
    }

    [Fact]
    public void HitTestGroupBounds_PointInsideBounds_ReturnsTrue()
    {
        // Arrange
        var component = CreateTestComponent(100, 100, 50, 30);
        var groupInstance = CreateGroupInstance(component);

        // Act
        bool hit = ComponentGroupRenderer.HitTestGroupBounds(groupInstance, 120, 110, 10.0);

        // Assert
        hit.ShouldBeTrue();
    }

    [Fact]
    public void HitTestGroupBounds_PointOutsideBounds_ReturnsFalse()
    {
        // Arrange
        var component = CreateTestComponent(100, 100, 50, 30);
        var groupInstance = CreateGroupInstance(component);

        // Act
        bool hit = ComponentGroupRenderer.HitTestGroupBounds(groupInstance, 200, 200, 10.0);

        // Assert
        hit.ShouldBeFalse();
    }

    [Fact]
    public void HitTestGroupBounds_PointOnBorder_ReturnsTrue()
    {
        // Arrange
        var component = CreateTestComponent(100, 100, 50, 30);
        var groupInstance = CreateGroupInstance(component);

        // Act - point exactly on the padded border
        bool hit = ComponentGroupRenderer.HitTestGroupBounds(groupInstance, 90, 100, 10.0);

        // Assert
        hit.ShouldBeTrue();
    }

    [Fact]
    public void GetTopLevelGroup_ComponentNotInGroup_ReturnsNull()
    {
        // Arrange
        var component = CreateTestComponent(0, 0, 50, 50);
        var groupInstances = new Dictionary<Guid, ComponentGroupInstance>();

        // Act
        var result = ComponentGroupRenderer.GetTopLevelGroup(component, groupInstances);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GetTopLevelGroup_ComponentInGroup_ReturnsGroup()
    {
        // Arrange
        var component = CreateTestComponent(0, 0, 50, 50);
        var groupInstance = CreateGroupInstance(component);
        component.ParentGroupInstanceId = groupInstance.InstanceId;
        var groupInstances = new Dictionary<Guid, ComponentGroupInstance>
        {
            { groupInstance.InstanceId, groupInstance }
        };

        // Act
        var result = ComponentGroupRenderer.GetTopLevelGroup(component, groupInstances);

        // Assert
        result.ShouldNotBeNull();
        result.InstanceId.ShouldBe(groupInstance.InstanceId);
    }

    [Fact]
    public void ShouldHighlightAsGroupMember_ComponentInGroup_ReturnsTrue()
    {
        // Arrange
        var component = CreateTestComponent(0, 0, 50, 50);
        var groupInstance = CreateGroupInstance(component);
        component.ParentGroupInstanceId = groupInstance.InstanceId;

        // Act
        bool shouldHighlight = ComponentGroupRenderer.ShouldHighlightAsGroupMember(component, groupInstance);

        // Assert
        shouldHighlight.ShouldBeTrue();
    }

    [Fact]
    public void ShouldHighlightAsGroupMember_ComponentNotInGroup_ReturnsFalse()
    {
        // Arrange
        var component = CreateTestComponent(0, 0, 50, 50);
        var groupInstance = CreateGroupInstance();

        // Act
        bool shouldHighlight = ComponentGroupRenderer.ShouldHighlightAsGroupMember(component, groupInstance);

        // Assert
        shouldHighlight.ShouldBeFalse();
    }

    [Fact]
    public void ShouldHighlightAsGroupMember_NullGroupInstance_ReturnsFalse()
    {
        // Arrange
        var component = CreateTestComponent(0, 0, 50, 50);

        // Act
        bool shouldHighlight = ComponentGroupRenderer.ShouldHighlightAsGroupMember(component, null);

        // Assert
        shouldHighlight.ShouldBeFalse();
    }

    // Helper methods

    private Component CreateTestComponent(double x, double y, double width, double height)
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        return component;
    }

    private ComponentGroupInstance CreateGroupInstance(params Component[] components)
    {
        var groupDefinition = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            Category = "Test"
        };

        var groupInstance = new ComponentGroupInstance(groupDefinition);

        foreach (var component in components)
        {
            groupInstance.Components.Add(component);
        }

        return groupInstance;
    }
}
