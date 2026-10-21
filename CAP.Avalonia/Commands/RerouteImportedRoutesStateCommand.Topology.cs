using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Commands;

public sealed partial class RerouteImportedRoutesStateCommand
{
    private readonly Dictionary<ComponentGroup, List<CrossingChange>> _groupChanges = new();

    internal ComponentGroup? RoutingGroup { get; set; }

    internal void RecordCrossingChange(CrossingChange change)
    {
        if (RoutingGroup is not { } group)
        {
            RootTopology?.Record(change);
            return;
        }
        if (!_groupChanges.TryGetValue(group, out var changes))
            _groupChanges[group] = changes = new();
        changes.Add(change);
    }

    internal void SealGroupTopology()
    {
        foreach (var state in _groups)
        {
            var changes = _groupChanges.GetValueOrDefault(state.Group) ?? new();
            var ownedComponents = changes.SelectMany(change => change.Added.Concat(change.Removed)).ToHashSet();
            var removedComponents = changes.SelectMany(change => change.Removed).ToHashSet();
            var actualChildren = (state.NewChildren ?? []).ToHashSet();
            state.NewChildren = (state.OldChildren ?? [])
                .Where(child => !ownedComponents.Contains(child) || actualChildren.Contains(child))
                .Concat(actualChildren.Where(child => ownedComponents.Contains(child)))
                .Distinct().ToArray();
            var addedKeys = changes.SelectMany(change => change.AddedConnections)
                .Select(connection => (connection.StartPin, connection.EndPin, Guid.Empty)).ToHashSet();
            var touchedPins = changes.SelectMany(change => change.AddedConnections)
                .SelectMany(connection => new[] { connection.StartPin, connection.EndPin }).ToHashSet();
            bool CrossingAffected(FrozenWaveguidePath path) =>
                (path.StartPin != null && path.EndPin != null
                    && touchedPins.Contains(path.StartPin) && touchedPins.Contains(path.EndPin))
                || (path.StartPin?.ParentComponent is { } start && removedComponents.Contains(start))
                || (path.EndPin?.ParentComponent is { } end && removedComponents.Contains(end));
            var requested = state.TargetPins?.ToHashSet();
            state.OldPaths = state.OldPaths.Where(path => requested == null
                || requested.Contains((path.StartPin, path.EndPin))
                || CrossingAffected(path)).ToList();
            var oldKeys = state.OldPaths.Select(PathKey).ToHashSet();
            var build = state.CreateNewPaths;
            state.CreateNewPaths = () =>
            {
                var paths = build().Where(path => oldKeys.Contains(PathKey(path)) || addedKeys.Contains(PathKey(path))).ToList();
                var keys = paths.Select(PathKey).ToHashSet();
                paths.AddRange(state.OldPaths.Where(path => !keys.Contains(PathKey(path))
                    && !CrossingAffected(path))
                    .Select(CloneWithPins));
                return paths;
            };
        }
    }

    private static (PhysicalPin?, PhysicalPin?, Guid) PathKey(FrozenWaveguidePath path) =>
        (path.StartPin, path.EndPin, path.StartPin == null || path.EndPin == null ? path.PathId : Guid.Empty);

    private void ApplyGroup(GroupState state, bool useNew)
    {
        _canvas.Groups.SaveEditedGroup(state.Group);
        var newPaths = state.CreateNewPaths();
        var paths = useNew ? newPaths : state.OldPaths.Select(CloneWithPins).ToList();
        var oldChildren = state.OldChildren ?? [];
        var newChildren = state.NewChildren ?? [];
        var remove = useNew ? oldChildren.Except(newChildren) : newChildren.Except(oldChildren);
        var add = useNew ? newChildren.Except(oldChildren) : oldChildren.Except(newChildren);
        state.Group.RemoveChildren(remove.ToList());
        state.Group.AddChildren(add.Where(child => !state.Group.ChildComponents.Contains(child)).ToList());
        var ownedKeys = state.OldPaths.Concat(newPaths).Select(PathKey).ToHashSet();
        var independentPaths = state.Group.InternalPaths.Where(path => !ownedKeys.Contains(PathKey(path))).ToList();
        state.Group.InternalPaths.Clear();
        state.Group.AddInternalPaths(paths.Concat(independentPaths).ToList());
        _canvas.Groups.RefreshEditedGroup(state.Group);
    }
}
