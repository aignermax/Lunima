using System.Numerics;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services;

/// <summary>
/// Outcome of applying one <see cref="ComponentSMatrixData"/> to a component.
/// </summary>
/// <param name="Applied">Number of wavelength entries written to the component's map.</param>
/// <param name="Replaced">How many of the applied entries replaced an existing wavelength.</param>
/// <param name="Skipped">Per-entry reasons for wavelengths that could not be applied.</param>
public sealed record ApplyResult(
    int Applied,
    int Replaced,
    IReadOnlyList<(string WavelengthKey, string Reason)> Skipped)
{
    /// <summary>True when nothing was applied and at least one entry was rejected.</summary>
    public bool IsTotalFailure => Applied == 0 && Skipped.Count > 0;

    /// <summary>True when some entries applied and others were rejected.</summary>
    public bool IsPartial => Applied > 0 && Skipped.Count > 0;
}

/// <summary>
/// Outcome of <see cref="SMatrixOverrideApplicator.ApplyAll"/>.
/// </summary>
/// <param name="PerComponent">Per-component results keyed by <see cref="Component.Identifier"/>.</param>
/// <param name="OrphanKeys">Keys present in the store that did not match any live component.</param>
public sealed record ApplyAllResult(
    IReadOnlyDictionary<string, ApplyResult> PerComponent,
    IReadOnlyList<string> OrphanKeys);

/// <summary>
/// Applies stored S-matrix override data from <see cref="ComponentSMatrixData"/> to
/// a live component's <see cref="Component.WaveLengthToSMatrixMap"/>.
/// Centralises the conversion from persisted PIR data to the simulator's pin-keyed
/// transfer dictionary so port-name resolution and the S[row,col] convention are
/// applied consistently regardless of caller.
/// </summary>
public static class SMatrixOverrideApplicator
{
    /// <summary>
    /// Applies all wavelength entries from <paramref name="data"/> to the component's
    /// wavelength-to-S-matrix map, replacing existing entries for those wavelengths.
    /// Port ordering uses <see cref="SMatrixWavelengthEntry.PortNames"/> when available;
    /// falls back to <see cref="Component.PhysicalPins"/> order only when no port names
    /// were supplied at all (a name-list with the wrong count is rejected, not silently
    /// reordered, to avoid producing physically wrong S-matrices from misaligned ports).
    /// </summary>
    /// <param name="component">Live component receiving the override.</param>
    /// <param name="data">Stored override data to apply.</param>
    /// <param name="errorConsole">Optional error console; when supplied, partial failures are logged.</param>
    /// <returns>An <see cref="ApplyResult"/> describing how many wavelengths applied/replaced and why others were skipped.</returns>
    public static ApplyResult Apply(
        Component component,
        ComponentSMatrixData data,
        ErrorConsoleService? errorConsole = null)
    {
        var skipped = new List<(string, string)>();
        var physPins = component.PhysicalPins;
        if (physPins.Count == 0)
        {
            foreach (var key in data.Wavelengths.Keys)
                skipped.Add((key, "component has no physical pins"));
            return Finalize(component, 0, 0, skipped, errorConsole);
        }

        var pinByName = physPins
            .Where(pp => pp.LogicalPin != null)
            .ToDictionary(pp => pp.Name, pp => pp.LogicalPin!, StringComparer.OrdinalIgnoreCase);

        int applied = 0;
        int replaced = 0;

        foreach (var kvp in data.Wavelengths)
        {
            if (!int.TryParse(kvp.Key, out int wavelengthNm))
            {
                skipped.Add((kvp.Key, $"wavelength key '{kvp.Key}' is not an integer (nm)"));
                continue;
            }

            var entry = kvp.Value;
            if (entry.Rows != entry.Cols || entry.Rows == 0)
            {
                skipped.Add((kvp.Key, $"matrix is not square (rows={entry.Rows}, cols={entry.Cols})"));
                continue;
            }

            int n = entry.Rows;
            int expectedLength = n * n;
            if (entry.Real.Count < expectedLength || entry.Imag.Count < expectedLength)
            {
                skipped.Add((kvp.Key, $"real/imag arrays shorter than {expectedLength} (got real={entry.Real.Count}, imag={entry.Imag.Count})"));
                continue;
            }

            var (pins, reason) = ResolvePins(entry, n, physPins, pinByName);
            if (pins == null)
            {
                skipped.Add((kvp.Key, reason ?? "could not resolve pins"));
                continue;
            }

            bool wasPresent = component.WaveLengthToSMatrixMap.ContainsKey(wavelengthNm);
            component.WaveLengthToSMatrixMap[wavelengthNm] = BuildSMatrix(pins, entry, n);
            applied++;
            if (wasPresent) replaced++;
        }

        return Finalize(component, applied, replaced, skipped, errorConsole);
    }

