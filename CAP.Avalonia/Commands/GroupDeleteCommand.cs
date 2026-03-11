using CAP.Avalonia.ViewModels;
using CAP_Core.Components;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for deleting multiple components as a single undoable operation.
/// Stores all affected components and their connections for undo.
/// </summary>
public class GroupDeleteCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly List<DeletedComponentData> _deletedComponents = new();

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
            var connections = canvas.Connections
                .Where(c => c.Connection.StartPin.ParentComponent == comp.Component ||
                            c.Connection.EndPin.ParentComponent == comp.Component)
                .Select(c => (c.Connection, c))
                .ToList();

            _deletedComponents.Add(new DeletedComponentData(
                comp,
                comp.Component,
                comp.TemplateName,
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
        // Remove all components (RemoveComponent also removes their connections)
        foreach (var data in _deletedComponents)
        {
            // Skip locked components
            if (data.Component.IsLocked)
                continue;

            // Snapshot connections that still exist (may have been removed by prior deletion)
            data.Connections.Clear();
            foreach (var connVm in _canvas.Connections.ToList())
            {
                if (connVm.Connection.StartPin.ParentComponent == data.Component ||
                    connVm.Connection.EndPin.ParentComponent == data.Component)
                {
                    data.Connections.Add((connVm.Connection, connVm));
                }
            }

            _canvas.RemoveComponent(data.ViewModel);
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        // Re-add components in original order
        foreach (var data in _deletedComponents)
        {
            data.Component.PhysicalX = data.X;
            data.Component.PhysicalY = data.Y;
            data.ViewModel = _canvas.AddComponent(data.Component, data.TemplateName);
        }

        // Re-add connections (deduplicate by connection identity)
        var restoredConnections = new HashSet<WaveguideConnection>();
        foreach (var data in _deletedComponents)
        {
            foreach (var (connection, _) in data.Connections)
            {
                if (restoredConnections.Add(connection))
                {
                    _canvas.ConnectionManager.AddExistingConnection(connection);
                    var connVm = new WaveguideConnectionViewModel(connection);
                    _canvas.Connections.Add(connVm);
                }
            }
        }

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
        public double X { get; }
        public double Y { get; }
        public List<(WaveguideConnection Connection, WaveguideConnectionViewModel Vm)> Connections { get; }

        public DeletedComponentData(
            ComponentViewModel viewModel,
            Component component,
            string? templateName,
            double x,
            double y,
            List<(WaveguideConnection, WaveguideConnectionViewModel)> connections)
        {
            ViewModel = viewModel;
            Component = component;
            TemplateName = templateName;
            X = x;
            Y = y;
            Connections = connections;
        }
    }
}
