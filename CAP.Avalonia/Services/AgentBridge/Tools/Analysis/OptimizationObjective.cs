using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Analysis;

/// <summary>
/// What <c>optimize</c> improves, as a score in dB where larger is better: an output's power
/// (maximise), its negative (minimise), or the power ratio of two outputs (extinction).
/// </summary>
public sealed class OptimizationObjective
{
    private readonly string _kind;
    private readonly string _first;
    private readonly string? _second;

    private OptimizationObjective(string kind, string first, string? second) => (_kind, _first, _second) = (kind, first, second);

    /// <summary>Reads the <c>objective</c> argument; null with a reason when malformed.</summary>
    public static OptimizationObjective? Read(JsonElement arguments, out string? error)
    {
        error = null;
        if (arguments.TryGetProperty("objective", out var objective) && objective.ValueKind == JsonValueKind.Object)
        {
            if (ToolArguments.GetString(objective, "maximize") is { } max) return new("maximize", max, null);
            if (ToolArguments.GetString(objective, "minimize") is { } min) return new("minimize", min, null);
            var ratio = ToolArguments.GetStringList(objective, "maximize_ratio");
            if (ratio.Count == 2) return new("maximize_ratio", ratio[0], ratio[1]);
        }
        error = "objective must be {maximize: \"out\"}, {minimize: \"out\"} or {maximize_ratio: [\"a\", \"b\"]}.";
        return null;
    }

    /// <summary>The score of one simulation (dB; larger is better).</summary>
    /// <exception cref="InvalidOperationException">A named output does not exist.</exception>
    public double Score(IReadOnlyList<OutputPower> outputs)
    {
        var first = Db(Find(outputs, _first));
        return _kind switch
        {
            "minimize" => -first,
            "maximize_ratio" => first - Db(Find(outputs, _second!)),
            _ => first,
        };
    }

    /// <summary>Human-readable form for the result.</summary>
    public string Describe() => _second == null ? $"{_kind} {_first}" : $"{_kind} {_first}/{_second}";

    private static double Find(IReadOnlyList<OutputPower> outputs, string name) =>
        outputs.FirstOrDefault(o => string.Equals(o.Component, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase))?.Power
        ?? throw new InvalidOperationException($"No output '{name}'. Outputs: {string.Join(", ", outputs.Select(o => o.Component))}.");

    private static double Db(double linear) => 10 * Math.Log10(Math.Max(linear, 1e-12));
}
