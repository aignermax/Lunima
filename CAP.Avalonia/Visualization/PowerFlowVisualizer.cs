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
    /// When frozen path pins are absent from fieldResults (because they are internal to the
    /// group's S-Matrix), their amplitudes are estimated from the parent group's external pins.
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
        var enhancedFields = BuildEnhancedFieldResults(components, fieldResults);
        CurrentResult = _analyzer.Analyze(connections, frozenPaths, enhancedFields);
    }

    /// <summary>
    /// Builds an enhanced field results dictionary by adding fallback amplitude estimates
    /// for internal frozen path pins that are absent from the simulation results.
    /// These pins are hidden inside the group's S-Matrix and do not appear in fieldResults.
    /// The fallback uses the maximum amplitude found at the parent group's external pins.
    /// </summary>
    private static IReadOnlyDictionary<Guid, Complex> BuildEnhancedFieldResults(
        IReadOnlyList<Component> components,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        var enhanced = new Dictionary<Guid, Complex>(fieldResults);

        foreach (var component in components)
        {
            if (component is ComponentGroup group)
                AddGroupPinFallbacksRecursive(group, fieldResults, enhanced);
        }

        return enhanced;
    }

    /// <summary>
    /// Recursively adds fallback amplitude entries for each group's internal frozen path pins.
    /// Uses the maximum amplitude from the group's external pins as the fallback value.
    /// Only adds entries for GUIDs that are not already present in the enhanced dictionary.
    /// </summary>
    private static void AddGroupPinFallbacksRecursive(
        ComponentGroup group,
        IReadOnlyDictionary<Guid, Complex> originalFields,
        Dictionary<Guid, Complex> enhanced)
    {
        var maxAmplitude = FindMaxExternalPinAmplitude(group, originalFields);

        if (maxAmplitude != Complex.Zero)
        {
            foreach (var frozenPath in group.InternalPaths)
            {
                AddFallbackAmplitudeIfMissing(frozenPath.StartPin, maxAmplitude, enhanced);
                AddFallbackAmplitudeIfMissing(frozenPath.EndPin, maxAmplitude, enhanced);
            }
        }

        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nestedGroup)
                AddGroupPinFallbacksRecursive(nestedGroup, originalFields, enhanced);
        }
    }

    /// <summary>
    /// Finds the maximum signal amplitude among a group's external pins.
    /// Uses ExternalPins.InternalPin.LogicalPin to look up GUIDs in fieldResults,
    /// since these are the same GUIDs present in the simulation output.
    /// </summary>
    private static Complex FindMaxExternalPinAmplitude(
        ComponentGroup group,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        var maxAmplitude = Complex.Zero;

        foreach (var externalPin in group.ExternalPins)
        {
            var logicalPin = externalPin.InternalPin?.LogicalPin;
            if (logicalPin == null) continue;

            if (fieldResults.TryGetValue(logicalPin.IDOutFlow, out var outAmp) &&
                outAmp.Magnitude > maxAmplitude.Magnitude)
                maxAmplitude = outAmp;

            if (fieldResults.TryGetValue(logicalPin.IDInFlow, out var inAmp) &&
                inAmp.Magnitude > maxAmplitude.Magnitude)
                maxAmplitude = inAmp;
        }

        return maxAmplitude;
    }

    /// <summary>
    /// Adds fallback amplitude entries for both IDOutFlow and IDInFlow of a physical pin's
    /// logical pin, only if those GUIDs are not already present in the enhanced dictionary.
    /// </summary>
    private static void AddFallbackAmplitudeIfMissing(
        PhysicalPin? pin,
        Complex fallbackAmplitude,
        Dictionary<Guid, Complex> enhanced)
    {
        if (pin?.LogicalPin == null) return;

        enhanced.TryAdd(pin.LogicalPin.IDOutFlow, fallbackAmplitude);
        enhanced.TryAdd(pin.LogicalPin.IDInFlow, fallbackAmplitude);
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
