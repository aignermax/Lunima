using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for ComponentGroup rendering with DesignCanvasViewModel.
/// Tests the complete flow from Core classes through ViewModel to rendering data.
/// </summary>
public class ComponentGroupRenderingIntegrationTests
{
    [Fact]
    public void RegisterGroupInstance_AddsGroupToCanvas()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDefinition = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = "Test MZI",
            Category = "Interferometers"
        };
        var groupInstance = new ComponentGroupInstance(groupDefinition);

        // Act
        vm.RegisterGroupInstance(groupInstance);

        // Assert
        var allGroups = vm.GetAllGroupInstances().ToList();
        allGroups.Count.ShouldBe(1);
        allGroups[0].InstanceId.ShouldBe(groupInstance.InstanceId);
    }

    [Fact]
    public void GetGroupInstance_RetrievesRegisteredGroup()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDefinition = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = "Test Group"
        };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        vm.RegisterGroupInstance(groupInstance);

        // Act
        var retrieved = vm.GetGroupInstance(groupInstance.InstanceId);

        // Assert
        retrieved.ShouldNotBeNull();
        retrieved.InstanceId.ShouldBe(groupInstance.InstanceId);
        retrieved.Name.ShouldBe("Test Group");
    }

    [Fact]
    public void UnregisterGroupInstance_RemovesGroupFromCanvas()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDefinition = new ComponentGroup { Id = Guid.NewGuid(), Name = "Test" };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        vm.RegisterGroupInstance(groupInstance);

        // Act
        vm.UnregisterGroupInstance(groupInstance.InstanceId);

        // Assert
        var allGroups = vm.GetAllGroupInstances().ToList();
        allGroups.Count.ShouldBe(0);
    }

    [Fact]
    public void GroupInstanceWithComponents_CalculatesBoundsCorrectly()
    {
        // Arrange
        var component1 = CreateTestComponent(0, 0, 100, 50);
        var component2 = CreateTestComponent(150, 100, 100, 50);

        var groupDefinition = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = "MZI"
        };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        groupInstance.Components.Add(component1);
        groupInstance.Components.Add(component2);

        // Act
        var (minX, minY, maxX, maxY) = ComponentGroupRenderer.CalculateGroupBounds(groupInstance);

        // Assert
        minX.ShouldBe(0);
        minY.ShouldBe(0);
        maxX.ShouldBe(250); // 150 + 100
        maxY.ShouldBe(150); // 100 + 50
    }

    [Fact]
    public void ComponentWithParentGroup_IsRecognizedByRenderer()
    {
        // Arrange
        var component = CreateTestComponent(0, 0, 50, 50);
        var groupDefinition = new ComponentGroup { Id = Guid.NewGuid(), Name = "Test" };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        groupInstance.Components.Add(component);
        component.ParentGroupInstanceId = groupInstance.InstanceId;

        // Act
        bool shouldHighlight = ComponentGroupRenderer.ShouldHighlightAsGroupMember(
            component,
            groupInstance);

        // Assert
        shouldHighlight.ShouldBeTrue();
    }

    [Fact]
    public void MultipleGroupInstances_AreTrackedSeparately()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var group1 = new ComponentGroupInstance(new ComponentGroup { Id = Guid.NewGuid(), Name = "MZI" });
        var group2 = new ComponentGroupInstance(new ComponentGroup { Id = Guid.NewGuid(), Name = "Ring" });

        // Act
        vm.RegisterGroupInstance(group1);
        vm.RegisterGroupInstance(group2);

        // Assert
        var allGroups = vm.GetAllGroupInstances().ToList();
        allGroups.Count.ShouldBe(2);
        allGroups.ShouldContain(g => g.Name == "MZI");
        allGroups.ShouldContain(g => g.Name == "Ring");
    }

    [Fact]
    public void HitTestGroupBounds_DetectsPointInsideGroup()
    {
        // Arrange
        var component = CreateTestComponent(100, 100, 100, 100);
        var groupDefinition = new ComponentGroup { Id = Guid.NewGuid(), Name = "Test" };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        groupInstance.Components.Add(component);

        // Act
        bool hitInside = ComponentGroupRenderer.HitTestGroupBounds(groupInstance, 150, 150, 10.0);
        bool hitOutside = ComponentGroupRenderer.HitTestGroupBounds(groupInstance, 300, 300, 10.0);

        // Assert
        hitInside.ShouldBeTrue();
        hitOutside.ShouldBeFalse();
    }

    [Fact]
    public void CalculatePaddedBounds_ProvidesRenderingDimensions()
    {
        // Arrange
        var component = CreateTestComponent(50, 50, 100, 100);
        var groupDefinition = new ComponentGroup { Id = Guid.NewGuid(), Name = "Test" };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        groupInstance.Components.Add(component);

        // Act
        var (x, y, width, height) = ComponentGroupRenderer.CalculatePaddedBounds(groupInstance, 10.0);

        // Assert
        x.ShouldBe(40); // 50 - 10
        y.ShouldBe(40); // 50 - 10
        width.ShouldBe(120); // 100 + 2*10
        height.ShouldBe(120); // 100 + 2*10
    }

    [Fact]
    public void EnterGroupEditMode_UpdatesViewModel()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDefinition = new ComponentGroup { Id = Guid.NewGuid(), Name = "MZI" };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        vm.RegisterGroupInstance(groupInstance);

        // Act
        vm.EnterGroupEditMode(groupInstance);

        // Assert
        vm.IsInGroupEditMode.ShouldBeTrue();
        vm.CurrentEditGroupInstance.ShouldBe(groupInstance);
    }

    [Fact]
    public void ExitGroupEditMode_ClearsEditContext()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDefinition = new ComponentGroup { Id = Guid.NewGuid(), Name = "MZI" };
        var groupInstance = new ComponentGroupInstance(groupDefinition);
        vm.RegisterGroupInstance(groupInstance);
        vm.EnterGroupEditMode(groupInstance);

        // Act
        vm.ExitGroupEditMode();

        // Assert
        vm.IsInGroupEditMode.ShouldBeFalse();
        vm.CurrentEditGroupInstance.ShouldBeNull();
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
}
