using System.Numerics;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;

/// <summary>
/// Everything the spectrum sweep needs from the canvas: the simulation grid,
/// the configured light-source ports, human-readable pin labels, the output
/// coupler pins to plot and the design wavelength to mark.
/// </summary>
/// <param name="GridManager">Simulation grid built from the canvas.</param>
/// <param name="Ports">Port manager holding the configured light sources.</param>
/// <param name="PinNames">Flow-id → human-readable pin label.</param>
/// <param name="OutputCouplerPinIds">Light pins of couplers whose laser is off (the design outputs).</param>
/// <param name="DesignWavelengthNm">Wavelength of the first enabled laser (fallback: 1550 nm).</param>
/// <param name="InputLabel">Display name of the single active light source; null when zero or several are on.</param>
internal sealed record SpectrumCircuit(
    GridManager GridManager,
    PhysicalExternalPortManager Ports,
    IReadOnlyDictionary<Guid, string> PinNames,
    HashSet<Guid> OutputCouplerPinIds,
    int DesignWavelengthNm,
    string? InputLabel);

/// <summary>
/// Builds the simulation circuit for the spectrum tab from the current canvas.
/// Follows the same input/output convention as the Transient and Eye tabs:
/// couplers with the laser ON inject light, couplers with the laser OFF are
/// the listen-only outputs whose transmission is plotted.
/// </summary>
internal static class SpectrumSweepCircuitFactory
{
    /// <summary>
    /// Creates the circuit, or null when the canvas is empty.
    /// </summary>
    /// <param name="canvas">Canvas providing components and connections.</param>
    public static SpectrumCircuit? Create(DesignCanvasViewModel canvas)
    {
        if (canvas.Components.Count == 0) return null;

        var tileManager = new ComponentListTileManager();
        foreach (var compVm in canvas.Components)
            tileManager.AddComponent(compVm.Component);

        var portManager = new PhysicalExternalPortManager();
        int designWavelengthNm = ConfigureLightSources(canvas, portManager);

        var gridManager = GridManager.CreateForSimulation(
            tileManager, canvas.ConnectionManager, portManager);

        var displayNames = SpectrumLegendLabelBuilder.BuildDisplayNames(
            SimulationService.GetAllComponentsRecursively(canvas.Components));

        return new SpectrumCircuit(
            gridManager,
            portManager,
            SpectrumLegendLabelBuilder.BuildPinNameMap(displayNames),
            CollectOutputCouplerPinIds(canvas),
            designWavelengthNm,
            ResolveSingleInputLabel(canvas, displayNames));
    }

    /// <summary>
    /// Registers a light source on every light pin of each laser-on coupler and
    /// returns the design wavelength (first enabled laser, fallback 1550 nm).
    /// </summary>
    private static int ConfigureLightSources(
        DesignCanvasViewModel canvas, PhysicalExternalPortManager portManager)
    {
        int? designWavelengthNm = null;
        foreach (var compVm in canvas.Components)
        {
            if (!compVm.IsLightSource) continue;
            if (compVm.IsLaserOff) continue;

            var laserConfig = compVm.LaserConfig;
            double power = laserConfig?.InputPower ?? 1.0;
            designWavelengthNm ??= laserConfig?.WavelengthNm;

            foreach (var pin in compVm.Component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light) continue;
                var input = new ExternalInput(
                    $"spectrum_{compVm.Component.Identifier}_{pin.Name}",
                    LaserType.Red, 0, new Complex(power, 0));
                portManager.AddLightSource(input, pin.LogicalPin.IDInFlow);
            }
        }
        return designWavelengthNm ?? StandardWaveLengths.RedNM;
    }

    /// <summary>
    /// Light pins of couplers whose laser is off (the design outputs). Unlike
    /// the Transient tab, only the in-flow id per pin is collected: both flow
    /// directions of one pin share a label, so plotting both would double
    /// every legend entry.
    /// </summary>
    private static HashSet<Guid> CollectOutputCouplerPinIds(DesignCanvasViewModel canvas)
    {
        var pinIds = new HashSet<Guid>();
        foreach (var compVm in canvas.Components)
        {
            if (!compVm.IsLaserOff) continue;
            foreach (var pin in compVm.Component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light) continue;
                pinIds.Add(pin.LogicalPin.IDInFlow);
            }
        }
        return pinIds;
    }

    /// <summary>
    /// Display name of the single laser-on coupler (the spectrum's input), or
    /// null when zero or several lasers are on — a superposed transmission
    /// cannot be attributed to one input.
    /// </summary>
    private static string? ResolveSingleInputLabel(
        DesignCanvasViewModel canvas, IReadOnlyDictionary<Component, string> displayNames)
    {
        var active = canvas.Components
            .Where(compVm => compVm.IsLightSource && !compVm.IsLaserOff)
            .Select(compVm => compVm.Component)
            .ToList();
        return active.Count == 1 && displayNames.TryGetValue(active[0], out var label)
            ? label
            : null;
    }
}
