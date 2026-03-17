using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for GroupPinViewModel.
/// Tests that GroupPins are correctly added to AllPins and support highlighting.
/// </summary>
public class GroupPinViewModelTests
{
    /// <summary>
    /// Tests that adding a ComponentGroup to the canvas adds its GroupPins to AllPins.
    /// </summary>
    [Fact]
    public void AddComponent_WithComponentGroup_AddsGroupPinsToAllPins()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var group = CreateGroupWithPins();

        // Act
        vm.AddComponent(group);

        // Assert
        // ComponentGroup has no PhysicalPins on the group itself, only ExternalPins
        vm.AllPins.Count.ShouldBe(1); // 1 group pin

        // Verify the group pin is included
        var groupPinVm = vm.AllPins.OfType<GroupPinViewModel>().FirstOrDefault();
        groupPinVm.ShouldNotBeNull();
        groupPinVm.Name.ShouldBe("external_east");
    }

    /// <summary>
    /// Tests that GroupPinViewModel calculates correct absolute positions.
    /// </summary>
    [Fact]
    public void GroupPinViewModel_CalculatesCorrectAbsolutePosition()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");
        group.PhysicalX = 100;
        group.PhysicalY = 200;

        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        group.AddChild(child);

        var groupPin = new GroupPin
        {
            Name = "test_pin",
            InternalPin = child.PhysicalPins.First(),
            RelativeX = 50,
            RelativeY = 75,
            AngleDegrees = 0
        };
        group.AddExternalPin(groupPin);

        var componentVm = new ComponentViewModel(group);
        var pinVm = new GroupPinViewModel(groupPin, group, componentVm);

        // Act & Assert
        pinVm.X.ShouldBe(150.0); // 100 + 50
        pinVm.Y.ShouldBe(275.0); // 200 + 75
    }

    /// <summary>
    /// Tests that UpdatePinHighlight correctly highlights GroupPins.
    /// </summary>
    [Fact]
    public void UpdatePinHighlight_WithGroupPin_HighlightsCorrectly()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var group = CreateGroupWithPins();
        vm.AddComponent(group);

        // Pin position: group at (100, 100), pin at relative (50, 0) = absolute (150, 100)
        double testX = 150;
        double testY = 100;

        // Act
        var highlightedPin = vm.UpdatePinHighlight(testX, testY);

        // Assert
        highlightedPin.ShouldNotBeNull();
        highlightedPin.ShouldBeOfType<GroupPinViewModel>();
        highlightedPin.IsHighlighted.ShouldBeTrue();
        highlightedPin.Name.ShouldBe("external_east");
    }

    /// <summary>
    /// Tests that UpdatePinHighlight selects the nearest pin (GroupPin vs PhysicalPin).
    /// </summary>
    [Fact]
    public void UpdatePinHighlight_SelectsNearestPin()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();

        // Add a regular component
        var regularComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        regularComp.PhysicalX = 200;
        regularComp.PhysicalY = 100;
        vm.AddComponent(regularComp);

        // Add a group with a pin
        var group = CreateGroupWithPins();
        vm.AddComponent(group);

        // Test point closer to the GroupPin (150, 100) than to the regular component (200, 100)
        double testX = 155;
        double testY = 100;

        // Act
        var highlightedPin = vm.UpdatePinHighlight(testX, testY);

        // Assert
        highlightedPin.ShouldNotBeNull();
        highlightedPin.ShouldBeOfType<GroupPinViewModel>();
        highlightedPin.Name.ShouldBe("external_east");
    }

    /// <summary>
    /// Tests that removing a ComponentGroup also removes its GroupPins from AllPins.
    /// </summary>
    [Fact]
    public void RemoveComponent_WithComponentGroup_RemovesGroupPinsFromAllPins()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var group = CreateGroupWithPins();
        var componentVm = vm.AddComponent(group);

        // Verify pins were added
        vm.AllPins.Count.ShouldBe(1); // 1 group pin

        // Act
        vm.RemoveComponent(componentVm);

        // Assert
        vm.AllPins.Count.ShouldBe(0);
    }

    /// <summary>
    /// Tests that GroupPinViewModel provides the correct InternalPin for connections.
    /// </summary>
    [Fact]
    public void GroupPinViewModel_ExposesCorrectInternalPin()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");
        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        group.AddChild(child);

        var internalPin = child.PhysicalPins.First();
        var groupPin = new GroupPin
        {
            Name = "external",
            InternalPin = internalPin,
            RelativeX = 50,
            RelativeY = 0,
            AngleDegrees = 0
        };
        group.AddExternalPin(groupPin);

        var componentVm = new ComponentViewModel(group);
        var pinVm = new GroupPinViewModel(groupPin, group, componentVm);

        // Act & Assert
        pinVm.Pin.ShouldBe(internalPin);
    }

    /// <summary>
    /// Tests that GroupPinViewModel implements IPinViewModel interface correctly.
    /// </summary>
    [Fact]
    public void GroupPinViewModel_ImplementsIPinViewModelInterface()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");
        group.PhysicalX = 100;
        group.PhysicalY = 100;

        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        group.AddChild(child);

        var groupPin = new GroupPin
        {
            Name = "external",
            InternalPin = child.PhysicalPins.First(),
            RelativeX = 50,
            RelativeY = 0,
            AngleDegrees = 45
        };
        group.AddExternalPin(groupPin);

        var componentVm = new ComponentViewModel(group);

        // Act
        IPinViewModel pinVm = new GroupPinViewModel(groupPin, group, componentVm);

        // Assert
        pinVm.X.ShouldBe(150.0);
        pinVm.Y.ShouldBe(100.0);
        pinVm.Angle.ShouldBe(45.0);
        pinVm.Name.ShouldBe("external");
        pinVm.Pin.ShouldBe(child.PhysicalPins.First());
    }

    /// <summary>
    /// Tests that SetHighlighted updates IsHighlighted and Scale properties.
    /// </summary>
    [Fact]
    public void SetHighlighted_UpdatesPropertiesCorrectly()
    {
        // Arrange
        var group = CreateGroupWithPins();
        var groupPin = group.ExternalPins.First();
        var componentVm = new ComponentViewModel(group);
        var pinVm = new GroupPinViewModel(groupPin, group, componentVm);

        // Act
        pinVm.SetHighlighted(true);

        // Assert
        pinVm.IsHighlighted.ShouldBeTrue();
        pinVm.Scale.ShouldBe(1.5);

        // Act
        pinVm.SetHighlighted(false);

        // Assert
        pinVm.IsHighlighted.ShouldBeFalse();
        pinVm.Scale.ShouldBe(1.0);
    }

    /// <summary>
    /// Tests that a ComponentGroup with multiple external pins adds all to AllPins.
    /// </summary>
    [Fact]
    public void AddComponent_WithMultipleGroupPins_AddsAllToAllPins()
    {
        // Arrange
        var vm = new DesignCanvasViewModel();
        var group = CreateGroupWithMultiplePins();

        // Act
        vm.AddComponent(group);

        // Assert
        // ComponentGroup has no PhysicalPins on the group itself, only 3 GroupPins
        vm.AllPins.Count.ShouldBe(3);

        var groupPinVms = vm.AllPins.OfType<GroupPinViewModel>().ToList();
        groupPinVms.Count.ShouldBe(3);

        groupPinVms.ShouldContain(p => p.Name == "external_east");
        groupPinVms.ShouldContain(p => p.Name == "external_west");
        groupPinVms.ShouldContain(p => p.Name == "external_north");
    }

    /// <summary>
    /// Creates a ComponentGroup with a single external pin for testing.
    /// </summary>
    private ComponentGroup CreateGroupWithPins()
    {
        var group = new ComponentGroup("TestGroup");
        group.PhysicalX = 100;
        group.PhysicalY = 100;

        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        group.AddChild(child);

        var groupPin = new GroupPin
        {
            Name = "external_east",
            InternalPin = child.PhysicalPins.First(),
            RelativeX = 50,
            RelativeY = 0,
            AngleDegrees = 0
        };
        group.AddExternalPin(groupPin);

        return group;
    }

    /// <summary>
    /// Creates a ComponentGroup with multiple external pins for testing.
    /// </summary>
    private ComponentGroup CreateGroupWithMultiplePins()
    {
        var group = new ComponentGroup("TestGroup");
        group.PhysicalX = 100;
        group.PhysicalY = 100;

        // Add child components
        var child1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var child2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        child2.PhysicalX = 50;
        var child3 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        child3.PhysicalX = 100;
        group.AddChild(child1);
        group.AddChild(child2);
        group.AddChild(child3);

        // Add external pins
        var pin1 = new GroupPin
        {
            Name = "external_east",
            InternalPin = child1.PhysicalPins.First(),
            RelativeX = 50,
            RelativeY = 0,
            AngleDegrees = 0
        };

        var pin2 = new GroupPin
        {
            Name = "external_west",
            InternalPin = child2.PhysicalPins.First(),
            RelativeX = -50,
            RelativeY = 0,
            AngleDegrees = 180
        };

        var pin3 = new GroupPin
        {
            Name = "external_north",
            InternalPin = child3.PhysicalPins.First(),
            RelativeX = 0,
            RelativeY = -50,
            AngleDegrees = 90
        };

        group.AddExternalPin(pin1);
        group.AddExternalPin(pin2);
        group.AddExternalPin(pin3);

        return group;
    }
}
