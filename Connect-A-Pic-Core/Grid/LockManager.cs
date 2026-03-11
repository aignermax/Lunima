using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.Grid;

/// <summary>
/// Manages lock state of components and connections.
/// Provides methods to lock/unlock elements and check lock status.
/// </summary>
public class LockManager
{
    /// <summary>
    /// Locks a component, preventing it from being moved, rotated, or deleted.
    /// </summary>
    /// <param name="component">The component to lock.</param>
    public void LockComponent(Component component)
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));

        component.IsLocked = true;
    }

    /// <summary>
    /// Unlocks a component, allowing it to be moved, rotated, or deleted.
    /// </summary>
    /// <param name="component">The component to unlock.</param>
    public void UnlockComponent(Component component)
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));

        component.IsLocked = false;
    }

    /// <summary>
    /// Toggles the lock state of a component.
    /// </summary>
    /// <param name="component">The component to toggle.</param>
    public void ToggleComponentLock(Component component)
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));

        component.IsLocked = !component.IsLocked;
    }

    /// <summary>
    /// Locks multiple components.
    /// </summary>
    /// <param name="components">The components to lock.</param>
    public void LockComponents(IEnumerable<Component> components)
    {
        if (components == null)
            throw new ArgumentNullException(nameof(components));

        foreach (var component in components)
        {
            component.IsLocked = true;
        }
    }

    /// <summary>
    /// Unlocks multiple components.
    /// </summary>
    /// <param name="components">The components to unlock.</param>
    public void UnlockComponents(IEnumerable<Component> components)
    {
        if (components == null)
            throw new ArgumentNullException(nameof(components));

        foreach (var component in components)
        {
            component.IsLocked = false;
        }
    }

    /// <summary>
    /// Locks a waveguide connection, preventing it from being deleted or modified.
    /// </summary>
    /// <param name="connection">The connection to lock.</param>
    public void LockConnection(WaveguideConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        connection.IsLocked = true;
    }

    /// <summary>
    /// Unlocks a waveguide connection, allowing it to be deleted or modified.
    /// </summary>
    /// <param name="connection">The connection to unlock.</param>
    public void UnlockConnection(WaveguideConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        connection.IsLocked = false;
    }

    /// <summary>
    /// Toggles the lock state of a connection.
    /// </summary>
    /// <param name="connection">The connection to toggle.</param>
    public void ToggleConnectionLock(WaveguideConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        connection.IsLocked = !connection.IsLocked;
    }

    /// <summary>
    /// Locks multiple connections.
    /// </summary>
    /// <param name="connections">The connections to lock.</param>
    public void LockConnections(IEnumerable<WaveguideConnection> connections)
    {
        if (connections == null)
            throw new ArgumentNullException(nameof(connections));

        foreach (var connection in connections)
        {
            connection.IsLocked = true;
        }
    }

    /// <summary>
    /// Unlocks multiple connections.
    /// </summary>
    /// <param name="connections">The connections to unlock.</param>
    public void UnlockConnections(IEnumerable<WaveguideConnection> connections)
    {
        if (connections == null)
            throw new ArgumentNullException(nameof(connections));

        foreach (var connection in connections)
        {
            connection.IsLocked = false;
        }
    }

    /// <summary>
    /// Checks if a component is locked.
    /// </summary>
    /// <param name="component">The component to check.</param>
    /// <returns>True if the component is locked, false otherwise.</returns>
    public bool IsComponentLocked(Component component)
    {
        return component?.IsLocked ?? false;
    }

    /// <summary>
    /// Checks if a connection is locked.
    /// </summary>
    /// <param name="connection">The connection to check.</param>
    /// <returns>True if the connection is locked, false otherwise.</returns>
    public bool IsConnectionLocked(WaveguideConnection connection)
    {
        return connection?.IsLocked ?? false;
    }

    /// <summary>
    /// Gets all locked components from a collection.
    /// </summary>
    /// <param name="components">The components to filter.</param>
    /// <returns>Locked components.</returns>
    public IEnumerable<Component> GetLockedComponents(IEnumerable<Component> components)
    {
        if (components == null)
            throw new ArgumentNullException(nameof(components));

        return components.Where(c => c.IsLocked);
    }

    /// <summary>
    /// Gets all unlocked components from a collection.
    /// </summary>
    /// <param name="components">The components to filter.</param>
    /// <returns>Unlocked components.</returns>
    public IEnumerable<Component> GetUnlockedComponents(IEnumerable<Component> components)
    {
        if (components == null)
            throw new ArgumentNullException(nameof(components));

        return components.Where(c => !c.IsLocked);
    }
}
