using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for group edit mode workflow.
/// Tests the full workflow from creating groups to editing them.
/// </summary>
public class GroupEditModeIntegrationTests
{
    [Fact]
    public void FullWorkflow_CreateGroupInstance_EnterEditMode_EditComponents_ExitEditMode()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateMZIGroup();
        var groupInstance = new ComponentGroupInstance(groupDef);

        // Create components belonging to the group
        var mmi1 = CreateTestComponent("MMI1");
        mmi1.ParentGroupInstanceId = groupInstance.InstanceId;
        var mmi2 = CreateTestComponent("MMI2");
        mmi2.ParentGroupInstanceId = groupInstance.InstanceId;

        // Create a component NOT in the group
        var externalComponent = CreateTestComponent("External");

        // Add components to canvas
        var mmi1Vm = vm.AddComponent(mmi1);
        var mmi2Vm = vm.AddComponent(mmi2);
        var externalVm = vm.AddComponent(externalComponent);

        // Register the group instance
        vm.RegisterGroupInstance(groupInstance);
        groupInstance.Components.Add(mmi1);
        groupInstance.Components.Add(mmi2);

        // Act - Enter edit mode
        vm.EnterGroupEditMode(groupInstance);

        // Assert - In edit mode
        vm.IsInGroupEditMode.ShouldBeTrue();
        vm.CurrentEditGroup.ShouldBe(groupDef);
        vm.CurrentEditGroupInstance.ShouldBe(groupInstance);

        // Assert - Can edit group components
        vm.CanEditComponent(mmi1Vm).ShouldBeTrue();
        vm.CanEditComponent(mmi2Vm).ShouldBeTrue();

        // Assert - Cannot edit external components
        vm.CanEditComponent(externalVm).ShouldBeFalse();

        // Assert - Breadcrumbs show correct path
        vm.BreadcrumbPath.Count.ShouldBe(2);
        vm.BreadcrumbPath[0].Name.ShouldBe("Root");
        vm.BreadcrumbPath[1].Name.ShouldBe("MZI");

        // Act - Exit edit mode
        vm.ExitGroupEditMode();

        // Assert - Back to root
        vm.IsInGroupEditMode.ShouldBeFalse();
        vm.CurrentEditGroup.ShouldBeNull();

        // Assert - Can now edit all components
        vm.CanEditComponent(mmi1Vm).ShouldBeTrue();
        vm.CanEditComponent(mmi2Vm).ShouldBeTrue();
        vm.CanEditComponent(externalVm).ShouldBeTrue();
    }

    [Fact]
    public void EnterGroupEditModeForComponent_ShouldEnterEditMode_WhenComponentBelongsToGroup()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateMZIGroup();
        var groupInstance = new ComponentGroupInstance(groupDef);

        var component = CreateTestComponent("MMI");
        component.ParentGroupInstanceId = groupInstance.InstanceId;
        var componentVm = vm.AddComponent(component);

        vm.RegisterGroupInstance(groupInstance);

        // Act - Double-click would call this
        vm.EnterGroupEditModeForComponent(componentVm);

        // Assert
        vm.IsInGroupEditMode.ShouldBeTrue();
        vm.CurrentEditGroupInstance.ShouldBe(groupInstance);
    }

    [Fact]
    public void EnterGroupEditModeForComponent_ShouldDoNothing_WhenComponentNotInGroup()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var component = CreateTestComponent("MMI");
        var componentVm = vm.AddComponent(component);

        // Act - Try to enter edit mode for component not in a group
        vm.EnterGroupEditModeForComponent(componentVm);

        // Assert - Should still be at root
        vm.IsInGroupEditMode.ShouldBeFalse();
        vm.CurrentEditGroup.ShouldBeNull();
    }

    [Fact]
    public void MultipleGroups_ShouldTrackEachSeparately()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();

        var group1Def = CreateMZIGroup();
        var group1Instance = new ComponentGroupInstance(group1Def);

        var group2Def = CreateRingGroup();
        var group2Instance = new ComponentGroupInstance(group2Def);

        var comp1 = CreateTestComponent("MZI_MMI");
        comp1.ParentGroupInstanceId = group1Instance.InstanceId;
        var comp1Vm = vm.AddComponent(comp1);

        var comp2 = CreateTestComponent("Ring_Coupler");
        comp2.ParentGroupInstanceId = group2Instance.InstanceId;
        var comp2Vm = vm.AddComponent(comp2);

        vm.RegisterGroupInstance(group1Instance);
        vm.RegisterGroupInstance(group2Instance);

        // Act & Assert - Enter group 1 edit mode
        vm.EnterGroupEditMode(group1Instance);
        vm.CanEditComponent(comp1Vm).ShouldBeTrue();
        vm.CanEditComponent(comp2Vm).ShouldBeFalse();

        // Act & Assert - Switch to group 2 edit mode
        vm.ExitGroupEditMode();
        vm.EnterGroupEditMode(group2Instance);
        vm.CanEditComponent(comp1Vm).ShouldBeFalse();
        vm.CanEditComponent(comp2Vm).ShouldBeTrue();
    }

    [Fact]
    public void Breadcrumbs_ShouldUpdate_WhenEnteringAndExitingEditMode()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var groupDef = CreateMZIGroup();
        var groupInstance = new ComponentGroupInstance(groupDef);

        // Initially at root
        vm.BreadcrumbPath.Count.ShouldBe(1);
        vm.BreadcrumbPath[0].Name.ShouldBe("Root");

        // Enter edit mode
        vm.EnterGroupEditMode(groupInstance);
        vm.BreadcrumbPath.Count.ShouldBe(2);
        vm.BreadcrumbPath[1].Name.ShouldBe("MZI");

        // Exit edit mode
        vm.ExitGroupEditMode();
        vm.BreadcrumbPath.Count.ShouldBe(1);
        vm.BreadcrumbPath[0].Name.ShouldBe("Root");
    }

    /// <summary>
    /// Creates an MZI group definition.
    /// </summary>
    private static ComponentGroup CreateMZIGroup()
    {
        return new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = "MZI",
            Category = "Interferometers",
            Description = "Mach-Zehnder Interferometer",
            WidthMicrometers = 200,
            HeightMicrometers = 100,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a ring resonator group definition.
    /// </summary>
    private static ComponentGroup CreateRingGroup()
    {
        return new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = "Ring Resonator",
            Category = "Filters",
            Description = "Ring resonator filter",
            WidthMicrometers = 150,
            HeightMicrometers = 150,
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
