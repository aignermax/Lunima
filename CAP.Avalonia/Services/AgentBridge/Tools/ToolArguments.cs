using System.Globalization;
using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>
/// Lenient readers for MCP tool arguments. Agents sometimes send numbers as strings, so
/// numeric readers accept both; absent values are reported as null instead of a default.
/// </summary>
internal static class ToolArguments
{
    /// <summary>The string value, or null when absent or not a string/number.</summary>
    public static string? GetString(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    /// <summary>The number, or null when absent or not parseable.</summary>
    public static double? GetDouble(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    /// <summary>The boolean, or <paramref name="fallback"/> when absent.</summary>
    public static bool GetBool(JsonElement args, string key, bool fallback = false)
    {
        if (!args.TryGetProperty(key, out var v))
            return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : fallback,
            _ => fallback,
        };
    }

    /// <summary>The non-empty strings of an array argument (a single string counts as one item).</summary>
    public static IReadOnlyList<string> GetStringList(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var v))
            return Array.Empty<string>();
        if (v.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(v.GetString()) ? Array.Empty<string>() : new[] { v.GetString()! };
        if (v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return v.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}
