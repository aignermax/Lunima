using System.Numerics;

namespace CAP.Avalonia.Services;

/// <summary>
/// Result of an S-Matrix light simulation run.
/// </summary>
public class SimulationResult
{
    /// <summary>
    /// Whether the simulation completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message when simulation fails.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Per-pin field amplitudes from the simulation.
    /// </summary>
    public Dictionary<Guid, Complex>? FieldResults { get; set; }

    /// <summary>
    /// Wavelengths used in this simulation run.
    /// </summary>
    public List<int> WavelengthsUsed { get; set; } = new();

    /// <summary>
    /// Number of light sources in the simulation.
    /// </summary>
    public int LightSourceCount { get; set; }

    /// <summary>
    /// Number of components in the simulation.
    /// </summary>
    public int ComponentCount { get; set; }

    /// <summary>
    /// Number of connections in the simulation.
    /// </summary>
    public int ConnectionCount { get; set; }

    /// <summary>
    /// Per-source configuration details used in the simulation.
    /// </summary>
    public List<SourceConfigInfo> SourceConfigs { get; set; } = new();

    /// <summary>
    /// Creates an empty (failed) result with an error message.
    /// </summary>
    public static SimulationResult Empty(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };

    /// <summary>
    /// Summary of wavelengths used (e.g. "1550nm" or "1550nm, 1310nm").
    /// </summary>
    public string WavelengthSummary =>
        WavelengthsUsed.Count > 0
            ? string.Join(", ", WavelengthsUsed.Select(w => $"{w}nm"))
            : "none";
}
