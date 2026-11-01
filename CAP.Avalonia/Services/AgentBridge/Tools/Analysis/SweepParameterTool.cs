using System.Text.Json;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Analysis;

/// <summary>
/// <c>sweep_parameter</c>: steps one tunable parameter of a component across a range, simulates
/// at each value and returns every output's power — e.g. an MZI's transfer curve against its
/// phase. The parameter is restored afterwards; the design is not changed.
/// </summary>
public sealed class SweepParameterTool : IAgentBridgeTool
{
    private const int DefaultSteps = 21;
    private const int MaxSteps = 401;

    private readonly AgentDesignSession _session;
    private readonly SimulationService _simulation;

    /// <summary>Creates the tool.</summary>
    public SweepParameterTool(AgentDesignSession session, SimulationService simulation)
    {
        _session = session;
        _simulation = simulation;
    }

    /// <inheritdoc/>
    public string Name => "sweep_parameter";

    /// <inheritdoc/>
    public string Description =>
        "Sweep one tunable parameter of a component (by name or slider number) from 'from' to 'to' in 'steps' " +
        "values; simulate each and return every output's power in dB per value, plus the value with the most power " +
        "at each output. The parameter is restored afterwards (no design change).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Component id or unique display name." },
            parameter = new { type = "string", description = "Parameter name (see get_component)." },
            slider = new { type = "integer", description = "Slider number when the parameter has no name. Default 0." },
            from = new { type = "number", description = "First value (default: the parameter's minimum)." },
            to = new { type = "number", description = "Last value (default: the parameter's maximum)." },
            steps = new { type = "integer", description = $"Number of values (default {DefaultSteps}, max {MaxSteps})." },
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
        var slider = Find(vm.Component, ToolArguments.GetString(arguments, "parameter"), (int?)ToolArguments.GetDouble(arguments, "slider"));
        if (slider == null)
            return AgentToolResult.Error($"'{id}' has no such parameter. See get_component.");
        var steps = Math.Clamp((int)(ToolArguments.GetDouble(arguments, "steps") ?? DefaultSteps), 2, MaxSteps);
        var from = ToolArguments.GetDouble(arguments, "from") ?? slider.MinValue;
        var to = ToolArguments.GetDouble(arguments, "to") ?? slider.MaxValue;

        var original = slider.Value;
        var samples = new List<(double Value, IReadOnlyList<OutputPower> Outputs)>();
        try
        {
            for (var i = 0; i < steps; i++)
            {
                var value = Math.Clamp(from + (to - from) * i / (steps - 1), slider.MinValue, slider.MaxValue);
                slider.Value = value;
                var result = await _simulation.RunAsync(_session.Canvas, context?.CancellationToken ?? default, context);
                if (!result.Success)
                    return AgentToolResult.Error($"Simulation did not run at value {value}: {result.ErrorMessage}");
                samples.Add((value, SimulationReport.MeasureOutputs(_session.Canvas, result.FieldResults!)));
            }
        }
        finally
        {
            slider.Value = original;
            vm.OnSliderChanged?.Invoke();
        }

        return AgentToolResult.Json(Describe(vm.Component.Identifier, slider.Number, samples));
    }

    private static object Describe(string component, int slider, List<(double Value, IReadOnlyList<OutputPower> Outputs)> samples)
    {
        var outputs = samples[0].Outputs.Select(o => o.Component).ToList();
        return new
        {
            Component = component,
            Slider = slider,
            Values = samples.Select(s => Math.Round(s.Value, 6)).ToList(),
            Outputs = outputs.Select(name =>
            {
                var powers = samples.Select(s => s.Outputs.First(o => o.Component == name).Power).ToList();
                var best = powers.IndexOf(powers.Max());
                var worst = powers.IndexOf(powers.Min());
                return new
                {
                    Output = name,
                    PowerDb = powers.Select(SimulationReport.Db).ToList(),
                    MaxAt = Math.Round(samples[best].Value, 6),
                    MaxDb = SimulationReport.Db(powers[best]),
                    MinAt = Math.Round(samples[worst].Value, 6),
                    MinDb = SimulationReport.Db(powers[worst]),
                };
            }).ToList(),
        };
    }

    private static Slider? Find(Component component, string? parameter, int? number)
    {
        if (parameter == null)
            return component.GetSlider(number ?? 0);
        var definition = component.ParameterDefinitions.FirstOrDefault(d =>
            string.Equals(d.Name, parameter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Label, parameter, StringComparison.OrdinalIgnoreCase));
        return definition?.SliderNumber is int bound ? component.GetSlider(bound) : null;
    }
}
