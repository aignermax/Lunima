namespace CAP.Avalonia.ViewModels.Library;

/// <summary>
/// Finds the inline Nazca code of a PDK template whose geometry is written as code in the
/// PDK itself (e.g. the Demo PDK crossing) instead of a function of the PDK's Nazca module.
/// </summary>
public static class InlineNazcaCode
{
    private const string GdsFactoryBackend = "gdsfactory";

    /// <summary>
    /// The inline Nazca code of the template with this module and function, or null when the
    /// template renders through its module (or carries gdsfactory code).
    /// </summary>
    /// <param name="templates">The loaded component library.</param>
    /// <param name="moduleName">The component's Nazca module name.</param>
    /// <param name="functionName">The component's Nazca function name.</param>
    public static string? Find(IEnumerable<ComponentTemplate> templates, string? moduleName, string? functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return null;
        var template = templates.FirstOrDefault(t =>
            string.Equals(t.NazcaFunctionName, functionName, StringComparison.Ordinal)
            && string.Equals(t.NazcaModuleName ?? "", moduleName ?? "", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(t.RawCode)
            && !string.Equals(t.RawCodeBackend, GdsFactoryBackend, StringComparison.OrdinalIgnoreCase));
        return template?.RawCode;
    }
}