    private static ApplyResult Finalize(
        Component component,
        int applied,
        int replaced,
        List<(string, string)> skipped,
        ErrorConsoleService? errorConsole)
    {
        if (errorConsole != null && skipped.Count > 0)
        {
            var lines = string.Join("; ", skipped.Select(s => $"{s.Item1} nm: {s.Item2}"));
            errorConsole.LogWarning(
                $"S-matrix override on '{component.Identifier}': skipped {skipped.Count} wavelength(s) — {lines}");
        }
        return new ApplyResult(applied, replaced, skipped);
    }

    /// <summary>
    /// Applies all S-matrix overrides in <paramref name="storedSMatrices"/> to matching
    /// components. A component matches by <see cref="Component.Identifier"/>, with an
    /// optional <paramref name="templateKeyResolver"/> consulted as a fallback so PDK-
    /// template-scoped overrides (key shape <c>"{pdkSource}::{templateName}"</c>) reach
    /// every instance of that template.
    /// Keys with no matching component are reported as orphans rather than silently kept.
    /// </summary>
    public static ApplyAllResult ApplyAll(
        IEnumerable<Component> components,
        IReadOnlyDictionary<string, ComponentSMatrixData> storedSMatrices,
        Func<Component, string?>? templateKeyResolver = null,
        ErrorConsoleService? errorConsole = null)
    {
        var componentList = components.ToList();
        var perComponent = new Dictionary<string, ApplyResult>();
        var matchedKeys = new HashSet<string>();

        foreach (var comp in componentList)
        {
            string? resolvedKey = null;
            if (storedSMatrices.TryGetValue(comp.Identifier, out var data))
                resolvedKey = comp.Identifier;
            else
            {
                var templateKey = templateKeyResolver?.Invoke(comp);
                if (templateKey != null && storedSMatrices.TryGetValue(templateKey, out data))
                    resolvedKey = templateKey;
            }

            if (resolvedKey != null && data != null)
            {
                perComponent[comp.Identifier] = Apply(comp, data, errorConsole);
                matchedKeys.Add(resolvedKey);
            }
        }

        var orphans = storedSMatrices.Keys.Where(k => !matchedKeys.Contains(k)).ToList();
        if (errorConsole != null && orphans.Count > 0)
        {
            errorConsole.LogWarning(
                $"S-matrix overrides could not be applied: {orphans.Count} stored entry/entries " +
                $"have no matching component (renamed or removed): {string.Join(", ", orphans)}");
        }
        return new ApplyAllResult(perComponent, orphans);
    }

    private static (List<Pin>? pins, string? reason) ResolvePins(
        SMatrixWavelengthEntry entry,
        int n,
        List<PhysicalPin> physPins,
        Dictionary<string, Pin> pinByName)
    {
        if (entry.PortNames != null)
        {
            if (entry.PortNames.Count != n)
                return (null, $"PortNames count {entry.PortNames.Count} does not match matrix dimension {n}");

            var ordered = new List<Pin>();
            foreach (var name in entry.PortNames)
            {
                if (!pinByName.TryGetValue(name, out var pin))
                {
                    var available = string.Join(", ", pinByName.Keys);
                    return (null, $"port name '{name}' not found on component (available: {available})");
                }
                ordered.Add(pin);
            }
            return (ordered, null);
        }

        var positional = physPins
            .Select(pp => pp.LogicalPin)
            .Where(p => p != null)
            .Select(p => p!)
            .Take(n)
            .ToList();

        if (positional.Count != n)
            return (null, $"component has {positional.Count} usable pins; need {n}");

        return (positional, null);
    }

    private static SMatrix BuildSMatrix(
        List<Pin> pins,
        SMatrixWavelengthEntry entry,
        int n)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>();
        for (int row = 0; row < n; row++)
        {
            for (int col = 0; col < n; col++)
            {
                int flatIdx = row * n + col;
                var value = new Complex(entry.Real[flatIdx], entry.Imag[flatIdx]);
                transfers[BuildTransferKey(pins[col], pins[row])] = value;
            }
        }

        sMatrix.SetValues(transfers);
        return sMatrix;
    }

    /// <summary>
    /// Encodes the S-matrix mapping convention in one place:
    /// <c>entry.Real[row*n + col] = S[row, col]</c> with row=output port, col=input port,
    /// stored as a transmission keyed by <c>(input.IDInFlow, output.IDOutFlow)</c>.
    /// This matches Lumerical/Touchstone "S(out, in)" output and aligns with
    /// <see cref="SMatrix.SetValues"/>, which writes <c>SMat[outflow, inflow]</c>
    /// from the same key shape.
    /// </summary>
    private static (Guid InFlow, Guid OutFlow) BuildTransferKey(Pin inputPin, Pin outputPin)
        => (inputPin.IDInFlow, outputPin.IDOutFlow);
}
