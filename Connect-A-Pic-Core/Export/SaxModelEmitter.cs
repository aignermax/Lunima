using System.Globalization;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Emits one sax model function per component. Each function has signature
/// <c>(wl: float = ...) -&gt; sax.SDict</c>, where <c>wl</c> is in micrometres
/// and the returned dict maps <c>(port_a, port_b)</c> tuples to complex transfer
/// coefficients. sax's <c>sax.circuit(...)</c> composes these functions into a
/// full-circuit S-matrix.
///
/// <para>
/// Two paths:
/// <list type="bullet">
///   <item><description><b>Measured</b>: a <c>_s_&lt;var&gt;</c> dict is present
///   (see <see cref="SaxSMatrixEmitter"/>). The model does a nearest-
///   measured-wavelength lookup and rebuilds the SDict from the matrix.</description></item>
///   <item><description><b>Analytic waveguide</b>: a 2-port component whose
///   name matches a waveguide pattern. The model returns the canonical
///   propagation formula with configurable length / loss / neff.</description></item>
/// </list>
/// Anything else throws — we never invent S-parameters for unrecognised
/// components. Give the component an S-matrix or rename it to match a known
/// pattern.
/// </para>
/// </summary>
internal static class SaxModelEmitter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Default target wavelength baked into each model's signature (μm).</summary>
    private const double DefaultWlUm = 1.55;

    internal static void AppendModels(
        StringBuilder sb,
        IReadOnlyList<Component> components,
        int wavelengthNm)
    {
        if (components.Count == 0) return;

        sb.AppendLine("# ── Per-component sax models ────────────────────────────────────────");
        sb.AppendLine("# Each function takes a wavelength in micrometres and returns an SDict");
        sb.AppendLine("# suitable for sax.circuit(...). Pin names match the Lunima PhysicalPin");
        sb.AppendLine("# names, so netlist connections can reference them directly.");
        sb.AppendLine();

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var comp in components)
        {
            var varName = SaxIdentifier.ForVar(comp);
            if (!emitted.Add(varName)) continue;

            if (comp.WaveLengthToSMatrixMap.Count > 0)
                AppendMeasuredModel(sb, comp, varName);
            else
                AppendAnalyticModel(sb, comp, varName);
        }
    }

    private static void AppendMeasuredModel(StringBuilder sb, Component comp, string varName)
    {
        int n = comp.PhysicalPins.Count;
        if (n == 0) ThrowNoPins(comp);

        sb.AppendLine($"def {varName}_model(wl={DefaultWlUm.ToString(Inv)}):");
        sb.AppendLine($"    \"\"\"Measured-data model for {EscapeDocstring(comp.Name)}.\"\"\"");
        sb.AppendLine("    wl_m = float(wl) * 1e-6");
        sb.AppendLine($"    wls = sorted(_s_{varName}.keys())");
        sb.AppendLine("    nearest = min(wls, key=lambda w: abs(w - wl_m))");
        sb.AppendLine($"    m = _s_{varName}[nearest]");

        // Emit one sdict entry per matrix index pair. Pin ordering is the
        // component's PhysicalPins order — this mirrors the matrix rows/cols
        // produced by SaxSMatrixEmitter and PirSMatrixExtractor.
        sb.AppendLine("    return sax.reciprocal({");
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                // sax.reciprocal fills in (b, a) from (a, b); emit only the
                // upper triangle (including diagonal) to avoid duplicate keys.
                if (c < r) continue;
                var from = SaxIdentifier.ForPin(comp.PhysicalPins[r].Name);
                var to = SaxIdentifier.ForPin(comp.PhysicalPins[c].Name);
                sb.AppendLine($"        ('{from}', '{to}'): complex(m[{r}, {c}]),");
            }
        }
        sb.AppendLine("    })");
        sb.AppendLine();
    }

    private static void AppendAnalyticModel(StringBuilder sb, Component comp, string varName)
    {
        int n = comp.PhysicalPins.Count;
        if (n == 0) ThrowNoPins(comp);

        var name = (comp.NazcaFunctionName ?? comp.Identifier ?? comp.Name ?? "")
            .ToLowerInvariant();

        if (n == 2 && IsWaveguideLikeName(name))
        {
            AppendWaveguideAnalyticModel(sb, comp, varName);
            return;
        }

        // No measured data + not a known 2-port → refuse. We never invent
        // multi-port S-parameters. Explicit guidance matches the original
        // "loud failures" policy.
        throw new InvalidOperationException(
            $"Component '{comp.Name ?? comp.Identifier}' " +
            $"(NazcaFunctionName='{comp.NazcaFunctionName}', {n} pins) " +
            "has no measured S-matrix and no analytic fallback exists for this " +
            "port count. Provide S-parameters via WaveLengthToSMatrixMap, or — " +
            "for 2-port waveguide-like components — name it with one of: " +
            "waveguide, straight, strt, wg_.");
    }

    private static bool IsWaveguideLikeName(string lowerName)
    {
        return lowerName.Contains("waveguide")
            || lowerName.Contains("straight")
            || lowerName.Contains("strt")
            || lowerName.Contains("wg_");
    }

    private static void AppendWaveguideAnalyticModel(StringBuilder sb, Component comp, string varName)
    {
        // Length fallback: use the canvas width as a first approximation.
        // Real designs should provide a length via the PDK once there's a
        // dedicated length field.
        double lengthUm = comp.WidthMicrometers > 0 ? comp.WidthMicrometers : 10.0;
        var p0 = SaxIdentifier.ForPin(comp.PhysicalPins[0].Name);
        var p1 = SaxIdentifier.ForPin(comp.PhysicalPins[1].Name);

        sb.AppendLine($"def {varName}_model(wl={DefaultWlUm.ToString(Inv)}, ");
        sb.AppendLine($"                    length_um={lengthUm.ToString("G6", Inv)}, ");
        sb.AppendLine("                    loss_db_per_cm=2.0, neff=2.4):");
        sb.AppendLine($"    \"\"\"Analytic waveguide model for {EscapeDocstring(comp.Name)}.\"\"\"");
        sb.AppendLine("    phase = jnp.exp(1j * 2 * jnp.pi * neff * length_um / float(wl))");
        sb.AppendLine("    # loss_db_per_cm × length_cm → amplitude factor");
        sb.AppendLine("    amp = jnp.power(10.0, -loss_db_per_cm * length_um * 1e-4 / 20.0)");
        sb.AppendLine($"    return sax.reciprocal({{('{p0}', '{p1}'): amp * phase}})");
        sb.AppendLine();
    }

    private static void ThrowNoPins(Component comp)
    {
        throw new InvalidOperationException(
            $"Component '{comp.Name ?? comp.Identifier}' has no physical pins — " +
            "sax cannot model a port-less component. Add pins or remove it from " +
            "the design before exporting.");
    }

    /// <summary>
    /// Strips characters that would break a Python triple-quoted docstring
    /// (well, specifically the three-quote sequence). Other special chars are
    /// safe inside a <c>"""..."""</c> block.
    /// </summary>
    private static string EscapeDocstring(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "component";
        return text.Replace("\"\"\"", "'''");
    }
}
