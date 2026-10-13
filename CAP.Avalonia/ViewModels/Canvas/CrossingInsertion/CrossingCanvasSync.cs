using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;

/// <summary>
/// Keeps the canvas view-model collections in step with crossings the routing layer placed
/// or took out: the crossing's component and pin view-models, and the connection
/// view-models of the pieces and merged wires. Every operation is idempotent, so the
/// binder's notifications and a command's own sync may both run.
/// </summary>
public static class CrossingCanvasSync
{
    /// <summary>
    /// Adds the crossing's component and pin view-models. The grid obstacle is the routing
    /// layer's business, so this never goes through <see cref="DesignCanvasViewModel.AddComponent"/>.
    /// </summary>
    public static void AddCrossing(DesignCanvasViewModel canvas, Component crossing, string? templateName, string? templatePdkSource)
    {
        if (canvas.Components.Any(vm => vm.Component == crossing)) return;

        var vm = new ComponentViewModel(crossing, templateName, templatePdkSource);
        canvas.Components.Add(vm);
        foreach (var pin in crossing.PhysicalPins)
            canvas.AllPins.Add(new PinViewModel(pin, vm));
    }

    /// <summary>Removes the crossing's component and pin view-models; returns the removed component view-model.</summary>
    public static ComponentViewModel? RemoveCrossing(DesignCanvasViewModel canvas, Component crossing)
    {
        var vm = canvas.Components.FirstOrDefault(c => c.Component == crossing);
        if (vm == null) return null;

        foreach (var pin in canvas.AllPins.Where(p => p.ParentComponentViewModel == vm).ToList())
            canvas.AllPins.Remove(pin);
        canvas.Components.Remove(vm);
        return vm;
    }

    /// <summary>
    /// Reconciles the connection view-models with the manager's connections: pieces and
    /// split originals that are gone disappear, merged wires and new pieces appear.
    /// </summary>
    public static void SyncConnections(DesignCanvasViewModel canvas, IReadOnlyCollection<WaveguideConnection> managedConnections)
    {
        var managed = managedConnections.ToHashSet();
        foreach (var vm in canvas.Connections.Where(vm => !managed.Contains(vm.Connection)).ToList())
            canvas.Connections.Remove(vm);

        var wrapped = canvas.Connections.Select(vm => vm.Connection).ToHashSet();
        foreach (var connection in managedConnections.Where(c => !wrapped.Contains(c)))
            canvas.Connections.Add(new WaveguideConnectionViewModel(connection));
    }

    /// <summary>Removes the crossings' view-models and re-syncs the connection view-models from the manager.</summary>
    public static void ApplyRemoval(DesignCanvasViewModel canvas, IEnumerable<Component> removedCrossings)
    {
        foreach (var crossing in removedCrossings)
            RemoveCrossing(canvas, crossing);
        SyncConnections(canvas, SnapshotConnections(canvas));
    }

    /// <summary>The manager's connections, copied under its lock.</summary>
    public static List<WaveguideConnection> SnapshotConnections(DesignCanvasViewModel canvas)
    {
        lock (canvas.ConnectionManager.SyncRoot)
            return canvas.ConnectionManager.Connections.ToList();
    }
}
