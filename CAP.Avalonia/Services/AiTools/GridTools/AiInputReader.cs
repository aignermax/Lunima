using System.Text.Json;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// Shared JSON input parsing helpers for AI grid tool implementations.
/// </summary>
internal static class AiInputReader
{
    /// <summary>Gets a string property value, or empty string if missing.</summary>
    internal static string GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    /// <summary>Gets a double property value, or 0.0 if missing.</summary>
    internal static double GetDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetDouble() : 0.0;

    /// <summary>Gets an integer property value, or the default if missing.</summary>
    internal static int GetInt(JsonElement el, string key, int defaultVal = 0) =>
        el.TryGetProperty(key, out var v) ? v.GetInt32() : defaultVal;

    /// <summary>Gets a string array property value, or an empty list if missing or invalid.</summary>
    internal static IReadOnlyList<string> GetStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arrayEl) || arrayEl.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return arrayEl.EnumerateArray()
            .Select(item => item.GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}
