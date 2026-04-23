using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.Export;

/// <summary>
/// Exports Lunima designs to a PICWave-compatible Python circuit-simulator script.
/// Errors are loud: missing S-matrices on unrecognised components, unwired LogicalPins,
/// port-less components, or S-matrices whose pin GUIDs drifted from the component's
/// PhysicalPins all throw <see cref="InvalidOperationException"/> instead of emitting
/// a silently-wrong stub. See the generated script header for notes on running the
/// output against real PICWave (COM) or Simphony (open-source).
/// </summary>
public class PicWaveExporter
{
    /// <summary>Target wavelength at which S-parameters are extracted.</summary>
    public const int DefaultWavelengthNm = 1550;

    /// <summary>Minimum wavelength for the default sweep in nanometres.</summary>
    public const double DefaultWavelengthMinNm = 1500.0;

    /// <summary>Maximum wavelength for the default sweep in nanometres.</summary>
    public const double DefaultWavelengthMaxNm = 1600.0;

    /// <summary>Default number of wavelength sweep points.</summary>
    public const int DefaultNumPoints = 100;

    /// <summary>Exports the design to a PICWave Python script.</summary>
    /// <param name="components">Flat list of components on the canvas. ComponentGroups are flattened.</param>
    /// <param name="connections">Waveguide connections between pins.</param>
    /// <param name="wavelengthNm">Wavelength at which to extract S-parameters.</param>
    /// <param name="wavelengthMinNm">Sweep minimum wavelength in nm.</param>
    /// <param name="wavelengthMaxNm">Sweep maximum wavelength in nm.</param>
    /// <param name="numPoints">Number of sweep wavelength points.</param>
    /// <exception cref="InvalidOperationException">
    /// A component lacks both an S-matrix and a recognised name pattern, an S-matrix's
    /// pin GUIDs don't match the component's LogicalPins, or a component has no
    /// physical pins. See message for the offending component.
    /// </exception>
    /// <exception cref="ArgumentException">Invalid wavelength sweep parameters.</exception>
    public string Export(
        IEnumerable<Component> components,
        IEnumerable<WaveguideConnection> connections,
        int wavelengthNm = DefaultWavelengthNm,
        double wavelengthMinNm = DefaultWavelengthMinNm,
        double wavelengthMaxNm = DefaultWavelengthMaxNm,
        int numPoints = DefaultNumPoints)
    {
        ValidateSweep(wavelengthNm, wavelengthMinNm, wavelengthMaxNm, numPoints);

        var topLevel = components.ToList();
        var allComponents = FlattenComponents(topLevel).ToList();

        var allConnections = connections.ToList();
        allConnections.AddRange(CollectInternalGroupConnections(topLevel));
        allConnections = DeduplicateConnections(allConnections);

        return PicWaveScriptWriter.Write(
            allComponents, allConnections,
            wavelengthNm, wavelengthMinNm, wavelengthMaxNm, numPoints);
    }

    /// <summary>
    /// Collapses duplicate edges. A connection can legitimately appear in both
    /// <c>_canvas.Connections</c> and a <see cref="ComponentGroup.InternalPaths"/>
    /// list when a group is built from already-connected components — without
    /// dedup the emitted netlist would list the same edge twice.
    /// Order of the first occurrence is preserved so the generated script stays
    /// diff-friendly.
    /// </summary>
    private static List<WaveguideConnection> DeduplicateConnections(
        List<WaveguideConnection> connections)
    {
        var seen = new HashSet<(PhysicalPin, PhysicalPin)>();
        var result = new List<WaveguideConnection>(connections.Count);
        foreach (var c in connections)
        {
            // An (A→B) and (B→A) edge describe the same waveguide, so record
            // both orderings in the seen-set to catch the reverse form too.
            var forward = (c.StartPin, c.EndPin);
            var reverse = (c.EndPin, c.StartPin);
            if (seen.Contains(forward) || seen.Contains(reverse))
                continue;
            seen.Add(forward);
            result.Add(c);
        }
        return result;
    }

    private static void ValidateSweep(int wavelengthNm, double wlMinNm, double wlMaxNm, int numPoints)
    {
        if (wavelengthNm <= 0)
            throw new ArgumentException(
                $"wavelengthNm must be positive; got {wavelengthNm}.", nameof(wavelengthNm));
        if (wlMinNm <= 0 || wlMaxNm <= 0)
            throw new ArgumentException(
                $"Sweep bounds must be positive; got [{wlMinNm}, {wlMaxNm}] nm.");
        if (wlMinNm >= wlMaxNm)
            throw new ArgumentException(
                $"Sweep minimum must be strictly less than maximum; got [{wlMinNm}, {wlMaxNm}] nm.");
        if (numPoints < 2)
            throw new ArgumentException(
                $"numPoints must be at least 2 for a meaningful sweep; got {numPoints}.",
                nameof(numPoints));
    }

    /// <summary>
    /// Expands any <see cref="ComponentGroup"/> into its leaf components while
    /// preserving order — connections reference the leaf PhysicalPins directly.
    /// </summary>
    private static IEnumerable<Component> FlattenComponents(IEnumerable<Component> components)
    {
        foreach (var comp in components)
        {
            if (comp is ComponentGroup group)
            {
                foreach (var child in group.GetAllComponentsRecursive())
                    yield return child;
            }
            else
            {
                yield return comp;
            }
        }
    }

    /// <summary>
    /// Yields group-internal <see cref="FrozenWaveguidePath"/>s as
    /// <see cref="WaveguideConnection"/>s. A <see cref="ComponentGroup"/>
    /// stores connections between its children in <c>InternalPaths</c>, not
    /// on the top-level canvas connection list — so the exporter has to pick
    /// them up explicitly, or internal wiring (child↔child inside the group)
    /// silently vanishes from the generated script. Handles nested groups by
    /// recursing into child groups.
    /// </summary>
    private static IEnumerable<WaveguideConnection> CollectInternalGroupConnections(
        IEnumerable<Component> components)
    {
        foreach (var comp in components)
        {
            if (comp is not ComponentGroup group) continue;

            foreach (var frozen in group.InternalPaths)
            {
                if (frozen.StartPin == null || frozen.EndPin == null) continue;
                yield return new WaveguideConnection
                {
                    StartPin = frozen.StartPin,
                    EndPin = frozen.EndPin,
                };
            }

            foreach (var nestedConn in CollectInternalGroupConnections(group.ChildComponents))
                yield return nestedConn;
        }
    }
}
