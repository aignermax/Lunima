using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for deleting multiple components as a single undoable operation.
/// Stores all affected components and their connections for undo.
/// </summary>
public class GroupDeleteCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly List<DeletedComponentData> _deletedComponents = new();

    // Locked components are skipped on Execute and must not be re-added on Undo,
    // otherwise they would end up on the canvas twice.
    private readonly List<DeletedComponentData> _removedComponents = new();

    /// <summary>
    /// Creates a group delete command for the given components.
    /// </summary>
    public GroupDeleteCommand(
        DesignCanvasViewModel canvas,
        IReadOnlyList<ComponentViewModel> components)
    {
        _canvas = canvas;

        // Snapshot each component and its connections before deletion
        foreach (var comp in components)
        {
            var scope = new ComponentWireScope(comp.Component);
            var connections = canvas.Connections
                .Where(c => scope.Touches(c.Connection))
                .Select(c => (c.Connection, c))
                .ToList();

            _deletedComponents.Add(new DeletedComponentData(
                comp,
                comp.Component,
                comp.TemplateName,
                comp.TemplatePdkSource,
                comp.X,
                comp.Y,
                connections));
        }
    }

    /// <inheritdoc />
    public string Description => $"Delete {_deletedComponents.Count} components";

    /// <inheritdoc />
    public void Execute()
    {
        _removedComponents.Clear();

        // Remove all components (DeleteComponent also removes their connections)
        var crossing = _canvas.ConnectionManager.CrossingService;
        foreach (var data in _deletedComponents)
        {
            // Skip locked components
            if (data.Component.IsLocked)
                continue;

            // Snapshot connections that still exist (may have been removed by prior deletion).
            // Crossing sub-connections are kept as their pre-split originals: deleting dissolves
            // the crossing, so restoring the subs would resurrect pins of a removed crossing.
            data.Connections.Clear();
            var scope = new ComponentWireScope(data.Component);
            if (!ChainCrossingMaintenance.IsTemporaryCrossing(data.Component))
                _canvas.DissolveInsertedCrossingChains(scope.Touches);
            foreach (var connVm in _canvas.Connections.ToList())
            {
                if (!scope.Touches(connVm.Connection)) continue;
                var original = crossing?.ResolveToOriginal(connVm.Connection) ?? connVm.Connection;
                if (data.Connections.All(c => c.Connection != original))
                    data.Connections.Add((original, connVm));
            }

            _canvas.DeleteComponent(data.ViewModel);
            _removedComponents.Add(data);
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        // Re-add only the components Execute actually removed (locked ones stayed put)
        foreach (var data in _removedComponents)
        {
            data.Component.PhysicalX = data.X;
            data.Component.PhysicalY = data.Y;
            data.ViewModel = _canvas.AddComponent(data.Component, data.TemplateName, data.TemplatePdkSource);
        }

        _canvas.RestoreConnections(_removedComponents.SelectMany(d => d.Connections.Select(c => c.Connection)));

        _ = _canvas.RecalculateRoutesAsync();
        _canvas.InvalidateSimulation();
    }

    /// <summary>
    /// Mutable data for a single deleted component.
    /// </summary>
    private sealed class DeletedComponentData
    {
        public ComponentViewModel ViewModel { get; set; }
        public Component Component { get; }
        public string? TemplateName { get; }
        public string? TemplatePdkSource { get; }
        public double X { get; }
        public double Y { get; }
        public List<(WaveguideConnection Connection, WaveguideConnectionViewModel Vm)> Connections { get; }

        public DeletedComponentData(
            ComponentViewModel viewModel,
            Component component,
            string? templateName,
            string? templatePdkSource,
            double x,
            double y,
            List<(WaveguideConnection, WaveguideConnectionViewModel)> connections)
        {
            ViewModel = viewModel;
            Component = component;
            TemplateName = templateName;
            TemplatePdkSource = templatePdkSource;
            X = x;
            Y = y;
            Connections = connections;
        }
    }
}
