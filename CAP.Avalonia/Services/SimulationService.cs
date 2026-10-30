using System.Numerics;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.ExternalPorts.LaserSpectrum;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Simulation;

namespace CAP.Avalonia.Services;

/// <summary>
/// Orchestrates S-Matrix light simulation from the Avalonia UI state.
/// Bridges the UI (DesignCanvasViewModel) with the core simulation engine
/// through detached physics snapshots using physical coordinates.
/// Supports per-source wavelength and power configuration.
/// </summary>
public class SimulationService
{
    /// <summary>Runs detached physics only; overridable for bounded scheduling/lifetime tests.</summary>
    protected virtual Task<SimulationSnapshotResult> SolveSnapshotAsync(
        SimulationInputSnapshot snapshot, CancellationToken cancellationToken) =>
        SimulationSnapshotSolver.RunAsync(snapshot, cancellationToken);

    /// <summary>
    /// Runs the full S-Matrix simulation and updates the PowerFlowVisualizer.
    /// Supports per-source wavelength/power via LaserConfig on each component.
    /// </summary>
    public async Task<SimulationResult> RunAsync(
        DesignCanvasViewModel canvas,
        CancellationToken cancellationToken = default,
        CAP_Contracts.ICommitPermission? permission = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, permission?.CancellationToken ?? default);
        CAP_Core.Documents.DocumentOperation captured = null!;
        void Capture() => captured = canvas.Document.CaptureOperation(linked.Token);
        if (permission == null) Capture(); else permission.Commit(Capture);
        await using var operation = captured;
        if (canvas.Components.Count == 0)
            return SimulationResult.Empty("No components placed");

        var componentViewModels = canvas.Components.ToList();
        var components = componentViewModels.Select(vm => vm.Component).ToList();
        var unresolved = ImportedModelReadiness.FindUnresolved(components);
        if (unresolved.Count > 0)
            return SimulationResult.Empty("Simulation unavailable: geometry-only imported components need models: "
                + string.Join(", ", unresolved));
        if (canvas.Connections.Count == 0 && !HasInternalPathsInGroups(canvas))
            return SimulationResult.Empty("No connections");

        // All editor objects are read synchronously under the caller's document ownership.
        // These lists remain on this orchestration path for guarded UI publication only.
        var connections = canvas.ConnectionManager.Connections.ToList();
        var componentCount = componentViewModels.Count;
        var connectionCount = canvas.Connections.Count;
        var partViewModels = canvas.Surface.IsActive ? canvas.Surface.Items : Array.Empty<ComponentViewModel>();
        var ports = new PhysicalExternalPortManager();
        var sourceConfigs = ConfigureLightSources(componentViewModels, ports,
            CollectWiredPins(componentViewModels, connections), partViewModels);
        if (sourceConfigs.Count == 0)
            return SimulationResult.Empty("No light sources found (place a Grating Coupler or Edge Coupler)");

        // ExternalInput stores power; the engine requires the corresponding complex FIELD.
        // Capture numeric connection physics and formula descriptions, never live factories.
        // Preserve the legacy first-wavelength diagnostic and spectral accumulation order;
        // ConcurrentBag enumeration of external inputs alone would reverse that order.
        var inputsByWavelength = ports.GetUsedExternalInputs().ToLookup(input => input.Input.LaserType.WaveLengthInNm);
        var sources = sourceConfigs.SelectMany(source => source.SampleWavelengthsNm).Distinct()
            .SelectMany(wavelength => inputsByWavelength[wavelength])
            .Select(input => new SimulationSourceInput(input.AttachedComponentPinId,
                input.Input.LaserType.WaveLengthInNm, input.Input.InFlowField)).ToArray();
        var snapshot = SimulationSnapshotCapture.Capture(components, connections, sources,
            canvas.ConnectionManager.EnableCoherentPropagationPhase, sourceConfigs.Any(source => source.HasSpectralLinewidth));
        cancellationToken.ThrowIfCancellationRequested();
        if (!operation.IsCurrent) return SimulationResult.Superseded();

