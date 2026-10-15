using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands.Ownership;

/// <summary>
/// The ownership step of drawing a wire: a wire between two parts of a group below the level
/// it was drawn on belongs to that group (their lowest common ancestor, see
/// <see cref="ConnectionOwnership"/>). Once the live wire has been routed it is stored in
/// the owner as frozen geometry — so it is there when the group is entered and moves with
/// it — and leaves the canvas. Wires owned by the current level, and wires on a die (a die
/// is a work surface edited in place), stay live. Undo/redo put it back exactly.
/// </summary>
public sealed class WireHomingStep
{
    private const int MaxRoutingPasses = 3;

    private readonly DesignCanvasViewModel _canvas;
    private GroupWireHome? _home;
    private bool _wanted;

    /// <summary>Creates the step for wires drawn on <paramref name="canvas"/>.</summary>
    public WireHomingStep(DesignCanvasViewModel canvas) => _canvas = canvas;

    /// <summary>True while the wire is stored in its owning group.</summary>
    public bool IsHomed => _home != null;

    /// <summary>True once the wire has been stored in its owner at least once (redo re-stores it).</summary>
    public bool HasHome { get; private set; }

    /// <summary>
    /// The group a wire between <paramref name="start"/> and <paramref name="end"/> must be
    /// stored in, or null when it stays on the current level.
    /// </summary>
    public static ComponentGroup? OwnerBelowScope(DesignCanvasViewModel canvas, PhysicalPin start, PhysicalPin end)
    {
        var owner = ConnectionOwnership.OwnerOf(start, end);
        if (owner == null || owner.IsDie || ReferenceEquals(owner, canvas.CurrentEditGroup))
            return null;
        return ConnectionOwnership.IsBelow(owner, canvas.CurrentEditGroup) ? owner : null;
    }

    /// <summary>
    /// Stores <paramref name="connection"/> in its owner once it is routed; completes at once
    /// when the wire stays on the current level. Cancelled by <see cref="Cancel"/>.
    /// </summary>
    public async Task HomeWhenRoutedAsync(WaveguideConnectionViewModel connection)
    {
        var owner = OwnerBelowScope(_canvas, connection.Connection.StartPin, connection.Connection.EndPin);
        if (owner == null)
            return;
        _wanted = true;
        for (int pass = 0; pass < MaxRoutingPasses && !IsRouted(connection); pass++)
            await _canvas.RecalculateRoutesAsync();
        if (_wanted && _canvas.Connections.Contains(connection))
            Home(connection, owner);
    }

    /// <summary>Stops a pending <see cref="HomeWhenRoutedAsync"/> (the wire was undone before it was routed).</summary>
    public void Cancel() => _wanted = false;

    /// <summary>Takes the stored wire out of its owner again and restores the owner's boundary pins.</summary>
    public void Unhome()
    {
        if (_home == null)
            return;
        var owner = _home.Owner;
        GroupWireHoming.Unhome(_home);
        _home = null;
        _canvas.RefreshGroupInterface(owner);
    }

    /// <summary>Re-stores the wire in its owner (redo).</summary>
    public void Rehome()
    {
        if (_home != null || _lastPath == null || _lastOwner == null)
            return;
        _home = GroupWireHoming.Home(_lastOwner, _lastPath);
        _canvas.RefreshGroupInterface(_lastOwner);
    }

    private FrozenWaveguidePath? _lastPath;
    private ComponentGroup? _lastOwner;

    private void Home(WaveguideConnectionViewModel connection, ComponentGroup owner)
    {
        var frozen = GroupWireHoming.Freeze(connection.Connection, owner);
        _canvas.ConnectionManager.RemoveConnectionDeferred(connection.Connection);
        _canvas.Connections.Remove(connection);
        _home = GroupWireHoming.Home(owner, frozen);
        _lastPath = frozen;
        _lastOwner = owner;
        HasHome = true;
        _canvas.RefreshGroupInterface(owner);
    }

    private static bool IsRouted(WaveguideConnectionViewModel connection) =>
        connection.Connection.RoutedPath is { Segments.Count: > 0 };
}
