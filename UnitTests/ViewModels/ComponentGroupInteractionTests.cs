using Avalonia;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for ComponentGroup UI interaction.
/// Tests hit testing, mouse interaction, and edit mode transitions.
/// </summary>
public class ComponentGroupInteractionTests
{
    [Fact]
    public void HitTestComponent_OnComponentGroup_DetectsGroupBounds()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Hit test at a point inside the group bounds (between comp1 and comp2)
        var hitPoint = new Point(150, 115);
        var hitComponent = DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should hit the group
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(group);
    }

    [Fact]
    public void HitTestComponent_OnComponentGroupChild_StillDetectsGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Hit test directly on child component position
        var hitPoint = new Point(125, 115);
        var hitComponent = DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should hit the group (not individual child)
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(group);
    }

    [Fact]
    public void HitTestComponent_OutsideGroupBounds_ReturnsNull()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Hit test at a point outside the group bounds
        var hitPoint = new Point(50, 50);
        var hitComponent = DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should not hit anything
        hitComponent.ShouldBeNull();
    }

    [Fact]
    public void HitTestComponent_EmptyGroup_UsesGroupPosition()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("EmptyGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Hit test at the group's position
        var hitPoint = new Point(125, 115);
        var hitComponent = DesignCanvasHitTesting.HitTestComponent(hitPoint, canvas);

        // Assert - Should hit the empty group
        hitComponent.ShouldNotBeNull();
        hitComponent.Component.ShouldBe(group);
    }

    [Fact]
    public void MoveComponentGroup_UpdatesViewModelPosition()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);
        var comp2 = CreateTestComponent("Comp2", 200, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");
        var initialX = groupVm.X;
        var initialY = groupVm.Y;

        // Act - Move the group
        canvas.MoveComponent(groupVm, 50, 30);

        // Assert - ViewModel position updated
        groupVm.X.ShouldBe(initialX + 50);
        groupVm.Y.ShouldBe(initialY + 30);
    }

    [Fact]
    public async Task MoveComponentGroup_WithExternalConnection_UpdatesConnection()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins("Comp1", 100, 100);
        var comp2 = CreateComponentWithPins("Comp2", 200, 100);
        var externalComp = CreateComponentWithPins("External", 300, 100);

        // Create group with comp1 and comp2
        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Add external pin for comp2
        var externalPin = new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = "ExtPin",
            InternalPin = comp2.PhysicalPins[0],
            RelativeX = 100,
            RelativeY = 0,
            AngleDegrees = 0
        };
        group.AddExternalPin(externalPin);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");
        var externalVm = canvas.AddComponent(externalComp, "ExternalTemplate");

        // Connect external component to group's external pin
        canvas.ConnectPins(comp2.PhysicalPins[0], externalComp.PhysicalPins[0]);
        await canvas.RecalculateRoutesAsync();

        var initialConnectionCount = canvas.Connections.Count;

        // Act - Move the group
        canvas.BeginDragComponent(groupVm);
        canvas.MoveComponent(groupVm, 50, 30);
        canvas.EndDragComponent(groupVm);

        // Wait for routing to complete
        await canvas.RecalculateRoutesAsync();

        // Assert - Connection still exists and points are updated
        canvas.Connections.Count.ShouldBe(initialConnectionCount);
        var connection = canvas.Connections[0];
        connection.StartX.ShouldNotBe(200); // Pin should have moved
    }

    [Fact]
    public void EnterGroupEditMode_OnComponentGroup_SetsCurrentEditGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);

        var groupVm = canvas.AddComponent(group, "GroupTemplate");

        // Act - Enter edit mode
        canvas.EnterGroupEditMode(group);

        // Assert - Edit mode active
        canvas.IsInGroupEditMode.ShouldBeTrue();
        canvas.CurrentEditGroup.ShouldBe(group);
        canvas.BreadcrumbPath.Count.ShouldBe(1);
        canvas.BreadcrumbPath[0].ShouldBe(group);
    }

    [Fact]
    public void ExitGroupEditMode_ReturnsToRootLevel()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateTestComponent("Comp1", 100, 100);

        var group = new ComponentGroup("TestGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(comp1);

        canvas.AddComponent(group, "GroupTemplate");
        canvas.EnterGroupEditMode(group);

        // Act - Exit edit mode
        canvas.ExitGroupEditMode();

        // Assert - Back to root level
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.BreadcrumbPath.Count.ShouldBe(0);
    }

    /// <summary>
    /// Helper to create a simple test component.
    /// </summary>
    private Component CreateTestComponent(string identifier, double x, double y)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin>()
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
        return component;
    }

    /// <summary>
    /// Helper to create a component with physical pins for connection testing.
    /// </summary>
    private Component CreateComponentWithPins(string identifier, double x, double y)
    {
        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid sliderID, double value)>());
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "Pin1",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 15,
                AngleDegrees = 0
            }
        };

        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, sMatrix } },
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            pins
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };

        return component;
    }
}
