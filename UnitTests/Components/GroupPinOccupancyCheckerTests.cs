using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for GroupPinOccupancyChecker utility class.
/// Tests pin occupancy detection and position calculations.
/// </summary>
public class GroupPinOccupancyCheckerTests
{
    /// <summary>
    /// Tests that a GroupPin with no connections is reported as unoccupied.
    /// </summary>
    [Fact]
    public void IsOccupied_WithNoConnections_ReturnsFalse()
    {
        // Arrange
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var internalPin = component.PhysicalPins.First();
        var groupPin = new GroupPin
        {
            Name = "external_pin",
            InternalPin = internalPin,
            RelativeX = 10,
            RelativeY = 0,
            AngleDegrees = 0
        };

        var connections = new List<WaveguideConnection>();

        // Act
        bool isOccupied = GroupPinOccupancyChecker.IsOccupied(groupPin, connections);

        // Assert
        isOccupied.ShouldBeFalse();
    }

    /// <summary>
    /// Tests that a GroupPin with a connection on its InternalPin is reported as occupied.
    /// </summary>
    [Fact]
    public void IsOccupied_WithConnectionOnInternalPin_ReturnsTrue()
    {
        // Arrange
        var component1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var component2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component2.PhysicalX = 300;
        var internalPin = component1.PhysicalPins.First();
        var externalPin = component2.PhysicalPins.First();

        var groupPin = new GroupPin
        {
            Name = "external_pin",
            InternalPin = internalPin,
            RelativeX = 10,
            RelativeY = 0,
            AngleDegrees = 0
        };

        var connection = new WaveguideConnection
        {
            StartPin = internalPin,
            EndPin = externalPin
        };

        var connections = new List<WaveguideConnection> { connection };

        // Act
        bool isOccupied = GroupPinOccupancyChecker.IsOccupied(groupPin, connections);

        // Assert
        isOccupied.ShouldBeTrue();
    }

    /// <summary>
    /// Tests that GetUnoccupiedPins returns only pins without connections.
    /// </summary>
    [Fact]
    public void GetUnoccupiedPins_ReturnsOnlyUnoccupiedPins()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");
        var child1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var child2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        child2.PhysicalX = 300;
        group.AddChild(child1);
        group.AddChild(child2);

        var pin1 = child1.PhysicalPins.First();
        var pin2 = child2.PhysicalPins.First();

        var groupPin1 = new GroupPin
        {
            Name = "pin1",
            InternalPin = pin1,
            RelativeX = 10,
            RelativeY = 0,
            AngleDegrees = 0
        };

        var groupPin2 = new GroupPin
        {
            Name = "pin2",
            InternalPin = pin2,
            RelativeX = 110,
            RelativeY = 0,
            AngleDegrees = 0
        };

        group.AddExternalPin(groupPin1);
        group.AddExternalPin(groupPin2);

        // Create external component for connection
        var externalComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        externalComp.PhysicalX = 500;
        var externalPin = externalComp.PhysicalPins.First();

        // Connect pin1 (making it occupied)
        var connection = new WaveguideConnection
        {
            StartPin = pin1,
            EndPin = externalPin
        };

        var connections = new List<WaveguideConnection> { connection };

        // Act
        var unoccupiedPins = GroupPinOccupancyChecker.GetUnoccupiedPins(group, connections);

        // Assert
        unoccupiedPins.Count.ShouldBe(1);
        unoccupiedPins[0].Name.ShouldBe("pin2");
    }

    /// <summary>
    /// Tests that GetAbsolutePosition correctly calculates world coordinates.
    /// </summary>
    [Fact]
    public void GetAbsolutePosition_CalculatesCorrectWorldCoordinates()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");
        group.PhysicalX = 100;
        group.PhysicalY = 200;

        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        group.AddChild(child);

        var internalPin = child.PhysicalPins.First();
        var groupPin = new GroupPin
        {
            Name = "pin",
            InternalPin = internalPin,
            RelativeX = 50,
            RelativeY = 75,
            AngleDegrees = 0
        };

        // Act
        var (x, y) = GroupPinOccupancyChecker.GetAbsolutePosition(groupPin, group);

        // Assert
        x.ShouldBe(150.0); // 100 + 50
        y.ShouldBe(275.0); // 200 + 75
    }

    /// <summary>
    /// Tests that GetAbsoluteAngle normalizes angles to 0-360 range.
    /// </summary>
    [Fact]
    public void GetAbsoluteAngle_NormalizesAngleTo360Range()
    {
        // Arrange
        var child = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var internalPin = child.PhysicalPins.First();

        var groupPin = new GroupPin
        {
            Name = "pin",
            InternalPin = internalPin,
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 450 // Should normalize to 90
        };

        // Act
        double angle = GroupPinOccupancyChecker.GetAbsoluteAngle(groupPin);

        // Assert
        angle.ShouldBe(90.0);
    }

    /// <summary>
    /// Tests that GetUnoccupiedPins handles groups with no external pins.
    /// </summary>
    [Fact]
    public void GetUnoccupiedPins_WithNoExternalPins_ReturnsEmptyList()
    {
        // Arrange
        var group = new ComponentGroup("EmptyGroup");
        var connections = new List<WaveguideConnection>();

        // Act
        var unoccupiedPins = GroupPinOccupancyChecker.GetUnoccupiedPins(group, connections);

        // Assert
        unoccupiedPins.ShouldBeEmpty();
    }

    /// <summary>
    /// Tests that IsOccupied handles null GroupPin gracefully.
    /// </summary>
    [Fact]
    public void IsOccupied_WithNullGroupPin_ReturnsFalse()
    {
        // Arrange
        var connections = new List<WaveguideConnection>();

        // Act
        bool isOccupied = GroupPinOccupancyChecker.IsOccupied(null, connections);

        // Assert
        isOccupied.ShouldBeFalse();
    }

    /// <summary>
    /// Tests that GetAbsolutePosition handles null inputs gracefully.
    /// </summary>
    [Fact]
    public void GetAbsolutePosition_WithNullInputs_ReturnsZero()
    {
        // Act
        var (x, y) = GroupPinOccupancyChecker.GetAbsolutePosition(null, null);

        // Assert
        x.ShouldBe(0.0);
        y.ShouldBe(0.0);
    }
}
