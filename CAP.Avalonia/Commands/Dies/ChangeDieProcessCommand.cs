using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Analysis.Hierarchy;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>One reversible process change, including removal of incompatible leaves and their wiring.</summary>
public sealed class ChangeDieProcessCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _die;
    private readonly DieState _after;
    private readonly HashSet<Component> _removed;
    private readonly List<GroupSnapshot> _groups;
    private readonly List<ComponentViewModel> _rootParts;
    private readonly List<WaveguideConnection> _wires;
    private IUndoableCommand? _edit;
    private readonly bool _proxy;
    private readonly Dictionary<PhysicalPin, bool> _pinImpact = new();

    /// <summary>Snapshots the exact affected content before confirmation is applied.</summary>
    public ChangeDieProcessCommand(DesignCanvasViewModel canvas, ComponentGroup die, DieState after,
        IEnumerable<Component> incompatible)
    {
        _canvas = canvas;
        _die = die;
        _after = after;
        _removed = incompatible.ToHashSet();
        _proxy = canvas.ChipDie.IsProxy(die);
        if (!_proxy)
            _edit = UpdateDieCommand.TryCreate(canvas, die, after, out var refusal)
                ?? throw new InvalidOperationException(refusal ?? "The process did not change.");
        var roots = _proxy ? canvas.Components.Select(c => c.Component).ToList() : new List<Component> { die };
        _groups = roots.OfType<ComponentGroup>()
            .SelectMany(g => new[] { g }.Concat(g.GetAllComponentsRecursive().OfType<ComponentGroup>()))
            .Distinct().Select(g => new GroupSnapshot(g)).ToList();
        _rootParts = canvas.Components.Where(c => _removed.Contains(c.Component)).ToList();
        foreach (var snapshot in _groups)
        {
            foreach (var pin in snapshot.PhysicalPins) Owns(pin);
            foreach (var pin in snapshot.Pins) Owns(pin.InternalPin);
            foreach (var path in snapshot.Paths) Touches(path.StartPin, path.EndPin);
        }
        _wires = canvas.ConnectionManager.Connections.Where(c => Touches(c.StartPin, c.EndPin)).ToList();
    }

    /// <summary>The die after execution, including promotion of a legacy chip.</summary>
    public ComponentGroup ResultDie => (_edit as PromoteChipToDieCommand)?.CreatedDie ?? _die;
    /// <inheritdoc/>
    public string Description => "Change die process";
    /// <summary>Includes frozen wires inside nested groups and live die links.</summary>
    public int AffectedConnectionCount => _wires.Count + _groups.Sum(g => g.Paths.Count(p => Touches(p.StartPin, p.EndPin)));

    /// <summary>Endpoint names for reviewing every affected connection before applying.</summary>
    public IEnumerable<string> AffectedConnections => _wires.Select(w => Label(w.StartPin, w.EndPin))
        .Concat(_groups.SelectMany(g => g.Paths).Where(p => Touches(p.StartPin, p.EndPin))
            .Select(p => Label(p.StartPin, p.EndPin)));

    private static string Label(PhysicalPin? a, PhysicalPin? b) =>
        $"{a?.ParentComponent?.Identifier}.{a?.Name} → {b?.ParentComponent?.Identifier}.{b?.Name}";

    private bool Touches(PhysicalPin? start, PhysicalPin? end) =>
        Owns(start) | Owns(end);

    private bool Owns(PhysicalPin? pin)
    {
        if (pin == null) return false;
        if (!_pinImpact.TryGetValue(pin, out var affected))
            _pinImpact[pin] = affected = _removed.Contains(DesignHierarchy.ResolveToLeafPin(pin).ParentComponent);
        return affected;
    }

    /// <inheritdoc/>
    public void Execute()
    {
        foreach (var wire in _wires)
        {
            _canvas.ConnectionManager.RemoveConnectionDeferred(wire);
            var vm = _canvas.Connections.FirstOrDefault(c => c.Connection == wire);
            if (vm != null) _canvas.Connections.Remove(vm);
        }
        foreach (var snapshot in _groups.AsEnumerable().Reverse())
        {
            var group = snapshot.Group;
            foreach (var path in snapshot.Paths.Where(p => Touches(p.StartPin, p.EndPin))) group.RemoveInternalPath(path);
            foreach (var pin in snapshot.Pins.Where(p => Touches(p.InternalPin, null))) group.RemoveExternalPin(pin);
            foreach (var pin in snapshot.PhysicalPins.Where(Owns)) group.PhysicalPins.Remove(pin);
            group.RemoveChildren(snapshot.Children.Where(_removed.Contains).ToList());
        }
        foreach (var part in _rootParts) _canvas.DetachTopLevel(part);
        _edit ??= _proxy ? new PromoteChipToDieCommand(_canvas, _after)
            : UpdateDieCommand.TryCreate(_canvas, _die, _after, out _)!;
        _edit.Execute();
        Refresh();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        _edit!.Undo();
        foreach (var part in _rootParts) _canvas.AttachTopLevel(part);
        foreach (var snapshot in _groups.AsEnumerable().Reverse()) snapshot.Restore();
        _canvas.RestoreConnections(_wires);
        Refresh();
    }

    private void Refresh()
    {
        foreach (var group in _groups) _canvas.RefreshGroupInterface(group.Group);
        _canvas.RefreshDieInterfaces();
        _canvas.Surface.Prune();
        _canvas.InvalidateSimulation();
        _canvas.NotifyDiesChanged();
    }

    private sealed class GroupSnapshot(ComponentGroup group)
    {
        public ComponentGroup Group { get; } = group;
        public List<Component> Children { get; } = group.ChildComponents.ToList();
        public List<FrozenWaveguidePath> Paths { get; } = group.InternalPaths.ToList();
        public List<GroupPin> Pins { get; } = group.ExternalPins.ToList();
        public List<PhysicalPin> PhysicalPins { get; } = group.PhysicalPins.ToList();
        public void Restore()
        {
            Group.RemoveChildren(Group.ChildComponents.ToList());
            Group.AddChildren(Children);
            foreach (var path in Group.InternalPaths.ToList()) Group.RemoveInternalPath(path);
            Group.AddInternalPaths(Paths);
            foreach (var pin in Group.ExternalPins.ToList()) Group.RemoveExternalPin(pin);
            foreach (var pin in Pins) Group.AddExternalPin(pin);
            Group.PhysicalPins.Clear();
            Group.PhysicalPins.AddRange(PhysicalPins);
        }
    }
}
