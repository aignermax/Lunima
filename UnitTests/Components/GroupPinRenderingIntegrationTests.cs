using Avalonia;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Integration tests for group pin rendering and interaction.
/// Tests the complete flow from core logic to hit testing.
/// </summary>
public class GroupPinRenderingIntegrationTests
{
    /// <summary>
    /// Tests that unoccupied group pins can be hit-tested correctly.
    /// </summary>
    [Fact]
    public void HitTestGroupPin_WithUnoccupiedPin_ReturnsPin()
    {
        // Arrange
        var vm = CreateTestViewModel();
        var group = CreateGroupWithPins();
        vm.Components.Add(new ComponentViewModel(group));

        // Pin position: group at (100, 100), pin at relative (50, 0) = absolute (150, 100)
        var testPoint = new Point(150, 100);

        // Act
        var (hitPin, distance) = DesignCanvasHitTesting.HitTestGroupPin(
            testPoint,
            group,
            vm.Connections.Select(c => c.Connection));

        // Assert
        hitPin.ShouldNotBeNull();
        hitPin.Name.ShouldBe("external_east");
        distance.ShouldBeLessThan(15.0); // Within hit radius
    }

    /// <summary>
    /// Tests that occupied group pins are NOT returned by hit testing.
    /// </summary>
    [Fact]
    public void HitTestGroupPin_WithOccupiedPin_ReturnsNull()
    {
        // Arrange
        var vm = CreateTestViewModel();
        var group = CreateGroupWithPins();
        vm.Components.Add(new ComponentViewModel(group));

        // Create external component and connect to the pin
        var externalComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        externalComp.PhysicalX = 300;
        externalComp.PhysicalY = 100;
        vm.Components.Add(new ComponentViewModel(externalComp));

        var groupPin = group.ExternalPins.First();
        var connection = new WaveguideConnection
        {
            StartPin = groupPin.InternalPin,
            EndPin = externalComp.PhysicalPins.First()
        };
        vm.Connections.Add(new WaveguideConnectionViewModel(connection));

        // Pin position: group at (100, 100), pin at relative (50, 0) = absolute (150, 100)
        var testPoint = new Point(150, 100);

        // Act
        var (hitPin, distance) = DesignCanvasHitTesting.HitTestGroupPin(
            testPoint,
            group,
            vm.Connections.Select(c => c.Connection));

        // Assert
        hitPin.ShouldBeNull(); // Pin is occupied, should not be returned
    }

    /// <summary>
    /// Tests that GetUnoccupiedPins correctly filters occupied pins.
    /// </summary>
    [Fact]
    public void GetUnoccupiedPins_WithMixedOccupancy_ReturnsOnlyUnoccupied()
    {
        // Arrange
        var group = CreateGroupWithMultiplePins();
        var externalComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        externalComp.PhysicalX = 300;
        externalComp.PhysicalY = 100;

        // Connect to first pin (making it occupied)
        var connection = new WaveguideConnection
        {
            StartPin = group.ExternalPins[0].InternalPin,
            EndPin = externalComp.PhysicalPins.First()
        };

        var connections = new List<WaveguideConnection> { connection };

        // Act
        var unoccupiedPins = GroupPinOccupancyChecker.GetUnoccupiedPins(group, connections);

        // Assert
        unoccupiedPins.Count.ShouldBe(2); // 3 total - 1 occupied = 2 unoccupied
        unoccupiedPins.ShouldAllBe(pin => pin.Name != "external_east");
    }

    /// <summary>
    /// Tests that group pin absolute positions are calculated correctly relative to group.
    /// </summary>
    [Fact]
    public void GroupPinPosition_IsCorrectRelativeToGroup()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");
        group.PhysicalX = 100;
        group.PhysicalY = 200;

        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        group.AddChild(child);

        var groupPin = new GroupPin
        {
            Name = "pin",
            InternalPin = child.PhysicalPins.First(),
            RelativeX = 50,
            RelativeY = 75,
            AngleDegrees = 0
        };
        group.AddExternalPin(groupPin);

        // Act
        var (x, y) = GroupPinOccupancyChecker.GetAbsolutePosition(groupPin, group);

        // Assert
        x.ShouldBe(150.0);
        y.ShouldBe(275.0);
    }

    /// <summary>
    /// Tests that multiple unoccupied pins on the same group are all detected.
    /// </summary>
    [Fact]
    public void GetUnoccupiedPins_WithAllPinsUnoccupied_ReturnsAll()
    {
        // Arrange
        var group = CreateGroupWithMultiplePins();
        var connections = new List<WaveguideConnection>(); // No connections

        // Act
        var unoccupiedPins = GroupPinOccupancyChecker.GetUnoccupiedPins(group, connections);

        // Assert
        unoccupiedPins.Count.ShouldBe(3); // All 3 pins should be unoccupied
    }

    /// <summary>
    /// Tests that HitTestPin includes group pins in normal mode.
    /// </summary>
    [Fact]
    public void HitTestPin_InNormalMode_IncludesGroupPins()
    {
        // Arrange
        var vm = CreateTestViewModel();
        var group = CreateGroupWithPins();
        vm.Components.Add(new ComponentViewModel(group));

        // Pin position: group at (100, 100), pin at relative (50, 0) = absolute (150, 100)
        var testPoint = new Point(150, 100);

        // Act
        var hitPin = DesignCanvasHitTesting.HitTestPin(testPoint, vm);

        // Assert
        hitPin.ShouldNotBeNull();
        hitPin.ShouldBe(group.ExternalPins.First().InternalPin);
    }

    /// <summary>
    /// Creates a test DesignCanvasViewModel with minimal setup.
    /// </summary>
    private DesignCanvasViewModel CreateTestViewModel()
    {
        var vm = new DesignCanvasViewModel
        {
            ChipMinX = 0,
            ChipMinY = 0,
            ChipMaxX = 1000,
            ChipMaxY = 1000
        };
        return vm;
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
