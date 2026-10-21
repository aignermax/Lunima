using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Single undoable step that records the before/after state of a "Re-route imported
/// routes" pass. The initial routing is performed asynchronously by the ViewModel;
/// this command is pushed afterwards so one Ctrl+Z restores both canvas connections
/// and group-internal frozen paths to their exact pre-reroute geometry.
/// </summary>
public sealed partial class RerouteImportedRoutesStateCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly IReadOnlyList<ConnectionState> _connections;
    private readonly IReadOnlyList<GroupState> _groups;
    internal RerouteImportedRootTopology? RootTopology { get; set; }
    private bool _recordingInitialState = true;

    /// <summary>Mutable state of one canvas-level connection.</summary>
    public sealed class ConnectionState
    {
        /// <summary>The connection whose geometry is swapped.</summary>
        public required WaveguideConnection Connection { get; set; }

        /// <summary>Frozen geometry before the re-route.</summary>
        public required RoutedPath OldPath { get; set; }

        /// <summary>Freeze flag before the re-route.</summary>
        public required bool OldIsFrozen { get; set; }

        /// <summary>Bend-radius setting before interpreting the imported shape.</summary>
        public double OldBendRadius { get; set; }

        /// <summary>Drawn polygons of the imported route before the re-route, or null.</summary>
        public AsDrawnGeometry? OldAsDrawn { get; init; }

        /// <summary>Builds a private redo path from detached scalar geometry when needed.</summary>
        public Func<RoutedPath> CreateNewPath { get; set; } = null!;

        /// <summary>Freeze flag after the re-route.</summary>
        public bool NewIsFrozen { get; set; }

        /// <summary>Bend-radius setting used for the replacement route.</summary>
        public double NewBendRadius { get; set; }

        /// <summary>Exact source polygons retained when no replacement route was possible.</summary>
        public AsDrawnGeometry? NewAsDrawn { get; set; }
    }

    /// <summary>Mutable state of one component group's internal frozen paths.</summary>
    public sealed class GroupState
    {
        /// <summary>The group whose internal paths are swapped.</summary>
        public required ComponentGroup Group { get; set; }

        /// <summary>Pin pairs explicitly requested for conversion; other routes remain independently editable.</summary>
        public IReadOnlyCollection<(PhysicalPin? Start, PhysicalPin? End)>? TargetPins { get; init; }

        /// <summary>All internal paths before the re-route.</summary>
        public required IReadOnlyList<FrozenWaveguidePath> OldPaths { get; set; }

        /// <summary>Child membership before conversion, including pre-existing crossings.</summary>
        public IReadOnlyList<Component>? OldChildren { get; set; }

        /// <summary>Child membership after conversion.</summary>
        public IReadOnlyList<Component>? NewChildren { get; set; }

        /// <summary>Builds private redo paths from detached scalar geometry when needed.</summary>
        public Func<IReadOnlyList<FrozenWaveguidePath>> CreateNewPaths { get; set; } = null!;

        /// <summary>Count available without materializing the deferred redo paths.</summary>
        public int NewPathCount { get; set; }
    }

    /// <summary>Records the state change; the caller already performed the mutation.</summary>
    public RerouteImportedRoutesStateCommand(
        DesignCanvasViewModel canvas,
        IEnumerable<ConnectionState> connections,
        IEnumerable<GroupState> groups)
    {
        _canvas = canvas;
        _connections = connections as IReadOnlyList<ConnectionState> ?? connections.ToList();
        _groups = groups as IReadOnlyList<GroupState> ?? groups.ToList();
    }

    /// <inheritdoc/>
    public string Description
    {
        get
        {
            var count = Math.Max(_connections.Count, RootTopology?.ReplacedCount ?? 0) + _groups.Sum(g => g.NewPathCount);
            return count == 1
                ? "Re-route imported route"
                : $"Re-route {count} imported routes";
        }
    }

    /// <inheritdoc/>
    public void Execute()
    {
        if (_recordingInitialState)
        {
            _recordingInitialState = false;
            return;
        }
        Apply(useNew: true);
    }

    /// <inheritdoc/>
    public void Undo() => Apply(useNew: false);

    private void Apply(bool useNew)
    {
        RootTopology?.Apply(useNew);
        foreach (var state in _connections)
        {
            var path = useNew ? state.CreateNewPath() : state.OldPath.DeepCopy();
            var frozen = useNew ? state.NewIsFrozen : state.OldIsFrozen;
            state.Connection.RestoreCachedPath(path);
            state.Connection.BendRadiusMicrometers = useNew ? state.NewBendRadius : state.OldBendRadius;
            state.Connection.IsRouteFrozen = frozen;
            // A blocked reroute retains its original mask on redo as well as undo.
            if ((useNew ? state.NewAsDrawn : state.OldAsDrawn) is { } asDrawn)
                state.Connection.AttachAsDrawnGeometry(asDrawn);
        }

        foreach (var state in _groups)
            ApplyGroup(state, useNew);

        if (_canvas.ConnectionManager.CrossingService is { } crossings)
        {
            crossings.Reset();
            CAP_Core.Routing.CrossingInsertion.CrossingRecordRebuilder.Rebuild(
                crossings, _canvas.ConnectionManager, _canvas.Components.Select(c => c.Component));
        }
        _canvas.InvalidateSimulation();
        _ = _canvas.RecalculateRoutesAsync();
    }

    /// <summary>
    /// Deep-clones a frozen path while preserving its pin references, so a restored
    /// group path still anchors to the correct child-component pins.
    /// </summary>
    public static FrozenWaveguidePath CloneWithPins(FrozenWaveguidePath path)
    {
        var clone = new FrozenWaveguidePath
        {
            Path = path.Path.DeepCopy(),
            PathId = path.PathId,
            StartPin = path.StartPin,
            EndPin = path.EndPin
        };
        clone.CopySettingsFrom(path);
        return clone;
    }
}
