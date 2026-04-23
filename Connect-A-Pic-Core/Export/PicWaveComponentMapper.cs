using System.Globalization;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Maps a Lunima <see cref="Component"/> to the Python constructor call it
/// should emit in the generated PICWave script. Two decision paths:
/// <list type="number">
///   <item><description>If the component has an S-matrix at the target
///     wavelength, always emit a <c>CustomComponent</c> backed by the measured
///     data — this is physically truthful regardless of component type.</description></item>
///   <item><description>Otherwise, fall back to a typed constructor
///     (<c>Waveguide</c>, <c>MMI</c>, <c>DirectionalCoupler</c>,
///     <c>GratingCoupler</c>) only when the component's name matches a known
///     pattern. Unknown types throw — we never emit a silently-wrong stub.</description></item>
/// </list>
/// </summary>
internal static class PicWaveComponentMapper
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Ordered by specificity — the first match wins, so patterns that are
    // substrings of each other (e.g. "grating_coupler" vs "coupler") must
    // appear in the more-specific-first order.
    private static readonly (string Needle, Func<Component, string> Build)[] Patterns =
    {
        ("grating_coupler", _ => "GratingCoupler()"),
        ("gc_",             _ => "GratingCoupler()"),
        ("directional_coupler", _ => "DirectionalCoupler(coupling=0.5)"),
        ("dc_",             _ => "DirectionalCoupler(coupling=0.5)"),
        ("mmi",             c => $"MMI(ports={c.PhysicalPins.Count})"),
        ("splitter",        c => $"MMI(ports={c.PhysicalPins.Count})"),
        ("1x2",             c => $"MMI(ports={c.PhysicalPins.Count})"),
        ("2x2",             c => $"MMI(ports={c.PhysicalPins.Count})"),
        ("straight",        c => BuildWaveguide(c)),
        ("waveguide",       c => BuildWaveguide(c)),
        ("strt",            c => BuildWaveguide(c)),
        ("wg_",             c => BuildWaveguide(c)),
    };

    /// <summary>
    /// Returns the Python constructor expression for <paramref name="comp"/>.
    /// When <paramref name="hasSMatrix"/> is true, always emits a CustomComponent
    /// carrying the S-matrix reference. Otherwise applies name-based dispatch
    /// and throws <see cref="InvalidOperationException"/> for unknown types.
    /// </summary>
    internal static string Build(Component comp, bool hasSMatrix, string varName)
    {
        int numPorts = comp.PhysicalPins.Count;
        if (numPorts == 0)
            throw new InvalidOperationException(
                $"Component '{comp.Name ?? comp.Identifier}' has no physical pins — " +
                "PICWave cannot wire up a port-less component. Add pins or remove it " +
                "from the design before exporting.");

        if (hasSMatrix)
            return $"CustomComponent(s_matrices=_s_{varName}, num_ports={numPorts})";

        var name = (comp.NazcaFunctionName ?? comp.Identifier ?? comp.Name ?? "").ToLowerInvariant();
        foreach (var (needle, build) in Patterns)
            if (name.Contains(needle))
                return build(comp);

        // No S-matrix + no recognised type → we would have to invent S-parameters.
        // "No silent fallbacks" says throw; the user can fix by (a) adding an
        // S-matrix to the component or (b) renaming the component to match one
        // of the patterns above.
        throw new InvalidOperationException(
            $"Component '{comp.Name ?? comp.Identifier}' (NazcaFunctionName='{comp.NazcaFunctionName}') " +
            "has neither an S-matrix at the target wavelength nor a recognised name pattern. " +
            "PICWave export refuses to invent S-parameters for unknown components. " +
            "Provide an S-matrix via WaveLengthToSMatrixMap or rename the component to include " +
            "one of: grating_coupler, directional_coupler, mmi, splitter, waveguide, straight.");
    }

    private static string BuildWaveguide(Component comp)
    {
        // Length is approximated from the on-canvas width — real designs should
        // set this from PDK data once the PIR round-trip picks up a length field.
        double lengthUm = comp.WidthMicrometers > 0 ? comp.WidthMicrometers : 10.0;
        return $"Waveguide(length={lengthUm.ToString("F2", Inv)}e-6, loss=2.0)";
    }
}
