using System.Text.Json;

namespace CAP_Core.Solvers.Protocol;

/// <summary>Extracts the final valid single-line JSON object after solver/library log output.</summary>
public static class TrailingJsonObject
{
    public static string? Extract(string stdout)
    {
        var lines = stdout.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || !trimmed.StartsWith('{')) continue;
            try
            {
                using var _ = JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch (JsonException) { /* keep looking */ }
        }
        return null;
    }
}
