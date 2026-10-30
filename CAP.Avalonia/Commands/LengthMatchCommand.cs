using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Undoable change of a connection's length-matching state: applying a target (meander),
/// changing the meander's bend radius, or clearing the target. Both states are captured in
/// full, so Ctrl+Z restores the exact previous route — including the router's route a
/// meander replaced.
/// </summary>
public sealed class LengthMatchCommand : IUndoableCommand
{
    private readonly WaveguideConnectionViewModel _connection;
    private readonly LengthMatchState _before;
    private readonly LengthMatchState _after;
    private readonly Action<bool>? _afterApply;

    /// <summary>Initializes a new instance of <see cref="LengthMatchCommand"/>.</summary>
    /// <param name="connection">The connection whose length matching changed.</param>
    /// <param name="before">State restored on undo.</param>
    /// <param name="after">State applied on execute.</param>
    /// <param name="description">Undo-history label.</param>
    /// <param name="afterApply">
    /// Runs after each apply; its argument is true when the applied state has no route, so the
    /// router has to rebuild one.
    /// </param>
    public LengthMatchCommand(WaveguideConnectionViewModel connection, LengthMatchState before,
        LengthMatchState after, string description, Action<bool>? afterApply = null)
    {
        _connection = connection;
        _before = before;
        _after = after;
        Description = description;
        _afterApply = afterApply;
    }

    /// <inheritdoc/>
    public string Description { get; }

    /// <inheritdoc/>
    public void Execute() => Apply(_after);

    /// <inheritdoc/>
    public void Undo() => Apply(_before);

    private void Apply(LengthMatchState state)
    {
        state.ApplyTo(_connection.Connection);
        _connection.NotifyPathChanged();
        _afterApply?.Invoke(state.Path == null);
    }
}

/// <summary>
/// Everything length matching changes on a <see cref="WaveguideConnection"/>. The path is a
/// private deep copy, so later edits of the live route cannot alter a recorded state.
/// </summary>
/// <param name="Path">Route geometry; null means "let the router rebuild it".</param>
/// <param name="IsRouteFrozen">Whether routing passes keep the geometry.</param>
/// <param name="TargetLengthMicrometers">Recorded length target.</param>
/// <param name="ToleranceMicrometers">Recorded tolerance.</param>
/// <param name="BendRadiusMicrometers">The connection's bend radius.</param>
/// <param name="WidthMicrometers">The waveguide width; null leaves it as it is.</param>
public sealed record LengthMatchState(RoutedPath? Path, bool IsRouteFrozen, double? TargetLengthMicrometers,
    double? ToleranceMicrometers, double BendRadiusMicrometers, double? WidthMicrometers = null)
{
    /// <summary>Captures the connection's current state.</summary>
    public static LengthMatchState Capture(WaveguideConnection connection) => new(
        connection.RoutedPath?.DeepCopy(), connection.IsRouteFrozen, connection.TargetLengthMicrometers,
        connection.LengthToleranceMicrometers, connection.BendRadiusMicrometers, connection.WidthMicrometers);

    /// <summary>
    /// The state after clearing the target: no route, unfrozen, no target, default bend radius.
    /// The radius goes back too — a meander's radius (e.g. an equalizer minimum of 250 µm) would
    /// otherwise make the router fail every later route of the connection.
    /// </summary>
    public LengthMatchState Cleared() => this with
    {
        Path = null, IsRouteFrozen = false, TargetLengthMicrometers = null, ToleranceMicrometers = null,
        BendRadiusMicrometers = WaveguideConnection.DefaultBendRadiusMicrometers,
    };

    /// <summary>Writes this state onto the connection.</summary>
    public void ApplyTo(WaveguideConnection connection)
    {
        connection.TargetLengthMicrometers = TargetLengthMicrometers;
        connection.LengthToleranceMicrometers = ToleranceMicrometers;
        connection.BendRadiusMicrometers = BendRadiusMicrometers;
        if (WidthMicrometers is { } width)
            connection.WidthMicrometers = width;
        connection.IsRouteFrozen = IsRouteFrozen;
        if (Path == null)
            connection.InvalidateRoute();
        else
            connection.ReplaceRoutedPath(Path.DeepCopy());
    }
}
