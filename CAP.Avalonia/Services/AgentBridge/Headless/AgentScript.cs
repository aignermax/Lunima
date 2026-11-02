using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>One step of a <c>lunima agent run</c> script.</summary>
/// <param name="Tool">Tool name.</param>
/// <param name="Arguments">Tool arguments (a JSON object).</param>
public sealed record AgentScriptStep(string Tool, JsonElement Arguments);

/// <summary>
/// Parses a script: a JSON array of <c>{"tool": "name", "arguments": {…}}</c> objects.
/// <c>arguments</c> may be omitted.
/// </summary>
public static class AgentScript
{
    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>Parses <paramref name="json"/>; on failure returns false with a reason.</summary>
    public static bool TryParse(string json, out IReadOnlyList<AgentScriptStep> steps, out string? error)
    {
        steps = Array.Empty<AgentScriptStep>();
        error = null;
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            error = $"The script is not valid JSON: {ex.Message}";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "A script is a JSON array of {\"tool\": ..., \"arguments\": {...}} steps.";
                return false;
            }

            var parsed = new List<AgentScriptStep>();
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryParseStep(element, index++, out var step, out error))
                    return false;
                parsed.Add(step!);
            }
            steps = parsed;
            return true;
        }
    }

    private static bool TryParseStep(JsonElement element, int index, out AgentScriptStep? step, out string? error)
    {
        step = null;
        error = null;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("tool", out var tool)
            || tool.ValueKind != JsonValueKind.String)
        {
            error = $"Step {index} needs a string \"tool\".";
            return false;
        }

        var arguments = EmptyArguments;
        if (element.TryGetProperty("arguments", out var args))
        {
            if (args.ValueKind != JsonValueKind.Object)
            {
                error = $"Step {index}: \"arguments\" must be an object.";
                return false;
            }
            arguments = args.Clone();
        }
        step = new AgentScriptStep(tool.GetString()!, arguments);
        return true;
    }
}
