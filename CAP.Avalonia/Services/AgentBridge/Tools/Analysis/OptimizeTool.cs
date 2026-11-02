using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Analysis;

/// <summary>
/// <c>optimize</c>: tunes component parameters server-side so an objective on the simulated
/// outputs is best — maximise or minimise one output's power, or maximise the ratio of two
/// outputs (extinction). Cyclic coordinate search: a coarse scan of each parameter, then a
/// golden-section refinement around the best sample, repeated for a number of rounds. The best
/// values are applied as one undo step.
/// </summary>
public sealed class OptimizeTool : IAgentBridgeTool
{
    private const int DefaultRounds = 2;
    private const int ScanSamples = 13;
    private const int RefineSteps = 12;
    private static readonly double GoldenRatio = (Math.Sqrt(5) - 1) / 2;

    private readonly AgentDesignSession _session;
    private readonly SimulationService _simulation;

    /// <summary>Creates the tool.</summary>
    public OptimizeTool(AgentDesignSession session, SimulationService simulation)
    {
        _session = session;
        _simulation = simulation;
    }

    /// <inheritdoc/>
    public string Name => "optimize";

    /// <inheritdoc/>
    public string Description =>
        "Tune parameters to optimise the simulated outputs: parameters = [{id, parameter | slider, min?, max?}], " +
        "objective = {maximize: \"<output>\"} | {minimize: \"<output>\"} | {maximize_ratio: [\"<a>\", \"<b>\"]} (outputs " +
        "are light sources with the laser off). Coordinate search (scan + golden section), rounds (default 2). Applies " +
        "the best values as one undo step and returns them with the objective in dB.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            parameters = new { type = "array", items = new { type = "object" } },
            objective = new { type = "object" },
            rounds = new { type = "integer", description = $"Search rounds over all parameters (default {DefaultRounds})." },
        },
        required = new[] { "parameters", "objective" },
    };

    private sealed record Knob(ComponentViewModel Owner, Slider Slider, double Min, double Max);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var knobs = ReadKnobs(arguments, out var error);
        var objective = knobs == null ? null : OptimizationObjective.Read(arguments, out error);
        if (knobs == null || objective == null)
            return AgentToolResult.Error(error!);

        var originals = knobs.Select(k => k.Slider.Value).ToList();
        var evaluations = 0;
        async Task<double> ScoreAsync()
        {
            evaluations++;
            var result = await _simulation.RunAsync(_session.Canvas, context?.CancellationToken ?? default, context);
            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
            return objective.Score(SimulationReport.MeasureOutputs(_session.Canvas, result.FieldResults!));
        }

        double best;
        try
        {
            best = await ScoreAsync();
            var initial = best;
            for (var round = 0; round < Math.Max(1, (int)(ToolArguments.GetDouble(arguments, "rounds") ?? DefaultRounds)); round++)
                foreach (var knob in knobs)
                    best = await SearchAsync(knob, ScoreAsync, best);
            var tuned = knobs.Select(k => k.Slider.Value).ToList();
            for (var i = 0; i < knobs.Count; i++) knobs[i].Slider.Value = originals[i];

            await using var edit = new AgentDocumentOperation(_session.Canvas, context);
            var steps = knobs.Select((k, i) => (IUndoableCommand)new SetSliderValueCommand(k.Owner, k.Slider, tuned[i])).ToList();
            edit.Commit(() => _session.Commands.ExecuteCommand(new CompositeCommand("Optimize parameters", steps)));
            edit.Commit(_session.Canvas.InvalidateSimulation);
            return AgentToolResult.Json(new
            {
                Objective = objective.Describe(),
                InitialDb = Math.Round(initial, 3),
                BestDb = Math.Round(best, 3),
                Evaluations = evaluations,
                Parameters = knobs.Select((k, i) => new { Component = k.Owner.Component.Identifier, k.Slider.Number, Value = Math.Round(tuned[i], 6) }).ToList(),
            });
        }
        catch (InvalidOperationException ex)
        {
            for (var i = 0; i < knobs.Count; i++) knobs[i].Slider.Value = originals[i];
            return AgentToolResult.Error($"Simulation failed during optimisation: {ex.Message}");
        }
    }

    /// <summary>Coarse scan, then golden-section refinement around the best sample; leaves the slider at its best.</summary>
    private static async Task<double> SearchAsync(Knob knob, Func<Task<double>> score, double current)
    {
        var bestValue = knob.Slider.Value;
        var best = current;
        var step = (knob.Max - knob.Min) / (ScanSamples - 1);
        for (var i = 0; i < ScanSamples; i++)
        {
            knob.Slider.Value = knob.Min + i * step;
            var s = await score();
            if (s > best) (best, bestValue) = (s, knob.Slider.Value);
        }

        var (low, high) = (Math.Max(knob.Min, bestValue - step), Math.Min(knob.Max, bestValue + step));
        for (var i = 0; i < RefineSteps; i++)
        {
            var a = high - GoldenRatio * (high - low);
            var b = low + GoldenRatio * (high - low);
            knob.Slider.Value = a;
            var sa = await score();
            knob.Slider.Value = b;
            var sb = await score();
            if (sa >= sb) high = b; else low = a;
            if (Math.Max(sa, sb) > best) (best, bestValue) = sa >= sb ? (sa, a) : (sb, b);
        }
        knob.Slider.Value = bestValue;
        return best;
    }

    private List<Knob>? ReadKnobs(JsonElement arguments, out string? error)
    {
        error = null;
        if (!arguments.TryGetProperty("parameters", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            error = "Pass parameters: [{\"id\": \"ps\", \"parameter\": \"phase_shift\"}, ...].";
            return null;
        }
        var knobs = new List<Knob>();
        foreach (var item in list.EnumerateArray())
        {
            var id = ToolArguments.GetString(item, "id") ?? "";
            var owner = _session.FindComponent(id);
            var slider = owner == null ? null : FindSlider(owner.Component, ToolArguments.GetString(item, "parameter"), (int?)ToolArguments.GetDouble(item, "slider"));
            if (slider == null)
            {
                error = $"No tunable parameter on '{id}' as given. See get_component.";
                return null;
            }
            var min = Math.Max(slider.MinValue, ToolArguments.GetDouble(item, "min") ?? slider.MinValue);
            var max = Math.Min(slider.MaxValue, ToolArguments.GetDouble(item, "max") ?? slider.MaxValue);
            knobs.Add(new Knob(owner!, slider, min, Math.Max(min, max)));
        }
        return knobs;
    }

    private static Slider? FindSlider(Component component, string? parameter, int? number)
    {
        if (parameter == null)
            return component.GetSlider(number ?? 0);
        var definition = component.ParameterDefinitions.FirstOrDefault(d =>
            string.Equals(d.Name, parameter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Label, parameter, StringComparison.OrdinalIgnoreCase));
        return definition?.SliderNumber is int bound ? component.GetSlider(bound) : null;
    }
}
