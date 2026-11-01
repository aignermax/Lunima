using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>set_parameter</c>: sets a tunable parameter (phase, coupling ratio, … — a slider) of a
/// component by parameter name or slider number, clamped to its range. Undoable.
/// </summary>
public sealed class SetParameterTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public SetParameterTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "set_parameter";

    /// <inheritdoc/>
    public string Description =>
        "Set a component's tunable parameter (see 'parameters' in get_component): by parameter name, or by " +
        "slider number (default 0). The value is clamped to [min, max]. Undoable; simulate again to see the effect.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Component id or unique display name." },
            parameter = new { type = "string", description = "Parameter name (e.g. 'phase', 'coupling_ratio')." },
            slider = new { type = "integer", description = "Slider number when the parameter has no name. Default 0." },
            value = new { type = "number", description = "New value (clamped to the parameter's range)." },
        },
        required = new[] { "id", "value" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var vm = _session.FindComponent(id);
        if (vm == null)
            return AgentToolResult.Error($"No top-level component with id '{id}'. Use list_components.");
        var value = ToolArguments.GetDouble(arguments, "value");
        if (value == null)
            return AgentToolResult.Error("Pass the new value.");
        var slider = ResolveSlider(vm.Component, ToolArguments.GetString(arguments, "parameter"),
            (int?)ToolArguments.GetDouble(arguments, "slider"), out var error);
        if (slider == null)
            return AgentToolResult.Error(error!);

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var previous = slider.Value;
        var command = new SetSliderValueCommand(vm, slider, value.Value);
        edit.Commit(() => _session.Commands.ExecuteCommand(command));
        edit.Commit(_session.Canvas.InvalidateSimulation);
        return AgentToolResult.Json(new
        {
            Component = vm.Component.Identifier,
            Slider = slider.Number,
            Previous = previous,
            Value = slider.Value,
            Clamped = Math.Abs(command.NewValue - value.Value) > double.Epsilon,
        });
    }

    private static Slider? ResolveSlider(Component component, string? parameter, int? number, out string? error)
    {
        error = null;
        if (parameter != null)
        {
            var definition = component.ParameterDefinitions.FirstOrDefault(d =>
                string.Equals(d.Name, parameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Label, parameter, StringComparison.OrdinalIgnoreCase));
            if (definition?.SliderNumber is int bound && component.GetSlider(bound) is { } named)
                return named;
            error = $"'{component.Identifier}' has no parameter '{parameter}'. See get_component.";
            return null;
        }

        var slider = component.GetSlider(number ?? 0);
        if (slider == null)
            error = $"'{component.Identifier}' has no slider {number ?? 0}; it has {component.GetAllSliders().Count} tunable parameters.";
        return slider;
    }
}

/// <summary>
/// <c>set_light_source</c>: switches a light source (grating/edge coupler, laser) on or off and
/// sets its wavelength and power. A source that is off is an output. Undoable.
/// </summary>
public sealed class SetLightSourceTool : IAgentBridgeTool
{
    private const double MaxPower = 1.0;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public SetLightSourceTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "set_light_source";

    /// <inheritdoc/>
    public string Description =>
        "Configure a light source (a coupler/laser; get_component shows 'light_source'): enabled=true makes it an " +
        "input, false an output; wavelength_nm (e.g. 1550, 1310) and power (linear 0..1). Omitted values stay. " +
        "mark_as_source=true turns any component with an optical pin into a source first. Undoable.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Component id or unique display name." },
            enabled = new { type = "boolean", description = "Laser on (input) or off (output)." },
            wavelength_nm = new { type = "integer", description = "Laser wavelength in nm." },
            power = new { type = "number", description = "Input power, linear 0..1." },
            mark_as_source = new { type = "boolean", description = "Mark the component as a light source when it is not one yet." },
        },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var vm = _session.FindComponent(id);
        if (vm == null)
            return AgentToolResult.Error($"No top-level component with id '{id}'. Use list_components.");
        var power = ToolArguments.GetDouble(arguments, "power");
        if (power is < 0 or > MaxPower)
            return AgentToolResult.Error("power is linear and must lie in 0..1.");

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        if (vm.LaserConfig == null)
        {
            if (!ToolArguments.GetBool(arguments, "mark_as_source"))
                return AgentToolResult.Error($"'{id}' is not a light source. Pass mark_as_source=true to make it one.");
            edit.Commit(() => _session.Commands.ExecuteCommand(new MarkLightSourceCommand(vm, marked: true)));
            if (vm.LaserConfig == null)
                return AgentToolResult.Error($"'{id}' cannot act as a light source.");
        }

        var enabled = arguments.TryGetProperty("enabled", out _) ? ToolArguments.GetBool(arguments, "enabled") : (bool?)null;
        var wavelength = (int?)ToolArguments.GetDouble(arguments, "wavelength_nm");
        edit.Commit(() => _session.Commands.ExecuteCommand(new ConfigureLightSourceCommand(vm, enabled, wavelength, power)));
        edit.Commit(_session.Canvas.InvalidateSimulation);
        var laser = vm.LaserConfig!;
        return AgentToolResult.Json(new
        {
            Component = vm.Component.Identifier,
            Enabled = laser.IsEnabled,
            laser.WavelengthNm,
            Power = laser.InputPower,
        });
    }
}
