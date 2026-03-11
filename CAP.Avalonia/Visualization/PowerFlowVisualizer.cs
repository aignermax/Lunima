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
    /// </summary>
    /// <param name="connections">Current waveguide connections.</param>
    /// <param name="fieldResults">Simulation field results mapping pin GUIDs to amplitudes.</param>
    public void UpdateFromSimulation(
        IReadOnlyList<WaveguideConnection> connections,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        CurrentResult = _analyzer.Analyze(connections, fieldResults);
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
