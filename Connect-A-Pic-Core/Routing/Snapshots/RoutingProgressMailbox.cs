using System.Collections.Concurrent;
using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation.BendLoss;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Coalesces immutable wire deltas and scalar progress; it never captures a UI callback.</summary>
public sealed class RoutingProgressMailbox
{
    private readonly ConcurrentDictionary<Guid, ComputedRouteValue> _pending = new();
    private readonly Dictionary<Guid, (RoutedPath? Path, ConnectionLossBreakdown Loss, bool Frozen)> _seen = new();
    private long _lastCapture;
    private int _routed;
    private int _phase;
    private int _complex;
    public int RoutedCount => Volatile.Read(ref _routed);
    public RoutingPhase Phase => (RoutingPhase)Volatile.Read(ref _phase);
    public bool Complex => Volatile.Read(ref _complex) != 0;

    internal void Initialize(IEnumerable<WaveguideConnection> wires)
    {
        foreach (var wire in wires) _seen[wire.Id] = (wire.RoutedPath, wire.LossBreakdown, wire.IsRouteFrozen);
    }
    internal void PhaseChanged(RoutingPhase phase) { Volatile.Write(ref _phase, (int)phase); Volatile.Write(ref _complex, 0); }
    internal void ComplexStarted() => Volatile.Write(ref _complex, 1);
    internal void Completed(RoutingWorkGraph graph)
    {
        Interlocked.Increment(ref _routed);
        Volatile.Write(ref _complex, 0);
        var now = Environment.TickCount64;
        if (now - _lastCapture < 100 || graph.CrossingChanges.Count != 0) return;
        _lastCapture = now;
        foreach (var wire in graph.Manager.Connections)
        {
            // A topology transaction never escapes piecemeal. New wires wait for final publication.
            if (!_seen.TryGetValue(wire.Id, out var previous)) continue;
            if (ReferenceEquals(previous.Path, wire.RoutedPath) && ReferenceEquals(previous.Loss, wire.LossBreakdown)
                && previous.Frozen == wire.IsRouteFrozen) continue;
            _pending[wire.Id] = ComputedRouteValue.Capture(wire);
            _seen[wire.Id] = (wire.RoutedPath, wire.LossBreakdown, wire.IsRouteFrozen);
        }
    }

    public RoutingFrame TakeFrame()
    {
        var values = ImmutableArray.CreateBuilder<ComputedRouteValue>();
        foreach (var pair in _pending.ToArray())
            if (((ICollection<KeyValuePair<Guid, ComputedRouteValue>>)_pending).Remove(pair)) values.Add(pair.Value);
        return new(values.ToImmutable());
    }
}
