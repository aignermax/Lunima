namespace CAP.Avalonia.Services;

/// <summary>
/// Builds the runnable starting-point Nazca code shown in the per-instance code
/// editor (issue #556). It resolves a Lunima PDK module/function reference into a
/// concrete, executable <c>nazca</c> call the same way the preview script's
/// <c>_build_cell</c> does — peeling a dotted function prefix (e.g.
/// <c>"demo.mmi2x2_dp"</c> → module <c>demo</c>, function <c>mmi2x2_dp</c>) and
/// mapping the demo PDK onto <c>nazca.demofab</c>. Kept here (not inline in the
/// window) so the resolution is unit- and integration-testable.
/// </summary>
public static class NazcaCodeTemplateBuilder
{
    /// <summary>
    /// Produces a self-contained snippet defining <c>component()</c> that returns the
    /// component's current PDK cell. The result is valid input for the raw-code preview.
    /// </summary>
    /// <param name="moduleName">PDK module reference (e.g. "demo", "demo.shallow"), or null.</param>
    /// <param name="functionName">PDK function name, possibly dotted (e.g. "demo.mmi2x2_dp").</param>
    /// <param name="parameters">Optional keyword-argument string (e.g. "length=50"), or null.</param>
    public static string Build(string? moduleName, string? functionName, string? parameters)
    {
        var (importLine, baseExpr, funcLeaf) = ResolveCall(moduleName, functionName);
        var args = string.IsNullOrWhiteSpace(parameters) ? "" : parameters.Trim();

        // Many PDK function names (e.g. SiEPIC "ebeam_DC_2-1_te895") are not valid Python
        // identifiers — a hyphen/leading digit breaks "module.name". Fall back to getattr
        // so the generated code is at least syntactically valid; an unresolvable
        // module/attribute then fails with a clear runtime error instead of a parse error.
        var call = IsValidPythonIdentifier(funcLeaf)
            ? $"{baseExpr}.{funcLeaf}({args})"
            : $"getattr({baseExpr}, \"{funcLeaf}\")({args})";

        return
            "# Editable Nazca cell for THIS instance only (geometry-only — pins/S-matrix unchanged).\n" +
            "# Define component() returning a Nazca cell (or a module-level 'cell' variable).\n" +
            "import nazca as nd\n" +
            importLine + "\n\n" +
            "def component():\n" +
            $"    return {call}\n";
    }

    private static bool IsValidPythonIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || (!char.IsLetter(name[0]) && name[0] != '_'))
            return false;
        foreach (var c in name)
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        return true;
    }

    /// <summary>
    /// Resolves (importLine, baseExpr, funcLeaf) mirroring the preview script's
    /// <c>_build_cell</c>: a dotted function name is split into module-prefix + leaf, and
    /// the bundled demo PDK ("demo" / "demo_pdk" / "demo.&lt;sub&gt;") maps onto
    /// <c>nazca.demofab</c>. <paramref name="moduleName"/>'s sub-path becomes attribute
    /// access in <c>baseExpr</c>; <c>funcLeaf</c> is the (possibly non-identifier) call name.
    /// </summary>
    private static (string importLine, string baseExpr, string funcLeaf) ResolveCall(
        string? moduleName, string? functionName)
    {
        var module = moduleName?.Trim() ?? "";
        var function = functionName?.Trim() ?? "";

        // Peel a dotted prefix off the function name (e.g. "demo.mmi2x2_dp").
        if (function.Contains('.'))
        {
            var idx = function.LastIndexOf('.');
            var prefix = function[..idx];
            var leaf = function[(idx + 1)..];
            if (module.Length == 0 || module == "demo" || module == prefix)
                module = prefix;
            function = leaf;
        }

        if (module.Length == 0)
            module = "demo";

        // Demo PDK ships in nazca as nazca.demofab; sub-paths (e.g. "demo.shallow")
        // become attribute access on it.
        bool isDemo = module == "demo" || module == "demo_pdk"
                      || module.StartsWith("demo.") || module.StartsWith("demo_pdk.");
        if (isDemo)
        {
            var subParts = module.Split('.');
            var sub = subParts.Length > 1 ? "." + string.Join('.', subParts[1..]) : "";
            return ("import nazca.demofab", $"nazca.demofab{sub}", function);
        }

        return ($"import {module}", module, function);
    }
}
