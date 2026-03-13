using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation.PowerFlow;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Visualization;

/// <summary>
/// Manages power flow visualization state. Calculates and caches power flow
/// results for display on the design canvas.
/// </summary>
public class PowerFlowVisualizer
{
    private readonly PowerFlowAnalyzer _analyzer = new();

    /// <summary>
    /// Whether power flow visualization is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// The most recent power flow analysis result.
    /// Null when no simulation data is available.
    /// </summary>
    public PowerFlowResult? CurrentResult { get; private set; }

    /// <summary>
    /// Threshold in dB below maximum for fading out connections.
    /// </summary>
    public double FadeThresholdDb
    {
        get => _analyzer.FadeThresholdDb;
        set => _analyzer.FadeThresholdDb = value;
    }

    /// <summary>
    /// Toggles the power flow visualization on or off.
    /// </summary>
    /// <returns>The new enabled state.</returns>
    public bool Toggle()
    {
        IsEnabled = !IsEnabled;
        return IsEnabled;
    }

    /// <summary>
    /// Updates the power flow data from simulation results.
    /// Includes both regular connections and frozen paths inside groups.
    /// </summary>
    /// <param name="connections">Current waveguide connections.</param>
    /// <param name="components">Current components (to extract frozen paths from groups).</param>
    /// <param name="fieldResults">Simulation field results mapping pin GUIDs to amplitudes.</param>
    public void UpdateFromSimulation(
        IReadOnlyList<WaveguideConnection> connections,
        IReadOnlyList<Component> components,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        var frozenPaths = CollectAllFrozenPaths(components);
        CurrentResult = _analyzer.Analyze(connections, frozenPaths, fieldResults);
    }

    /// <summary>
    /// Collects all frozen waveguide paths from component groups (including nested groups).
    /// </summary>
    private static List<FrozenWaveguidePath> CollectAllFrozenPaths(IReadOnlyList<Component> components)
    {
        var frozenPaths = new List<FrozenWaveguidePath>();

        foreach (var component in components)
        {
            if (component is ComponentGroup group)
            {
                CollectFrozenPathsRecursive(group, frozenPaths);
            }
        }

        return frozenPaths;
    }

    /// <summary>
    /// Recursively collects frozen paths from a group and its nested groups.
    /// </summary>
    private static void CollectFrozenPathsRecursive(ComponentGroup group, List<FrozenWaveguidePath> frozenPaths)
    {
        frozenPaths.AddRange(group.InternalPaths);

        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nestedGroup)
            {
                CollectFrozenPathsRecursive(nestedGroup, frozenPaths);
            }
        }
    }

    /// <summary>
    /// Gets the power flow for a specific connection.
    /// Returns null if no data is available.
    /// </summary>
    public ConnectionPowerFlow? GetFlowForConnection(Guid connectionId)
    {
        if (CurrentResult == null) return null;

        return CurrentResult.ConnectionFlows.TryGetValue(connectionId, out var flow)
            ? flow
            : null;
    }

    /// <summary>
    /// Clears the cached power flow result.
    /// </summary>
    public void Clear()
    {
        CurrentResult = null;
    }
}
