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
        var replacements = _footprints.Values.ToDictionary(footprint => footprint,
            footprint => footprint.WithOwner(Map(footprint.Owner)));
        var cells = _componentCells.Select(pair => (Owner: Map(pair.Key), Cells:
            pair.Value is ComponentFootprint footprint ? replacements[footprint] : pair.Value)).ToArray();
        _componentCells.Clear();
        foreach (var entry in cells) _componentCells.Add(entry.Owner, entry.Cells);
        var zones = _componentPinZones.Select(pair => (Owner: Map(pair.Key), Cells: pair.Value)).ToArray();
        _componentPinZones.Clear();
        foreach (var entry in zones) _componentPinZones.Add(entry.Owner, entry.Cells);
        _footprints.Clear();
        foreach (var footprint in replacements.Values) _footprints.Add(footprint.Owner, footprint);
        foreach (var bucket in _footprintBuckets.Values)
            for (var index = 0; index < bucket.Count; index++) bucket[index] = replacements[bucket[index]];
        var corridors = _pinCorridorCells.Select(pair => (Pin: MapPin(pair.Key), Cells: pair.Value)).ToArray();
        _pinCorridorCells.Clear();
        foreach (var entry in corridors) _pinCorridorCells.Add(entry.Pin, entry.Cells);
        var componentPins = _componentCorridorPins.Select(pair => (Owner: Map(pair.Key), Pins: pair.Value.Select(MapPin).ToList())).ToArray();
        _componentCorridorPins.Clear();
        foreach (var entry in componentPins) _componentCorridorPins.Add(entry.Owner, entry.Pins);
        OnWaveguideCellsAdded = null;
        OnAllWaveguidesCleared = null;
    }
}
