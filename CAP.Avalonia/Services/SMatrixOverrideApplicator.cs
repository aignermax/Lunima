using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services;

/// <summary>
/// Applies stored S-matrix override data from <see cref="ComponentSMatrixData"/> to
/// a live component's <see cref="Component.WaveLengthToSMatrixMap"/>.
/// Used by the Component Settings dialog and the design-load pipeline to ensure
/// per-instance overrides are picked up by the simulator at run time.
/// </summary>
public static class SMatrixOverrideApplicator
{
    /// <summary>
    /// Applies all wavelength entries from <paramref name="data"/> to the component's
    /// wavelength-to-S-matrix map, replacing existing entries for those wavelengths.
    /// Port ordering uses <see cref="SMatrixWavelengthEntry.PortNames"/> when available;
    /// falls back to <see cref="Component.PhysicalPins"/> order.
    /// </summary>
    /// <returns><c>true</c> if at least one wavelength was successfully applied.</returns>
    public static bool Apply(Component component, ComponentSMatrixData data)
    {
        var physPins = component.PhysicalPins;
        if (physPins.Count == 0)
            return false;

        var pinByName = physPins
            .Where(pp => pp.LogicalPin != null)
            .ToDictionary(pp => pp.Name, pp => pp.LogicalPin, StringComparer.OrdinalIgnoreCase);

        bool anyApplied = false;

        foreach (var kvp in data.Wavelengths)
        {
            if (!int.TryParse(kvp.Key, out int wavelengthNm))
                continue;

            var entry = kvp.Value;
            if (entry.Rows != entry.Cols || entry.Rows == 0)
                continue;

            int n = entry.Rows;
            int expectedLength = n * n;
            if (entry.Real.Count < expectedLength || entry.Imag.Count < expectedLength)
                continue;

            var pins = ResolvePins(entry, n, physPins, pinByName);
            if (pins == null || pins.Count != n)
                continue;

            component.WaveLengthToSMatrixMap[wavelengthNm] = BuildSMatrix(pins, entry, n);
            anyApplied = true;
        }

        return anyApplied;
    }

    /// <summary>
    /// Applies all S-matrix overrides in <paramref name="storedSMatrices"/> to matching
    /// components in <paramref name="components"/>. Matching is done by
    /// <see cref="Component.Identifier"/> against the dictionary key.
    /// </summary>
    public static void ApplyAll(
        IEnumerable<Component> components,
        Dictionary<string, ComponentSMatrixData> storedSMatrices)
    {
        foreach (var comp in components)
        {
            if (storedSMatrices.TryGetValue(comp.Identifier, out var data))
                Apply(comp, data);
        }
    }

    private static List<CAP_Core.Components.Core.Pin>? ResolvePins(
        SMatrixWavelengthEntry entry,
        int n,
        List<PhysicalPin> physPins,
        Dictionary<string, CAP_Core.Components.Core.Pin> pinByName)
    {
        if (entry.PortNames != null && entry.PortNames.Count == n)
        {
            var ordered = new List<CAP_Core.Components.Core.Pin>();
            foreach (var name in entry.PortNames)
            {
                if (!pinByName.TryGetValue(name, out var pin))
                    return null;
                ordered.Add(pin);
            }
            return ordered;
        }

        // Positional fallback: use physical-pin order
        var positional = physPins
            .Select(pp => pp.LogicalPin)
            .Where(p => p != null)
            .Take(n)
            .ToList();

        return positional.Count == n ? positional : null;
    }

    private static SMatrix BuildSMatrix(
        List<CAP_Core.Components.Core.Pin> pins,
        SMatrixWavelengthEntry entry,
        int n)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>();
        for (int row = 0; row < n; row++)       // row = output port index
        {
            for (int col = 0; col < n; col++)   // col = input port index
            {
                int flatIdx = row * n + col;
                var value = new Complex(entry.Real[flatIdx], entry.Imag[flatIdx]);
                // S[row,col] = transmission from port col to port row
                transfers[(pins[col].IDInFlow, pins[row].IDOutFlow)] = value;
            }
        }

        sMatrix.SetValues(transfers);
        return sMatrix;
    }
}
