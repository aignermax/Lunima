using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

public partial class PathfindingGrid
{
    /// <summary>
    /// Rebinds a completed, exclusively owned grid to accepted model identities. This touches bookkeeping
    /// entries only, never enumerates occupied cells or rasterizes geometry. Invoke before the short commit.
    /// </summary>
    internal void RebindOwners(IReadOnlyDictionary<Guid, Component> components)
    {
        Component Map(Component component) => components.TryGetValue(component.Id, out var mapped) ? mapped
            : throw new InvalidDataException($"Missing grid component '{component.Id}'.");
        var pins = components.Values.SelectMany(component => component.PhysicalPins).ToDictionary(pin => pin.PinId);
        PhysicalPin MapPin(PhysicalPin pin) => pins[pin.PinId];
        var replacements = _componentOwnership.PrepareReboundFootprints(Map);
        var cells = _componentCells.Select(pair => (Owner: Map(pair.Key), Cells:
            pair.Value is ComponentFootprint footprint ? replacements[footprint] : pair.Value)).ToArray();
        _componentCells.Clear();
        foreach (var entry in cells) _componentCells.Add(entry.Owner, entry.Cells);
        var zones = _componentPinZones.Select(pair => (Owner: Map(pair.Key), Cells: pair.Value)).ToArray();
        _componentPinZones.Clear();
        foreach (var entry in zones) _componentPinZones.Add(entry.Owner, entry.Cells);
        _componentOwnership.RebindOwners(replacements, Map, MapPin);
        OnWaveguideCellsAdded = null;
        OnAllWaveguidesCleared = null;
    }
}
