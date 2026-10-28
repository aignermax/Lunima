using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.MeanderGeneration;

namespace CAP.Avalonia.Gestures;

/// <summary>
/// Bend-handle drag on a length-matched connection. Resizing one bend would change the route
/// length and silently break the recorded target, so the handle sets the meander's bend radius
/// instead: the meander is regenerated at the new radius, at the same target length and clear
/// of other routes. The whole drag commits as one undoable <see cref="LengthMatchCommand"/>.
/// </summary>
internal sealed class MeanderRadiusDrag
{
    private const double MeaningfulChangeMicrometers = 1e-3;

    private readonly WaveguideConnectionViewModel _connection;
    private readonly DesignCanvasViewModel _canvas;
    private readonly ConnectionLengthMatcher _matcher;
    private readonly LengthMatchState _atPress;
    private readonly double _minRadiusMicrometers;
    private double _lastValidRadius;

    /// <summary>Starts a drag; <paramref name="minRadiusMicrometers"/> is the process floor for this connection.</summary>
    public MeanderRadiusDrag(WaveguideConnectionViewModel connection, DesignCanvasViewModel canvas, double minRadiusMicrometers)
    {
        _connection = connection;
        _canvas = canvas;
        _matcher = new ConnectionLengthMatcher(canvas.Router);
        _atPress = LengthMatchState.Capture(connection.Connection);
        _minRadiusMicrometers = Math.Max(minRadiusMicrometers, _matcher.ResolveProcessFloor(connection.Connection));
        _lastValidRadius = connection.Connection.BendRadiusMicrometers;
    }

    /// <summary>True when the connection's routes are governed by a length target.</summary>
    public static bool Applies(WaveguideConnectionViewModel connection) =>
        connection.Connection.TargetLengthMicrometers != null;

    /// <summary>Regenerates the meander at <paramref name="radius"/>; false keeps the last valid geometry.</summary>
    public bool TryApply(double radius)
    {
        if (radius < _minRadiusMicrometers)
            return false;
        if (Math.Abs(radius - _lastValidRadius) <= MeaningfulChangeMicrometers)
            return true;

        var connection = _connection.Connection;
        var result = _matcher.ApplyTargetLength(connection,
            _canvas.Components.Select(view => view.Component).ToList(),
            connection.TargetLengthMicrometers!.Value, connection.LengthToleranceMicrometers ?? 0.0,
            _canvas.ConnectionManager.Connections, radius);
        if (!result.IsSuccess)
            return false;
        _lastValidRadius = radius;
        _connection.NotifyPathChanged();
        return true;
    }

    /// <summary>The command recording the drag, or null when the radius did not change.</summary>
    public LengthMatchCommand? Complete(Action invalidate)
    {
        var after = LengthMatchState.Capture(_connection.Connection);
        if (Math.Abs(after.BendRadiusMicrometers - _atPress.BendRadiusMicrometers) <= MeaningfulChangeMicrometers)
            return null;
        return new LengthMatchCommand(_connection, _atPress, after, "Set meander bend radius", _ => invalidate());
    }

    /// <summary>Restores the state captured when the drag began.</summary>
    public void Revert()
    {
        _atPress.ApplyTo(_connection.Connection);
        _connection.NotifyPathChanged();
    }
}
