using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Services;

/// <summary>
/// Orchestrates S-Matrix light simulation from the Avalonia UI state.
/// Bridges the UI (DesignCanvasViewModel) with the core simulation engine
/// (GridLightCalculator) using physical coordinates.
/// </summary>
public class SimulationService
{
    /// <summary>
    /// Default wavelength for simulation in nm (1550nm = standard telecom C-band).
    /// </summary>
    public int WavelengthNm { get; set; } = StandardWaveLengths.RedNM;

    /// <summary>
    /// Default optical input power (linear, not dB).
    /// </summary>
    public double InputPower { get; set; } = 1.0;

    /// <summary>
    /// Template names that are treated as light input sources.
    /// </summary>
    private static readonly HashSet<string> LightSourceTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Grating Coupler",
        "Edge Coupler"
    };

    /// <summary>
    /// Runs the full S-Matrix simulation and updates the PowerFlowVisualizer.
    /// </summary>
    public async Task<SimulationResult> RunAsync(
        DesignCanvasViewModel canvas,
        CancellationToken cancellationToken = default)
    {
        if (canvas.Components.Count == 0)
            return SimulationResult.Empty("No components placed");

        if (canvas.Connections.Count == 0)
            return SimulationResult.Empty("No connections");

        // 1. Build component list
        var tileManager = new ComponentListTileManager();
        foreach (var compVm in canvas.Components)
        {
            tileManager.AddComponent(compVm.Component);
        }

        // 2. Set up light sources from I/O components
        var portManager = new PhysicalExternalPortManager();
        var lightSourceCount = ConfigureLightSources(canvas, portManager);

        if (lightSourceCount == 0)
            return SimulationResult.Empty("No light sources found (place a Grating Coupler or Edge Coupler)");

        // 3. Create GridManager for simulation
        var gridManager = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);

        // 4. Run simulation
        var builder = new SystemMatrixBuilder(gridManager);
        var calculator = new GridLightCalculator(builder, gridManager);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fieldResults = await calculator.CalculateFieldPropagationAsync(cts, WavelengthNm);

        // 5. Update PowerFlowVisualizer
        canvas.PowerFlowVisualizer.UpdateFromSimulation(
            canvas.ConnectionManager.Connections,
            fieldResults);

        // 6. Auto-enable visualization and force redraw
        canvas.PowerFlowVisualizer.IsEnabled = true;
        // Toggle off/on to ensure PropertyChanged fires even if already true
        canvas.ShowPowerFlow = false;
        canvas.ShowPowerFlow = true;

        return new SimulationResult
        {
            Success = true,
            FieldResults = fieldResults,
            WavelengthNm = WavelengthNm,
            LightSourceCount = lightSourceCount,
            ComponentCount = canvas.Components.Count,
            ConnectionCount = canvas.Connections.Count
        };
    }

    /// <summary>
    /// Finds I/O components and configures them as light sources.
    /// Returns the number of light sources configured.
    /// </summary>
    private int ConfigureLightSources(DesignCanvasViewModel canvas, PhysicalExternalPortManager portManager)
    {
        int count = 0;
        var laserType = GetLaserTypeForWavelength(WavelengthNm);

        foreach (var compVm in canvas.Components)
        {
            if (!IsLightSource(compVm))
                continue;

            // Find the pin on this I/O component
            foreach (var pin in compVm.Component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light)
                    continue;

                var input = new ExternalInput(
                    $"src_{compVm.Component.Identifier}_{pin.Name}",
                    laserType,
                    0, // tilePositionY unused in physical mode
                    new Complex(InputPower, 0));

                portManager.AddLightSource(input, pin.LogicalPin.IDInFlow);
                count++;
            }
        }

        return count;
    }

    private static bool IsLightSource(ComponentViewModel compVm)
    {
        // Check by template name
        if (compVm.TemplateName != null && LightSourceTemplates.Contains(compVm.TemplateName))
            return true;

        // Fallback: check component identifier
        var id = compVm.Component.Identifier?.ToLowerInvariant() ?? "";
        return id.Contains("grating") || id.Contains("edge coupler");
    }

    private static LaserType GetLaserTypeForWavelength(int wavelengthNm)
    {
        if (wavelengthNm == StandardWaveLengths.RedNM) return LaserType.Red;
        if (wavelengthNm == StandardWaveLengths.GreenNM) return LaserType.Green;
        if (wavelengthNm == StandardWaveLengths.BlueNM) return LaserType.Blue;
        return LaserType.Red; // Default to 1550nm
    }
}

public class SimulationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<Guid, Complex>? FieldResults { get; set; }
    public int WavelengthNm { get; set; }
    public int LightSourceCount { get; set; }
    public int ComponentCount { get; set; }
    public int ConnectionCount { get; set; }

    public static SimulationResult Empty(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}