        SimulationSnapshotResult computed;
        var workerCancellation = operation.CancellationToken;
        try
        {
            // Only detached input and the token cross this boundary. In particular no canvas,
            // component, source ViewModel, connection manager, or document lease enters it.
            computed = await RunDetachedAsync(snapshot, workerCancellation);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && !operation.IsCurrent)
        {
            return SimulationResult.Superseded();
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!operation.IsCurrent) return SimulationResult.Superseded();
        var fields = computed.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        // Linear-time endpoint analysis stays outside the lease lock. Complete flattened
        // fields must never trigger the legacy live group-matrix reconstruction here.
        var preparedOverlay = canvas.PowerFlowVisualizer.PrepareFromFlattenedSimulation(connections, components, fields);
        var published = false;
        void Publish() => published = operation.TryPublish(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                canvas.PowerFlowVisualizer.PublishPreparedResult(preparedOverlay);
                canvas.RefreshPowerFlowDisplay();
            });
        if (permission == null) Publish(); else permission.Commit(Publish);
        if (!published) return SimulationResult.Superseded();

        return new SimulationResult
        {
            Success = true,
            FieldResults = fields,
            WavelengthsUsed = sourceConfigs.Select(source => source.WavelengthNm).Distinct().ToList(),
            LightSourceCount = sourceConfigs.Count,
            ComponentCount = componentCount,
            ConnectionCount = connectionCount,
            SourceConfigs = sourceConfigs,
            SystemMatrix = computed.SystemMatrix
        };
    }

    // Keep worker closure creation in a method whose parameters contain no editor objects.
    private Task<SimulationSnapshotResult> RunDetachedAsync(SimulationInputSnapshot snapshot, CancellationToken token) =>
        Task.Run(() => SolveSnapshotAsync(snapshot, token), token);

    /// <summary>
    /// Finds I/O components and configures them as light sources.
    /// Uses per-source LaserConfig when available, otherwise defaults.
    /// Recursively searches inside ComponentGroups.
    /// </summary>
    internal List<SourceConfigInfo> ConfigureLightSources(
        DesignCanvasViewModel canvas,
        PhysicalExternalPortManager portManager)
    {
        var componentViewModels = canvas.Components.ToList();
        var wiredPins = CollectWiredPins(componentViewModels, canvas.ConnectionManager.Connections);
        return ConfigureLightSources(componentViewModels, portManager, wiredPins);
    }

    /// <summary>
    /// Collects every physical pin that has a waveguide connection — either a
    /// canvas-level connection or a frozen internal path inside a (nested) group.
    /// </summary>
    private static HashSet<PhysicalPin> CollectWiredPins(
        IReadOnlyList<ViewModels.Canvas.ComponentViewModel> componentViewModels,
        IReadOnlyList<WaveguideConnection> connections)
    {
        var wiredPins = new HashSet<PhysicalPin>();
        foreach (var connection in connections)
        {
            if (connection.StartPin != null) wiredPins.Add(connection.StartPin);
            if (connection.EndPin != null) wiredPins.Add(connection.EndPin);
        }
        foreach (var compVm in componentViewModels)
        {
            if (compVm.Component is ComponentGroup group)
                CollectGroupWiredPins(group, wiredPins);
        }
        return wiredPins;
    }

    private static void CollectGroupWiredPins(ComponentGroup group, HashSet<PhysicalPin> wiredPins)
    {
        foreach (var path in group.InternalPaths)
        {
            if (path.StartPin != null) wiredPins.Add(path.StartPin);
            if (path.EndPin != null) wiredPins.Add(path.EndPin);
        }
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup childGroup)
                CollectGroupWiredPins(childGroup, wiredPins);
        }
    }

    /// <summary>
    /// Picks the pins a light-source component injects on. Couplers with more
    /// than one optical pin (e.g. the two-pin SiEPIC grating coupler with a
    /// fiber-side and a waveguide-side pin) inject only on the off-chip pins —
    /// the optical pins without a waveguide connection. Injecting on the
    /// connected pin as well would feed power through that pin's reflection
    /// matrix entry on top of the real fiber-to-waveguide transfer and
    /// double-count the source. Single-pin couplers keep the legacy behaviour;
    /// a multi-pin coupler with every pin wired falls back to all pins so a
    /// source is never silently dropped.
    /// </summary>
    internal static List<PhysicalPin> SelectInjectionPins(
        IReadOnlyList<PhysicalPin> lightPins, ISet<PhysicalPin> wiredPins)
    {
        if (lightPins.Count <= 1)
            return lightPins.ToList();
        var offChipPins = lightPins.Where(p => !wiredPins.Contains(p)).ToList();
        return offChipPins.Count > 0 ? offChipPins : lightPins.ToList();
    }

    /// <summary>
    /// Captures source configuration on the owning UI thread before detached simulation.
    /// The supplied ViewModels and physical pins must never be passed to the worker.
    /// </summary>
    internal static List<SourceConfigInfo> ConfigureLightSources(
        IReadOnlyList<ViewModels.Canvas.ComponentViewModel> componentViewModels,
        PhysicalExternalPortManager portManager,
        ISet<PhysicalPin> wiredPins,
        IReadOnlyList<ViewModels.Canvas.ComponentViewModel>? partViewModels = null)
    {
        var configs = new List<SourceConfigInfo>();

        // Per-instance LaserConfig exists on top-level ViewModels and on the parts of a die
        // (edited in place); components inside other groups fall back to the default source.
        var laserConfigs = new Dictionary<Component, LaserConfig>();
        foreach (var compVm in componentViewModels.Concat(partViewModels ?? Array.Empty<ViewModels.Canvas.ComponentViewModel>()))
        {
            if (compVm.LaserConfig != null)
                laserConfigs[compVm.Component] = compVm.LaserConfig;
        }

        // Collect all components, including those inside groups (recursively)
        var allComponents = GetAllComponentsRecursively(componentViewModels);

        foreach (var component in allComponents)
        {
            // ONA Analyzer: its "source" pin emits in the regular simulation too,
            // so light flows visibly source → DUT → measurement (consistent with the
            // ONA sweep). Only the source pin emits; measurement stays a detector.
            var analyzerSource = GetAnalyzerLightSourcePin(component);
            if (analyzerSource != null)
            {
                int analyzerWavelengthNm = StandardWaveLengths.RedNM;
                portManager.AddLightSource(
                    new ExternalInput(
                        $"ona_{component.Identifier}_source",
                        GetLaserTypeForWavelength(analyzerWavelengthNm),
                        0,
                        new Complex(1.0, 0)),
                    analyzerSource.LogicalPin!.IDInFlow);
                configs.Add(new SourceConfigInfo(component.Identifier, analyzerWavelengthNm, 1.0));
                continue;
            }

            if (!IsLightSource(component))
                continue;

            // Couplers whose laser is switched off (#690) act as listen-only outputs.
            // The flag lives on the core component, so it also covers group children.
            if (!component.LaserEnabled)
                continue;

            // Use the per-instance LaserConfig when available; components inside
            // groups have no ViewModel, so they keep the default (ideal red) source.
            var config = laserConfigs.GetValueOrDefault(component);
            int wavelengthNm = config?.WavelengthNm ?? StandardWaveLengths.RedNM;
            double power = config?.InputPower ?? 1.0;
            var spectrum = config?.ToSpectrum() ?? new LaserSpectrumModel(wavelengthNm);
            var samples = spectrum.GetSamples();
            var sampleWavelengths = samples.Select(s => s.WavelengthNm).ToList();

            var lightPins = component.PhysicalPins
                .Where(p => p.LogicalPin?.MatterType == MatterType.Light)
                .ToList();

            foreach (var pin in SelectInjectionPins(lightPins, wiredPins))
            {
                foreach (var sample in samples)
                {
                    // The center sample keeps the legacy name; side samples are suffixed.
                    string name = sample.WavelengthNm == wavelengthNm
                        ? $"src_{component.Identifier}_{pin.Name}"
                        : $"src_{component.Identifier}_{pin.Name}_{sample.WavelengthNm}nm";
                    var input = new ExternalInput(
                        name,
                        GetLaserTypeForWavelength(sample.WavelengthNm),
                        0,
                        new Complex(power * sample.Weight, 0));
                    portManager.AddLightSource(input, pin.LogicalPin.IDInFlow);
                }

                configs.Add(new SourceConfigInfo(
                    component.Identifier, wavelengthNm, power, sampleWavelengths));
            }
        }

        return configs;
    }

    /// <summary>
    /// Recursively collects all Component instances, including those inside ComponentGroups.
    /// Returns the raw Component objects, not ViewModels, to avoid TemplateName issues.
    /// </summary>
    public static List<Component> GetAllComponentsRecursively(IEnumerable<ComponentViewModel> components)
    {
        var result = new List<Component>();

        foreach (var compVm in components)
        {
            // Add the component itself
            result.Add(compVm.Component);

            // If it's a group, recursively add its children
            if (compVm.Component is ComponentGroup group)
            {
                result.AddRange(GetAllComponentsFromGroup(group));
            }
        }

        return result;
    }

    /// <summary>
    /// Recursively extracts all components from a ComponentGroup.
    /// </summary>
    private static List<Component> GetAllComponentsFromGroup(ComponentGroup group)
    {
        var result = new List<Component>();

        foreach (var child in group.ChildComponents)
        {
            result.Add(child);

            if (child is ComponentGroup childGroup)
            {
                result.AddRange(GetAllComponentsFromGroup(childGroup));
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if any ComponentGroups in the canvas have internal paths (connections).
    /// When all components are grouped, canvas.Connections is empty but groups have InternalPaths.
    /// </summary>
    private static bool HasInternalPathsInGroups(DesignCanvasViewModel canvas)
    {
        foreach (var compVm in canvas.Components)
        {
            if (compVm.Component is ComponentGroup group)
            {
                if (group.InternalPaths.Count > 0)
                    return true;

                // Recursively check nested groups
                if (HasInternalPathsInGroupRecursive(group))
                    return true;
            }
        }
        return false;
    }

    private static bool HasInternalPathsInGroupRecursive(ComponentGroup group)
    {
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nestedGroup)
            {
                if (nestedGroup.InternalPaths.Count > 0)
                    return true;
                if (HasInternalPathsInGroupRecursive(nestedGroup))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the ONA Analyzer's "source" pin when <paramref name="component"/> is an
    /// analysis tool with a usable light source pin; otherwise null. The regular
    /// simulation injects light here so the analyzer behaves like its sweep mode:
    /// light flows source → DUT → measurement. The measurement pin stays a detector.
    /// </summary>
    public static PhysicalPin? GetAnalyzerLightSourcePin(Component component)
    {
        if (!component.IsAnalysisTool)
            return null;

        var sourcePin = component.PhysicalPins.FirstOrDefault(
            p => string.Equals(p.Name, "source", StringComparison.OrdinalIgnoreCase));

        return sourcePin?.LogicalPin?.MatterType == MatterType.Light ? sourcePin : null;
    }

    public static bool IsLightSource(Component component) =>
        LightSourceClassifier.IsLightInjectingCoupler(component)
        || component.IsUserMarkedLightSource;

    internal static LaserType GetLaserTypeForWavelength(int wavelengthNm)
    {
        if (wavelengthNm == StandardWaveLengths.RedNM) return LaserType.Red;
        if (wavelengthNm == StandardWaveLengths.GreenNM) return LaserType.Green;
        if (wavelengthNm == StandardWaveLengths.BlueNM) return LaserType.Blue;
        // Spectral samples (#819) sit at arbitrary wavelengths; hue follows the
        // nearest standard wavelength.
        return new LaserType(NearestStandardColor(wavelengthNm), wavelengthNm);
    }

    private static LightColor NearestStandardColor(int wavelengthNm)
    {
        int redDist = Math.Abs(wavelengthNm - StandardWaveLengths.RedNM);
        int greenDist = Math.Abs(wavelengthNm - StandardWaveLengths.GreenNM);
        int blueDist = Math.Abs(wavelengthNm - StandardWaveLengths.BlueNM);
        if (redDist <= greenDist && redDist <= blueDist) return LightColor.Red;
        return greenDist <= blueDist ? LightColor.Green : LightColor.Blue;
    }


}

public class SimulationResult
{
    public bool Success { get; set; }
    public bool IsSuperseded { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<Guid, Complex>? FieldResults { get; set; }
    public List<int> WavelengthsUsed { get; set; } = new();
    public int LightSourceCount { get; set; }
    public int ComponentCount { get; set; }
    public int ConnectionCount { get; set; }
    public List<SourceConfigInfo> SourceConfigs { get; set; } = new();
    public SMatrix? SystemMatrix { get; set; }

    public static SimulationResult Empty(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };

    public static SimulationResult Superseded() => new()
    {
        IsSuperseded = true,
        ErrorMessage = "Simulation superseded by a document change"
    };

    public string WavelengthSummary =>
        WavelengthsUsed.Count > 0
            ? string.Join(", ", WavelengthsUsed.Select(w => $"{w}nm"))
            : "none";
}
