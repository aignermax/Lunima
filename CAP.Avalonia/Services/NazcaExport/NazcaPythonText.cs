using CAP.Avalonia.Services;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Shared Python literal escaping, comment sanitization, and the GDS port-label layer.</summary>
internal static class NazcaPythonText
{
    internal const string PortLabelLayer = "(1, 10)";

    /// <summary>
    /// Escapes a name for emission inside a single-quoted Python string literal:
    /// backslashes and single quotes would otherwise break (or inject into) the
    /// generated script, and raw line breaks would split the statement (#808).
    /// Internal so <see cref="NazcaRawCodeCellWriter"/> escapes pin labels identically.
    /// </summary>
    internal static string EscapePythonString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("'", "\\'", StringComparison.Ordinal)
             .Replace("\r", "\\r", StringComparison.Ordinal)
             .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// Makes an arbitrary PDK-controlled string (template name, PDK source) safe for
    /// emission inside a single-line Python COMMENT or a <c>"""</c> docstring of the
    /// generated script: CR/LF would break out of the line and inject raw script lines,
    /// a literal <c>"""</c> would terminate the docstring early. The characters are
    /// stripped outright (not escaped) — the text is informational only, so losing a
    /// quote sequence beats complicating the generated script. Internal so
    /// <see cref="NazcaRawCodeCellWriter"/> sanitizes its wrapper comments identically.
    /// </summary>
    internal static string SanitizePythonComment(string value) =>
        value.Replace("\"\"\"", string.Empty, StringComparison.Ordinal)
             .Replace("\r", string.Empty, StringComparison.Ordinal)
             .Replace("\n", string.Empty, StringComparison.Ordinal);
}
