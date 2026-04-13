namespace CAP.Avalonia.Services;

/// <summary>
/// Provides AI-accessible operations for reading and manipulating the photonic circuit grid.
/// The AI acts as a high-level planner while this service handles execution and validation.
/// </summary>
public interface IAiGridService
{
    /// <summary>
    /// Returns a JSON summary of the current grid state: placed components, connections,
    /// and available component types.
    /// </summary>
    string GetGridState();

    /// <summary>
    /// Places a component of the given type at approximately (x, y) in micrometers.
    /// The service finds the nearest valid position automatically.
    /// Returns a status message with the assigned component ID and actual position.
    /// </summary>
    Task<string> PlaceComponentAsync(string componentType, double x, double y, int rotation = 0);

    /// <summary>
    /// Connects two components by automatically selecting compatible unconnected pins.
    /// Returns a status message describing the connection or an error.
    /// </summary>
    Task<string> CreateConnectionAsync(string fromComponentId, string toComponentId);

    /// <summary>
    /// Runs the S-Matrix simulation for the current canvas state.
    /// Returns a status message with the result.
    /// </summary>
    Task<string> RunSimulationAsync();

    /// <summary>
    /// Returns the current light propagation values (loss, path length) for all connections.
    /// </summary>
    string GetLightValues();

    /// <summary>
    /// Removes all components and connections from the grid.
    /// Returns a status message.
    /// </summary>
    string ClearGrid();

    /// <summary>
    /// Returns the list of available component type names from loaded PDKs.
    /// </summary>
    IReadOnlyList<string> GetAvailableComponentTypes();

    /// <summary>
    /// Groups the specified components into a ComponentGroup.
    /// Components must be specified by their identifiers.
    /// Returns a status message with the created group identifier.
    /// </summary>
    string CreateGroup(IReadOnlyList<string> componentIds, string? groupName = null);

    /// <summary>
    /// Ungroups a ComponentGroup, restoring individual components.
    /// Returns a status message listing the restored component identifiers.
    /// </summary>
    string UngroupComponent(string groupId);

    /// <summary>
    /// Saves a group as a reusable prefab/template in the component library.
    /// The group will appear in the library panel for future use.
    /// Returns a status message with the template name.
    /// </summary>
    string SaveGroupAsPrefab(string groupId, string prefabName, string? description = null);

    /// <summary>
    /// Duplicates a component or group and places the copy at the specified position.
    /// Preserves all internal structure, connections, and frozen paths for groups.
    /// Optionally applies a new rotation to the copy.
    /// Returns a status message with the new component's identifier.
    /// </summary>
    Task<string> CopyComponentAsync(string sourceId, double x, double y, int rotation = -1);
}
