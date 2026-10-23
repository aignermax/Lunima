using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP.Avalonia.ViewModels.Canvas.Services;

namespace CAP.Avalonia.ViewModels.Canvas;

public partial class DesignCanvasViewModel
{
    // ── Connection management ─────────────────────────────────────────────

    public async Task<WaveguideConnectionViewModel?> ConnectPinsAsync(
        PhysicalPin startPin, PhysicalPin endPin)
    {
        if (!startPin.IsPlanarRoutable || !endPin.IsPlanarRoutable) return null;
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);
        var connection = ConnectionManager.AddConnectionDeferred(startPin, endPin);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);
        await RecalculateRoutesAsync();
        InvalidateSimulation();
        return vm;
    }

    /// <summary>Creates and routes under one document lease and externally revocable commit permission.</summary>
    public async Task<WaveguideConnectionViewModel?> ConnectPinsAsync(PhysicalPin startPin, PhysicalPin endPin,
        CancellationToken cancellationToken, CAP_Contracts.ICommitPermission permission)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, permission.CancellationToken);
        CAP_Core.Documents.DocumentOperation captured = null!;
        permission.Commit(() => captured = Document.CaptureOperation(linked.Token));
        await using var operation = captured;

        void Commit(Action change) => permission.Commit(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var suspended = Routing.SuspendAutomaticRouting();
            if (!operation.TryCommitAndAdvance(change))
                throw new OperationCanceledException("The connection operation no longer owns this document revision or scope.");
        });

        if (!startPin.IsPlanarRoutable || !endPin.IsPlanarRoutable) return null;
        WaveguideConnectionViewModel created = null!;
        Commit(() =>
        {
            RemoveConnectionsForPin(startPin);
            RemoveConnectionsForPin(endPin);
            var connection = ConnectionManager.AddConnectionDeferred(startPin, endPin);
            created = new WaveguideConnectionViewModel(connection);
            Connections.Add(created);
        });
        await Routing.RecalculateRoutesAsync(operation, permission, cancellationToken);
        Commit(InvalidateSimulation);
        return created;
    }

    public WaveguideConnectionViewModel? ConnectPins(PhysicalPin startPin, PhysicalPin endPin)
    {
        if (!startPin.IsPlanarRoutable || !endPin.IsPlanarRoutable) return null;
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);
        var connection = ConnectionManager.AddConnectionDeferred(startPin, endPin);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);
        InvalidateSimulation();
        return vm;
    }

    public WaveguideConnectionViewModel? ConnectPinsWithCachedRoute(
        PhysicalPin startPin, PhysicalPin endPin, RoutedPath cachedRoute)
    {
        if (!startPin.IsPlanarRoutable || !endPin.IsPlanarRoutable) return null;
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);
        var connection = ConnectionManager.AddConnectionWithCachedRoute(startPin, endPin, cachedRoute);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);
        return vm;
    }

    public void RemoveConnectionsForPin(PhysicalPin pin)
    {
        var connectionsToRemove = Connections
            .Where(c => c.Connection.StartPin == pin || c.Connection.EndPin == pin).ToList();
        foreach (var conn in connectionsToRemove)
        {
            ConnectionManager.RemoveConnectionDeferred(conn.Connection);
            Connections.Remove(conn);
        }
        if (connectionsToRemove.Count > 0) InvalidateSimulation();
    }

public WaveguideConnectionViewModel? GetConnectionForPin(PhysicalPin pin)
    => Connections.FirstOrDefault(c =>
        c.Connection.StartPin == pin || c.Connection.EndPin == pin);

    /// <summary>
    /// Re-anchors or drops waveguide connections after <paramref name="component"/>'s
    /// physical pins were replaced by a per-instance Nazca override (issue #561),
    /// refreshes the canvas pin view-models, re-routes and invalidates the simulation.
    /// Returns user-facing warnings for connections that had to be dropped.
    /// </summary>
    public IReadOnlyList<string> OnComponentPinsChanged(Component component)
    {
        var result = ConnectionPinReanchorService.Reanchor(
            component, ConnectionManager.Connections);

        foreach (var droppedConn in result.DroppedConnections)
        {
            ConnectionManager.RemoveConnectionDeferred(droppedConn);
            var droppedVm = Connections.FirstOrDefault(c => c.Connection == droppedConn);
            if (droppedVm != null) Connections.Remove(droppedVm);
        }

        RefreshPinViewModels(component);

        if (ConnectionManager.Connections.Count > 0) _ = RecalculateRoutesAsync();
        InvalidateSimulation();
        return result.Warnings;
    }

    /// <summary>
    /// Drops the pin view-models that still reference the component's replaced pins
    /// and re-creates them from the component's current <see cref="Component.PhysicalPins"/>.
    /// </summary>
    private void RefreshPinViewModels(Component component)
    {
        var staleVms = AllPins.Where(p => p.Pin.ParentComponent == component).ToList();
        foreach (var pinVm in staleVms) AllPins.Remove(pinVm);

        var compVm = Components.FirstOrDefault(c => c.Component == component);
        if (compVm == null) return;
        foreach (var pin in component.PhysicalPins)
            AllPins.Add(new PinViewModel(pin, compVm));
    }
}
