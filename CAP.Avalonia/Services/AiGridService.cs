using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services;

/// <summary>
/// Implements AI grid manipulation by wrapping canvas operations.
/// Translates high-level AI commands into application actions.
/// Uses a template provider delegate to decouple from <see cref="CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel"/>
/// and simplify testing.
/// </summary>
public class AiGridService : IAiGridService
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Func<IReadOnlyList<ComponentTemplate>> _templateProvider;
    private readonly CommandManager _commandManager;
    private readonly SimulationService _simulationService;

    private static readonly JsonSerializerOptions CompactJson =
        new JsonSerializerOptions { WriteIndented = false };

    /// <summary>
    /// Initializes the AI grid service with canvas and command dependencies.
    /// </summary>
    /// <param name="canvas">The design canvas ViewModel.</param>
    /// <param name="templateProvider">Delegate returning available component templates from the PDK.</param>
    /// <param name="commandManager">Undo-aware command executor.</param>
    /// <param name="simulationService">S-Matrix simulation service.</param>
    public AiGridService(
        DesignCanvasViewModel canvas,
        Func<IReadOnlyList<ComponentTemplate>> templateProvider,
        CommandManager commandManager,
        SimulationService simulationService)
    {
        _canvas = canvas;
        _templateProvider = templateProvider;
        _commandManager = commandManager;
        _simulationService = simulationService;
    }

    /// <inheritdoc/>
    public string GetGridState()
    {
        var components = _canvas.Components.Select(c => new
        {
            id = c.Name,
            type = c.TemplateName ?? c.Name,
            position = new { x = Math.Round(c.X, 1), y = Math.Round(c.Y, 1) },
            width = c.Width,
            height = c.Height
        }).ToList();

        var connections = _canvas.Connections.Select(c => new
        {
            from = c.Connection.StartPin.ParentComponent.Name,
            to = c.Connection.EndPin.ParentComponent.Name,
            from_pin = c.Connection.StartPin.Name,
            to_pin = c.Connection.EndPin.Name,
            path_length_um = Math.Round(c.PathLength, 1)
        }).ToList();

        var state = new
        {
            component_count = components.Count,
            connection_count = connections.Count,
            simulation_active = _canvas.ShowPowerFlow,
            components,
            connections
        };

        return JsonSerializer.Serialize(state, CompactJson);
    }

    /// <inheritdoc/>
    public string PlaceComponent(string componentType, double x, double y, int rotation = 0)
    {
        var templates = _templateProvider();
        var template = templates
            .FirstOrDefault(t => t.Name.Equals(componentType, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            var available = string.Join(", ", GetAvailableComponentTypes().Take(10));
            return $"Error: Unknown component type '{componentType}'. Available types: {available}";
        }

        double centeredX = x - template.WidthMicrometers / 2;
        double centeredY = y - template.HeightMicrometers / 2;

        var cmd = PlaceComponentCommand.TryCreate(_canvas, template, centeredX, centeredY);
        if (cmd == null)
        {
            return $"Error: Cannot place '{componentType}' near ({x:F0}, {y:F0})µm — position occupied or out of bounds.";
        }

        _commandManager.ExecuteCommand(cmd);

        var placed = _canvas.Components.LastOrDefault();
        return placed != null
            ? $"Placed {componentType} at ({placed.X:F0}, {placed.Y:F0})µm. Component ID: {placed.Name}"
            : $"Error: Component placement failed for '{componentType}'";
    }

    /// <inheritdoc/>
    public string CreateConnection(string fromComponentId, string toComponentId)
    {
        var fromVm = FindComponentById(fromComponentId);
        if (fromVm == null)
            return $"Error: Component '{fromComponentId}' not found. Use get_grid_state to see available IDs.";

        var toVm = FindComponentById(toComponentId);
        if (toVm == null)
            return $"Error: Component '{toComponentId}' not found. Use get_grid_state to see available IDs.";

        var fromPin = FindAvailablePin(fromVm.Component);
        if (fromPin == null)
            return $"Error: No available (unconnected) pins on '{fromComponentId}'";

        var toPin = FindAvailablePin(toVm.Component, exclude: fromPin);
        if (toPin == null)
            return $"Error: No available (unconnected) pins on '{toComponentId}'";

        var cmd = new CreateConnectionCommand(_canvas, fromPin, toPin);
        _commandManager.ExecuteCommand(cmd);

        return $"Connected {fromComponentId} (pin: {fromPin.Name}) → {toComponentId} (pin: {toPin.Name})";
    }

    /// <inheritdoc/>
    public string ClearGrid()
    {
        int count = _canvas.Components.Count;
        foreach (var comp in _canvas.Components.ToList())
            _canvas.RemoveComponent(comp);
        return $"Cleared {count} component(s) from the grid";
    }

    /// <inheritdoc/>
    public async Task<string> StartSimulationAsync()
    {
        var result = await _simulationService.RunAsync(_canvas);
        if (result.Success)
        {
            _canvas.ShowPowerFlow = true;
            return $"Simulation complete: {result.LightSourceCount} light source(s), " +
                   $"{result.ConnectionCount} connections @ {result.WavelengthSummary}";
        }
        return $"Simulation failed: {result.ErrorMessage ?? "Unknown error"}";
    }

    /// <inheritdoc/>
    public string StopSimulation()
    {
        _canvas.ShowPowerFlow = false;
        _canvas.PowerFlowVisualizer.IsEnabled = false;
        return "Simulation stopped";
    }

    /// <inheritdoc/>
    public string GetLightValues()
    {
        if (!_canvas.ShowPowerFlow)
            return "No simulation data available. Call start_simulation first.";

        var values = _canvas.Connections.Select(c => new
        {
            from_component = c.Connection.StartPin.ParentComponent.Name,
            to_component = c.Connection.EndPin.ParentComponent.Name,
            loss_db = Math.Round(c.LossDb, 3),
            path_length_um = Math.Round(c.PathLength, 1)
        }).ToList();

        return JsonSerializer.Serialize(values, CompactJson);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAvailableComponentTypes()
    {
        return _templateProvider()
            .Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
    }

    private CAP.Avalonia.ViewModels.Canvas.ComponentViewModel? FindComponentById(string id)
    {
        return _canvas.Components.FirstOrDefault(c =>
            c.Name.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            c.Component.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private PhysicalPin? FindAvailablePin(Component component, PhysicalPin? exclude = null)
    {
        return component.PhysicalPins.FirstOrDefault(pin =>
        {
            if (pin == exclude) return false;
            return !_canvas.Connections.Any(c =>
                c.Connection.StartPin == pin || c.Connection.EndPin == pin);
        });
    }
}
