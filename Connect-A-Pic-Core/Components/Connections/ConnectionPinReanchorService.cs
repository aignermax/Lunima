using CAP_Core.Components.Core;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Result of <see cref="ConnectionPinReanchorService.Reanchor"/>: how many connections
/// were re-anchored onto same-named replacement pins, which connections could not be
/// preserved (a referenced pin name no longer exists) and the matching user-facing warnings.
/// The caller is responsible for actually removing <see cref="DroppedConnections"/> from
/// its connection manager / view-model collections.
/// </summary>
public class ReanchorResult
{
    /// <summary>Number of connections whose endpoint(s) were moved to a same-named new pin.</summary>
    public int ReanchoredCount { get; init; }

    /// <summary>Connections referencing a pin name that no longer exists; must be removed by the caller.</summary>
    public IReadOnlyList<WaveguideConnection> DroppedConnections { get; init; } = Array.Empty<WaveguideConnection>();

    /// <summary>One user-facing warning per dropped connection.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Re-anchors waveguide connections after a component's <see cref="Component.PhysicalPins"/>
/// list was replaced by a per-instance Nazca override (issue #561). Endpoints that point at a
/// replaced pin object are moved to the new pin with the same name; if no same-named pin
/// exists, the connection is reported as dropped.
/// </summary>
public static class ConnectionPinReanchorService
{
    /// <summary>
    /// Walks <paramref name="connections"/> and re-anchors every endpoint that belongs to
    /// <paramref name="component"/> but is no longer in its pin list. Does not mutate the
    /// connection collection itself — dropped connections are only reported.
    /// </summary>
    public static ReanchorResult Reanchor(
        Component component, IReadOnlyList<WaveguideConnection> connections)
    {
        var currentPins = new HashSet<PhysicalPin>(component.PhysicalPins);
        var pinsByName = component.PhysicalPins
            .GroupBy(p => p.Name)
            .ToDictionary(g => g.Key, g => g.First());

        int reanchored = 0;
        var dropped = new List<WaveguideConnection>();
        var warnings = new List<string>();

        foreach (var conn in connections)
        {
            var staleStart = IsStaleEndpoint(conn.StartPin, component, currentPins);
            var staleEnd = IsStaleEndpoint(conn.EndPin, component, currentPins);
            if (!staleStart && !staleEnd)
                continue;

            var missingName = FindMissingPinName(conn, staleStart, staleEnd, pinsByName);
            if (missingName != null)
            {
                dropped.Add(conn);
                warnings.Add(
                    $"Connection {conn.StartPin.Name}–{conn.EndPin.Name} removed: " +
                    $"pin '{missingName}' no longer exists after the Nazca override of " +
                    $"'{component.Name}'.");
                continue;
            }

            if (staleStart)
                conn.StartPin = pinsByName[conn.StartPin.Name];
            if (staleEnd)
                conn.EndPin = pinsByName[conn.EndPin.Name];
            reanchored++;
        }

        return new ReanchorResult
        {
            ReanchoredCount = reanchored,
            DroppedConnections = dropped,
            Warnings = warnings,
        };
    }

    /// <summary>True when the pin belongs to the component but was replaced (not in its pin list anymore).</summary>
    private static bool IsStaleEndpoint(
        PhysicalPin pin, Component component, HashSet<PhysicalPin> currentPins)
        => pin.ParentComponent == component && !currentPins.Contains(pin);

    /// <summary>
    /// Returns the first stale endpoint pin name that has no same-named replacement,
    /// or null when every stale endpoint can be re-anchored.
    /// </summary>
    private static string? FindMissingPinName(
        WaveguideConnection conn, bool staleStart, bool staleEnd,
        IReadOnlyDictionary<string, PhysicalPin> pinsByName)
    {
        if (staleStart && !pinsByName.ContainsKey(conn.StartPin.Name))
            return conn.StartPin.Name;
        if (staleEnd && !pinsByName.ContainsKey(conn.EndPin.Name))
            return conn.EndPin.Name;
        return null;
    }
}
