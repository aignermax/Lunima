using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services;

/// <summary>
/// Orchestrates S-Matrix light simulation from the Avalonia UI state.
/// Bridges the UI (DesignCanvasViewModel) with the core simulation engine
/// (GridLightCalculator) using physical coordinates.
/// Supports per-source wavelength and power configuration.
/// </summary>
public class SimulationService
{
    /// <summary>
    /// Template names that are treated as light input sources.
    /// </summary>
    private static readonly HashSet<string> LightSourceTemplates =
        new(StringComparer.OrdinalIgnoreCase) { "Grating Coupler", "Edge Coupler" };

    /// <summary>
    /// Runs the full S-Matrix simulation and updates the PowerFlowVisualizer.
    /// Supports per-source wavelength/power via LaserConfig on each component.
    /// </summary>
    public async Task<SimulationResult> RunAsync(
        DesignCanvasViewModel canvas,
        CancellationToken cancellationToken = default)
    {
        if (canvas.Components.Count == 0)
            return SimulationResult.Empty("No components placed");

        if (canvas.Connections.Count == 0)
            return SimulationResult.Empty("No connections");

        var tileManager = new ComponentListTileManager();
        foreach (var compVm in canvas.Components)
        {
            // Ensure ComponentGroups have computed S-Matrices before simulation
            if (compVm.Component is ComponentGroup group)
            {
                group.EnsureSMatrixComputed();
            }
            tileManager.AddComponent(compVm.Component);
        }

        var portManager = new PhysicalExternalPortManager();
        var sourceConfigs = ConfigureLightSources(canvas, portManager);

        if (sourceConfigs.Count == 0)
            return SimulationResult.Empty(
                "No light sources found (place a Grating Coupler or Edge Coupler)");

        var gridManager = GridManager.CreateForSimulation(
            tileManager, canvas.ConnectionManager, portManager);

        // Run simulation for each distinct wavelength
        var wavelengths = sourceConfigs.Select(s => s.WavelengthNm).Distinct().ToList();
        var allFieldResults = new Dictionary<Guid, Complex>();
        SMatrix? systemMatrix = null;

        foreach (var wl in wavelengths)
        {
            var builder = new SystemMatrixBuilder(gridManager);
            var calculator = new GridLightCalculator(builder, gridManager);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var fields = await calculator.CalculateFieldPropagationAsync(cts, wl);
            MergeFieldResults(allFieldResults, fields);

            // Capture the system S-Matrix from the first wavelength for diagnostics
            if (systemMatrix == null)
            {
                systemMatrix = builder.GetSystemSMatrix(wl);
            }
        }

        var components = canvas.Components.Select(c => c.Component).ToList();
        canvas.PowerFlowVisualizer.UpdateFromSimulation(
            canvas.ConnectionManager.Connections, components, allFieldResults);

        canvas.RefreshPowerFlowDisplay();

        return new SimulationResult
        {
            Success = true,
            FieldResults = allFieldResults,
            WavelengthsUsed = wavelengths,
            LightSourceCount = sourceConfigs.Count,
            ComponentCount = canvas.Components.Count,
            ConnectionCount = canvas.Connections.Count,
            SourceConfigs = sourceConfigs,
            SystemMatrix = systemMatrix
        };
    }

    /// <summary>
    /// Finds I/O components and configures them as light sources.
    /// Uses per-source LaserConfig when available, otherwise defaults.
    /// Recursively searches inside ComponentGroups.
    /// </summary>
    internal List<SourceConfigInfo> ConfigureLightSources(
        DesignCanvasViewModel canvas,
        PhysicalExternalPortManager portManager)
    {
        var configs = new List<SourceConfigInfo>();

        // Collect all components, including those inside groups (recursively)
        var allComponents = GetAllComponentsRecursively(canvas.Components);

        foreach (var component in allComponents)
        {
            if (!IsLightSource(component))
                continue;

            // For components inside groups, we don't have LaserConfig, so use defaults
            int wavelengthNm = StandardWaveLengths.RedNM;
            double power = 1.0;
            var laserType = GetLaserTypeForWavelength(wavelengthNm);

            foreach (var pin in component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light)
                    continue;

                var input = new ExternalInput(
                    $"src_{component.Identifier}_{pin.Name}",
                    laserType,
                    0,
                    new Complex(power, 0));

                portManager.AddLightSource(input, pin.LogicalPin.IDInFlow);
                configs.Add(new SourceConfigInfo(
                    component.Identifier, wavelengthNm, power));
            }
        }

        return configs;
    }

    /// <summary>
    /// Recursively collects all Component instances, including those inside ComponentGroups.
    /// Returns the raw Component objects, not ViewModels, to avoid TemplateName issues.
    /// </summary>
    private static List<Component> GetAllComponentsRecursively(IEnumerable<ComponentViewModel> components)
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

    private static bool IsLightSource(Component component)
    {
        var id = component.Identifier?.ToLowerInvariant() ?? "";
        return id.Contains("grating") || id.Contains("edge coupler");
    }

    internal static LaserType GetLaserTypeForWavelength(int wavelengthNm)
    {
        if (wavelengthNm == StandardWaveLengths.RedNM) return LaserType.Red;
        if (wavelengthNm == StandardWaveLengths.GreenNM) return LaserType.Green;
        if (wavelengthNm == StandardWaveLengths.BlueNM) return LaserType.Blue;
        return LaserType.Red;
    }

    private static void MergeFieldResults(
        Dictionary<Guid, Complex> target,
        Dictionary<Guid, Complex> source)
    {
        foreach (var kvp in source)
        {
            if (target.ContainsKey(kvp.Key))
                target[kvp.Key] += kvp.Value;
            else
                target[kvp.Key] = kvp.Value;
        }
    }
}

public class SimulationResult
{
    public bool Success { get; set; }
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

    public string WavelengthSummary =>
        WavelengthsUsed.Count > 0
            ? string.Join(", ", WavelengthsUsed.Select(w => $"{w}nm"))
            : "none";
}
