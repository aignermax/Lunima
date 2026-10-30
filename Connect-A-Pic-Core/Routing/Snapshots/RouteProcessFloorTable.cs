using System.Collections.Immutable;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Worker-owned endpoint aliases and captured scalar answers, including ports of future crossings.</summary>
internal sealed class RouteProcessFloorTable
{
    private readonly Dictionary<(Guid, Guid), double> _values;
    private readonly Dictionary<Guid, Guid> _prototypePins = new();

    internal RouteProcessFloorTable(ImmutableArray<RouteConnectionValue> wires,
        ImmutableDictionary<(Guid, Guid), double>? prototypeFloors)
    {
        _values = prototypeFloors?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new();
        foreach (var wire in wires)
        {
            _values[(wire.Start, wire.End)] = wire.ProcessFloor;
            _values[(wire.End, wire.Start)] = wire.ProcessFloor;
        }
    }

    internal void Include(ImmutableDictionary<(Guid, Guid), double> values)
    {
        foreach (var pair in values) _values[pair.Key] = pair.Value;
    }

    internal void RegisterPrototypePin(Guid newId, Guid prototypeId) => _prototypePins.Add(newId, prototypeId);

    internal double? Resolve(PhysicalPin start, PhysicalPin end)
    {
        var first = _prototypePins.GetValueOrDefault(start.PinId, start.PinId);
        var second = _prototypePins.GetValueOrDefault(end.PinId, end.PinId);
        return _values.TryGetValue((first, second), out var floor) ? floor : null;
    }

    internal static ImmutableDictionary<(Guid, Guid), double> CapturePrototypePairs(Component prototype,
        IEnumerable<Component> components, WaveguideRouter router)
    {
        var values = ImmutableDictionary.CreateBuilder<(Guid, Guid), double>();
        var knownPins = components.SelectMany(component => component.PhysicalPins)
            .Concat(prototype.PhysicalPins).DistinctBy(pin => pin.PinId).ToArray();
        foreach (var port in prototype.PhysicalPins)
        foreach (var pin in knownPins)
        {
            values[(port.PinId, pin.PinId)] = router.ResolveProcessFloorFor(port, pin);
            values[(pin.PinId, port.PinId)] = router.ResolveProcessFloorFor(pin, port);
        }
        return values.ToImmutable();
    }
}
