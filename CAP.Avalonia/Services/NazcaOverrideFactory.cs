using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services;

/// <summary>
/// Builds the per-instance RawCode override scaffolding for the Nazca export (issue #559).
/// Each overridden component's self-contained RawCode (which defines <c>component()</c>)
/// is wrapped in a uniquely-named factory function so multiple overrides can coexist in
/// one script without colliding at module scope.
/// </summary>
public static class NazcaOverrideFactory
{
    private const int IndentSpaces = 4;

    /// <summary>
    /// Sanitizes a component identifier into a valid Python identifier fragment by
    /// replacing every character outside <c>[a-zA-Z0-9_]</c> with an underscore.
    /// </summary>
    /// <param name="identifier">The raw component identifier.</param>
    /// <returns>A sanitized fragment safe to embed in a Python function name.</returns>
    public static string Sanitize(string identifier) =>
        Regex.Replace(identifier ?? string.Empty, @"[^a-zA-Z0-9_]", "_");

    /// <summary>
    /// Returns the factory function name emitted for an overridden instance,
    /// e.g. <c>_ovr_Straight_Waveguide_100m</c>.
    /// </summary>
    /// <param name="identifier">The component identifier being overridden.</param>
    /// <returns>The Python factory function name.</returns>
    public static string FactoryName(string identifier) => $"_ovr_{Sanitize(identifier)}";

    /// <summary>
    /// Emits all override factory definitions, one per unique overridden identifier.
    /// Each factory locally scopes the user's RawCode (which defines <c>component()</c>)
    /// and returns the built cell via <c>return component()</c>. A missing <c>component()</c>
    /// raises a clear <c>NameError</c> at runtime rather than producing broken syntax.
    /// </summary>
    /// <param name="sb">The script builder to append to.</param>
    /// <param name="overrides">Map of component identifier to its non-null RawCode.</param>
    public static void AppendFactories(StringBuilder sb, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0)
            return;

        sb.AppendLine("# Per-instance RawCode overrides (issue #559)");
        sb.AppendLine();

        var emitted = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var kv in overrides)
        {
            var factoryName = FactoryName(kv.Key);
            if (!emitted.Add(factoryName))
                continue;

            sb.AppendLine($"def {factoryName}():");
            sb.AppendLine($"    # Raw-code override for instance '{kv.Key}'");
            foreach (var line in SplitLines(kv.Value))
            {
                if (line.Length == 0)
                    sb.AppendLine();
                else
                    sb.AppendLine(new string(' ', IndentSpaces) + line);
            }
            sb.AppendLine("    return component()");
            sb.AppendLine();
        }
    }

    private static IEnumerable<string> SplitLines(string rawCode)
    {
        // Normalize line endings so the emitted factory is platform-independent.
        var normalized = rawCode.Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.Split('\n');
    }
}
