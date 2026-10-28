using System.Globalization;
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Resolves stored PDK parameters and legacy demofab heuristics to a Nazca function call.</summary>
internal static class NazcaComponentFunctionResolver
{
    /// <summary>
    /// Maps a component to its Nazca function call string.
    /// Uses the stored NazcaFunctionName when it's a real PDK function,
    /// falls back to heuristic demofab mapping otherwise.
    /// </summary>
    internal static string GetNazcaFunction(Component comp)
    {
        // Use stored PDK function name if available and looks like a real function
        var funcName = comp.NazcaFunctionName;
        if (!string.IsNullOrEmpty(funcName) && NazcaCoordinateMapper.IsPdkFunction(funcName))
        {
            var funcParams = comp.NazcaFunctionParameters;
            if (!string.IsNullOrEmpty(funcParams)
                && NazcaCoordinateMapper.IsParametricStraight(funcName, funcParams))
            {
                // A parametric straight calls its generated stub — the same call
                // shape demo_pdk.* straights and non-dotted PDK straights already
                // use. The REAL module call (demo.shallow.strt) would dissolve at
                // export: nazca flattens interconnect straights into the PARENT
                // cell, erasing the component from the GDS structure (and merging
                // its two connections into one on re-import).
                var stubFuncName = System.Text.RegularExpressions.Regex.Replace(
                    funcName, @"[^a-zA-Z0-9_]", "_");
                return $"{stubFuncName}({funcParams})";
            }

            // Keep dots (for module attribute access like demo.mmi2x2_dp), replace other invalid chars.
            // The placement calls the parameter-specific stub (issue #783): StubName appends
            // the parameters hash exactly when the stub generator did — dotted names bypass
            // stubs (real module call) and parametric straights embed the length already.
            var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(
                NazcaStubNaming.StubName(funcName, comp.NazcaFunctionParameters), @"[^a-zA-Z0-9_.]", "_");

            // Forward stored parameters verbatim — the caller (component model)
            // is responsible for ensuring they match the target PDK function's signature.
            if (!string.IsNullOrEmpty(funcParams))
                return $"{pythonFuncName}({funcParams})";
            else
                return $"{pythonFuncName}()";
        }

        // For demo_pdk components, sanitize the function name to a valid Python identifier (replace dots too)
        if (!string.IsNullOrEmpty(funcName) && funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase))
        {
            var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(funcName, @"[^a-zA-Z0-9_]", "_");

            // Skip parameters for stub components - stubs don't support them
            var funcParams = comp.NazcaFunctionParameters;
            bool isParametricStraight = NazcaCoordinateMapper.IsParametricStraight(funcName, funcParams);

            if (isParametricStraight && !string.IsNullOrEmpty(funcParams))
                return $"{pythonFuncName}({funcParams})";
            else
                return $"{pythonFuncName}()";
        }

        // Fallback: heuristic mapping to demofab
        var name = funcName?.ToLower() ?? comp.Identifier.ToLower();
        var ci = CultureInfo.InvariantCulture;

        if (name.Contains("straight") || name.Contains("waveguide"))
            return $"demo.shallow.strt(length={comp.WidthMicrometers.ToString(ci)})";
        if (name.Contains("splitter") || name.Contains("1x2"))
            return "demo.mmi1x2_sh()";
        if (name.Contains("grating"))
            return "demo.io()";
        if (name.Contains("coupler") || name.Contains("2x2"))
            return "demo.mmi2x2_dp()";
        if (name.Contains("phase") || name.Contains("shifter"))
            return "demo.eopm_dc(length=500)";
        if (name.Contains("detector") || name.Contains("photo"))
            return "demo.pd()";
        if (name.Contains("bend"))
            return "demo.shallow.bend(angle=90)";
        if (name.Contains("y-junction") || name.Contains("yjunction"))
            return "demo.mmi1x2_sh()";

        return $"demo.shallow.strt(length={comp.WidthMicrometers.ToString(ci)})";
    }
}
