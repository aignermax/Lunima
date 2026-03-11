using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Grid;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Grid;

/// <summary>
/// Unit tests for the LockManager class.
/// </summary>
public class LockManagerTests
{
    private readonly LockManager _lockManager;

    public LockManagerTests()
    {
        _lockManager = new LockManager();
    }

    [Fact]
    public void LockComponent_SetsIsLockedToTrue()
    {
        // Arrange
        var component = TestComponentFactory.CreateBasicComponent();
        component.IsLocked.ShouldBeFalse();

        // Act
        _lockManager.LockComponent(component);

        // Assert
        component.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void UnlockComponent_SetsIsLockedToFalse()
    {
        // Arrange
        var component = TestComponentFactory.CreateBasicComponent();
        component.IsLocked = true;

        // Act
        _lockManager.UnlockComponent(component);

        // Assert
        component.IsLocked.ShouldBeFalse();
    }

    [Fact]
    public void ToggleComponentLock_TogglesLockState()
    {
        // Arrange
        var component = TestComponentFactory.CreateBasicComponent();
        var initialState = component.IsLocked;

        // Act
        _lockManager.ToggleComponentLock(component);

        // Assert
        component.IsLocked.ShouldBe(!initialState);

        // Act again
        _lockManager.ToggleComponentLock(component);

        // Assert back to initial
        component.IsLocked.ShouldBe(initialState);
    }

    [Fact]
    public void LockComponents_LocksMultipleComponents()
    {
        // Arrange
        var components = new[]
        {
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent()
        };

        // Act
        _lockManager.LockComponents(components);

        // Assert
        foreach (var component in components)
        {
            component.IsLocked.ShouldBeTrue();
        }
    }

    [Fact]
    public void UnlockComponents_UnlocksMultipleComponents()
    {
        // Arrange
        var components = new[]
        {
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent()
        };

        foreach (var component in components)
        {
            component.IsLocked = true;
        }

        // Act
        _lockManager.UnlockComponents(components);

        // Assert
        foreach (var component in components)
        {
            component.IsLocked.ShouldBeFalse();
        }
    }

    [Fact]
    public void LockConnection_SetsIsLockedToTrue()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        connection.IsLocked.ShouldBeFalse();

        // Act
        _lockManager.LockConnection(connection);

        // Assert
        connection.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void UnlockConnection_SetsIsLockedToFalse()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        connection.IsLocked = true;

        // Act
        _lockManager.UnlockConnection(connection);

        // Assert
        connection.IsLocked.ShouldBeFalse();
    }

    [Fact]
    public void ToggleConnectionLock_TogglesLockState()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        var initialState = connection.IsLocked;

        // Act
        _lockManager.ToggleConnectionLock(connection);

        // Assert
        connection.IsLocked.ShouldBe(!initialState);

        // Act again
        _lockManager.ToggleConnectionLock(connection);

        // Assert back to initial
        connection.IsLocked.ShouldBe(initialState);
    }

    [Fact]
    public void IsComponentLocked_ReturnsCorrectState()
    {
        // Arrange
        var component = TestComponentFactory.CreateBasicComponent();

        // Act & Assert - unlocked
        _lockManager.IsComponentLocked(component).ShouldBeFalse();

        // Lock and check
        component.IsLocked = true;
        _lockManager.IsComponentLocked(component).ShouldBeTrue();
    }

    [Fact]
    public void IsConnectionLocked_ReturnsCorrectState()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var connection = TestComponentFactory.CreateConnection(comp1, comp2);

        // Act & Assert - unlocked
        _lockManager.IsConnectionLocked(connection).ShouldBeFalse();

        // Lock and check
        connection.IsLocked = true;
        _lockManager.IsConnectionLocked(connection).ShouldBeTrue();
    }

    [Fact]
    public void GetLockedComponents_ReturnsOnlyLockedComponents()
    {
        // Arrange
        var components = new[]
        {
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent()
        };

        components[0].IsLocked = true;
        components[2].IsLocked = true;

        // Act
        var lockedComponents = _lockManager.GetLockedComponents(components).ToList();

        // Assert
        lockedComponents.Count.ShouldBe(2);
        lockedComponents.ShouldContain(components[0]);
        lockedComponents.ShouldContain(components[2]);
    }

    [Fact]
    public void GetUnlockedComponents_ReturnsOnlyUnlockedComponents()
    {
        // Arrange
        var components = new[]
        {
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent(),
            TestComponentFactory.CreateBasicComponent()
        };

        components[1].IsLocked = true;
        components[3].IsLocked = true;

        // Act
        var unlockedComponents = _lockManager.GetUnlockedComponents(components).ToList();

        // Assert
        unlockedComponents.Count.ShouldBe(2);
        unlockedComponents.ShouldContain(components[0]);
        unlockedComponents.ShouldContain(components[2]);
    }

    [Fact]
    public void LockComponent_ThrowsArgumentNullException_WhenComponentIsNull()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => _lockManager.LockComponent(null!));
    }

    [Fact]
    public void UnlockComponent_ThrowsArgumentNullException_WhenComponentIsNull()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => _lockManager.UnlockComponent(null!));
    }

    [Fact]
    public void LockConnection_ThrowsArgumentNullException_WhenConnectionIsNull()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => _lockManager.LockConnection(null!));
    }

    [Fact]
    public void UnlockConnection_ThrowsArgumentNullException_WhenConnectionIsNull()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => _lockManager.UnlockConnection(null!));
    }
}
