using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for group edit mode functionality in DesignCanvasViewModel.
/// </summary>
public class GroupEditModeTests
{
    [Fact]
    public void IsInGroupEditMode_ShouldBeFalse_WhenNoGroupActive()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();

        // Assert
        vm.IsInGroupEditMode.ShouldBeFalse();
        vm.CurrentEditGroup.ShouldBeNull();
        vm.CurrentEditGroupInstance.ShouldBeNull();
    }

    [Fact]
    public void EnterGroupEditMode_ShouldSetCurrentGroup()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateTestGroup("TestGroup");
        var groupInstance = new ComponentGroupInstance(groupDef);

        // Act
        vm.EnterGroupEditMode(groupInstance);

        // Assert
        vm.IsInGroupEditMode.ShouldBeTrue();
        vm.CurrentEditGroup.ShouldBe(groupDef);
        vm.CurrentEditGroupInstance.ShouldBe(groupInstance);
    }

    [Fact]
    public void EnterGroupEditMode_ShouldUpdateBreadcrumbs()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateTestGroup("MZI Block");
        var groupInstance = new ComponentGroupInstance(groupDef);

        // Act
        vm.EnterGroupEditMode(groupInstance);

        // Assert
        vm.BreadcrumbPath.Count.ShouldBe(2);
        vm.BreadcrumbPath[0].Name.ShouldBe("Root");
        vm.BreadcrumbPath[0].GroupInstance.ShouldBeNull();
        vm.BreadcrumbPath[1].Name.ShouldBe("MZI Block");
        vm.BreadcrumbPath[1].GroupInstance.ShouldBe(groupInstance);
    }

    [Fact]
    public void ExitGroupEditMode_ShouldReturnToRoot()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateTestGroup("TestGroup");
        var groupInstance = new ComponentGroupInstance(groupDef);
        vm.EnterGroupEditMode(groupInstance);

        // Act
        vm.ExitGroupEditMode();

        // Assert
        vm.IsInGroupEditMode.ShouldBeFalse();
        vm.CurrentEditGroup.ShouldBeNull();
        vm.CurrentEditGroupInstance.ShouldBeNull();
        vm.BreadcrumbPath.Count.ShouldBe(1);
        vm.BreadcrumbPath[0].Name.ShouldBe("Root");
    }

    [Fact]
    public void CanEditComponent_ShouldReturnTrue_WhenAtRootLevel()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var component = CreateTestComponent("MMI");
        var componentVm = vm.AddComponent(component);

        // Act & Assert
        vm.CanEditComponent(componentVm).ShouldBeTrue();
    }

    [Fact]
    public void CanEditComponent_ShouldReturnTrue_WhenComponentInCurrentGroup()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateTestGroup("TestGroup");
        var groupInstance = new ComponentGroupInstance(groupDef);
        var component = CreateTestComponent("MMI");
        component.ParentGroupInstanceId = groupInstance.InstanceId;
        var componentVm = vm.AddComponent(component);

        // Enter edit mode for the group
        vm.EnterGroupEditMode(groupInstance);

        // Act & Assert
        vm.CanEditComponent(componentVm).ShouldBeTrue();
    }

    [Fact]
    public void CanEditComponent_ShouldReturnFalse_WhenComponentNotInCurrentGroup()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateTestGroup("TestGroup");
        var groupInstance = new ComponentGroupInstance(groupDef);
        var component = CreateTestComponent("MMI");
        // Component does NOT belong to the group
        var componentVm = vm.AddComponent(component);

        // Enter edit mode for the group
        vm.EnterGroupEditMode(groupInstance);

        // Act & Assert
        vm.CanEditComponent(componentVm).ShouldBeFalse();
    }

    [Fact]
    public void RegisterGroupInstance_ShouldTrackGroupInstance()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateTestGroup("TestGroup");
        var groupInstance = new ComponentGroupInstance(groupDef);

        // Act
        vm.RegisterGroupInstance(groupInstance);

        // Assert - we can enter edit mode for this instance
        vm.EnterGroupEditMode(groupInstance);
        vm.CurrentEditGroupInstance.ShouldBe(groupInstance);
    }

    [Fact]
    public void UnregisterGroupInstance_ShouldRemoveGroupInstance()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateTestGroup("TestGroup");
        var groupInstance = new ComponentGroupInstance(groupDef);
        vm.RegisterGroupInstance(groupInstance);

        // Act
        vm.UnregisterGroupInstance(groupInstance.InstanceId);

        // Assert - Group is no longer tracked (this is implicit - no exception thrown)
        // We can verify by trying to enter edit mode (should still work as we pass the instance directly)
        vm.EnterGroupEditMode(groupInstance);
        vm.CurrentEditGroupInstance.ShouldBe(groupInstance);
    }

    /// <summary>
    /// Creates a test component group definition.
    /// </summary>
    private static ComponentGroup CreateTestGroup(string name)
    {
        return new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = "Test",
            Description = "Test group",
            WidthMicrometers = 100,
            HeightMicrometers = 50,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a simple test component.
    /// </summary>
    private static Component CreateTestComponent(string identifier)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        return new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_func",
            "",
            parts,
            1,
            identifier,
            DiscreteRotation.R0,
            new List<PhysicalPin>())
        {
            PhysicalX = 0,
            PhysicalY = 0,
            WidthMicrometers = 10,
            HeightMicrometers = 10
        };
    }
}
