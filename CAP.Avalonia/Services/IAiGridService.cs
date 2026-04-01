namespace CAP.Avalonia.Services;

/// <summary>
/// Contract for AI-driven grid manipulation operations.
/// Provides high-level commands for the AI to read and modify the photonic circuit design.
/// The AI acts as a circuit architect (decides WHAT to place and WHERE),
/// while the application handles snapping, collision avoidance, and routing.
/// </summary>
public interface IAiGridService
{
    /// <summary>
    /// Returns a JSON summary of the current grid state: components, connections, and simulation status.
    /// </summary>
    string GetGridState();

    /// <summary>
    /// Places a named component type on the grid at approximately the given position (micrometers).
    /// Returns the component ID on success, or an error message prefixed with "Error:".
    /// </summary>
    /// <param name="componentType">PDK component type name (e.g. "Grating Coupler").</param>
    /// <param name="x">Approximate X position in micrometers.</param>
    /// <param name="y">Approximate Y position in micrometers.</param>
    /// <param name="rotation">Rotation in degrees (0, 90, 180, or 270).</param>
    string PlaceComponent(string componentType, double x, double y, int rotation = 0);

    /// <summary>
    /// Creates a waveguide connection between two components.
    /// Automatically selects the first available unconnected pin on each component.
    /// Returns a success message or an error message prefixed with "Error:".
    /// </summary>
    /// <param name="fromComponentId">ID of the source component.</param>
    /// <param name="toComponentId">ID of the target component.</param>
    string CreateConnection(string fromComponentId, string toComponentId);

    /// <summary>
    /// Removes all components and connections from the design grid.
    /// Returns a summary of how many components were removed.
    /// </summary>
    string ClearGrid();

    /// <summary>
    /// Runs the photonic S-Matrix simulation and activates the power flow overlay.
    /// Returns simulation results summary or an error message.
    /// </summary>
    Task<string> StartSimulationAsync();

    /// <summary>
    /// Stops the running simulation and hides the power flow overlay.
    /// </summary>
    string StopSimulation();

    /// <summary>
    /// Returns current light power values for all waveguide connections as JSON.
    /// Requires a simulation to have been run first.
    /// </summary>
    string GetLightValues();

    /// <summary>
    /// Returns all available component type names loaded from the active PDK.
    /// </summary>
    IReadOnlyList<string> GetAvailableComponentTypes();
}
