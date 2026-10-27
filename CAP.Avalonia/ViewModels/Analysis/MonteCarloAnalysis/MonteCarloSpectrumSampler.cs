using System.Numerics;
using CAP_Core.Analysis.MonteCarloAnalysis;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;

/// <summary>Caller-side spectrum capture and display labels; retains no live simulation graph.</summary>
internal sealed class MonteCarloSpectrumSampler
{
    private readonly Dictionary<Guid, string> _pinNames;
    internal MonteCarloSpectrumSnapshot Snapshot { get; }
    internal double[] Wavelengths { get; }
    internal string PinName(Guid flowId) => _pinNames.GetValueOrDefault(flowId, "output");

    private MonteCarloSpectrumSampler(MonteCarloSpectrumSnapshot snapshot, double[] wavelengths,
        Dictionary<Guid, string> pinNames)
    {
        Snapshot = snapshot;
        Wavelengths = wavelengths;
        _pinNames = pinNames;
    }

    internal static (MonteCarloSpectrumSampler? Sampler, string? Error) Create(DesignCanvasViewModel canvas,
        WavelengthSweepConfiguration sweep, MonteCarloConfiguration configuration,
        IReadOnlyList<Component> participants, ProcessTolerances tolerances)
    {
        var tiles = new ComponentListTileManager();
        foreach (var component in canvas.Components) tiles.AddComponent(component.Component);
        var ports = new PhysicalExternalPortManager();
        var names = ConfigureLightSourcesAndNames(canvas, ports);
        if (ports.GetAllExternalInputs().Count == 0)
            return (null, Services.Localization.LocalizationService.Instance.Translate("Analysis.Common.NoLaserOn"));
        var grid = GridManager.CreateForSimulation(tiles, canvas.ConnectionManager, ports);
        var snapshot = MonteCarloSpectrumSnapshot.Capture(configuration, sweep, grid, ports,
            participants, tolerances, TransientCircuitFactory.CollectOutputCouplerPinIds(canvas));
        return (new(snapshot, sweep.GenerateWavelengthValues(), names), null);
    }

    private static Dictionary<Guid, string> ConfigureLightSourcesAndNames(
        DesignCanvasViewModel canvas, PhysicalExternalPortManager portManager)
    {
        var pinNames = new Dictionary<Guid, string>();
        foreach (var component in SimulationService.GetAllComponentsRecursively(canvas.Components))
        {
            string displayName = string.IsNullOrEmpty(component.HumanReadableName)
                ? component.Name
                : component.HumanReadableName!;
            bool injectsLight = SimulationService.IsLightSource(component) && component.LaserEnabled;

            foreach (var pin in component.PhysicalPins)
            {
                if (pin.LogicalPin?.MatterType != MatterType.Light) continue;
                pinNames[pin.LogicalPin.IDInFlow] = $"{displayName}.{pin.Name}";
                pinNames[pin.LogicalPin.IDOutFlow] = $"{displayName}.{pin.Name}";

                if (!injectsLight) continue;
                var input = new ExternalInput(
                    $"mc_{component.Identifier}_{pin.Name}",
                    LaserType.Red, 0, new Complex(1.0, 0));
                portManager.AddLightSource(input, pin.LogicalPin.IDInFlow);
            }
        }
        return pinNames;
    }
}
