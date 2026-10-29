using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Routing.MeanderGeneration;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// What can be done with a length lock (a recorded length target): find the routes locked to
/// the same length (the group equalized together), tell whether a route still holds its length,
/// re-equalize the whole group, or release the lock — as one undoable step.
/// </summary>
public sealed class LengthLockActions
{
    /// <summary>Targets equal to this many decimals (µm) belong to one lock group.</summary>
    private const int TargetDecimals = 3;

    private readonly DesignCanvasViewModel _canvas;
    private readonly LengthEqualizationViewModel _equalize;

    /// <summary>Creates the actions for one canvas and its "Equalize lengths" section.</summary>
    public LengthLockActions(DesignCanvasViewModel canvas, LengthEqualizationViewModel equalize)
    {
        _canvas = canvas;
        _equalize = equalize;
    }

    /// <summary>Every route locked to the same length as <paramref name="connection"/>, itself included.</summary>
    public IReadOnlyList<WaveguideConnectionViewModel> GroupOf(WaveguideConnectionViewModel connection)
    {
        if (Key(connection) is not { } key)
            return Array.Empty<WaveguideConnectionViewModel>();
        return _canvas.Connections.Where(c => Key(c) == key).ToList();
    }

    /// <summary>True when the route has a length lock it no longer meets (an edit moved its pins).</summary>
    public static bool IsViolated(WaveguideConnectionViewModel connection) =>
        connection.Connection.TargetLengthMicrometers != null && !ConnectionLengthMatcher.MeetsTarget(connection.Connection);

    /// <summary>One line on the lock: the locked length and group size, or what is wrong.</summary>
    public string Describe(WaveguideConnectionViewModel connection)
    {
        double target = connection.Connection.TargetLengthMicrometers ?? 0;
        return IsViolated(connection)
            ? Format("Routing.LengthLock.Violated", target, connection.Connection.PathLengthMicrometers)
            : Format("Routing.LengthLock.Info", target, GroupOf(connection).Count);
    }

    /// <summary>
    /// Selects the route's whole lock group and equalizes it again at the shortest common length —
    /// the fix after an edit made the old length unreachable.
    /// </summary>
    public async Task ReEqualizeAsync(WaveguideConnectionViewModel connection)
    {
        var group = GroupOf(connection);
        _canvas.Selection.ClearConnectionSelection();
        foreach (var member in group)
        {
            _canvas.Selection.SelectedConnections.Add(member);
            member.IsSelected = true;
        }
        _equalize.TargetLengthText = "";
        await _equalize.EqualizeCommand.ExecuteAsync(null);
    }

    /// <summary>Drops the length lock of each route, keeping its shape; one undo step. Returns how many were released.</summary>
    public int Release(IEnumerable<WaveguideConnectionViewModel> connections)
    {
        var steps = connections.Where(c => c.Connection.TargetLengthMicrometers != null).Distinct().Select(c =>
        {
            var before = LengthMatchState.Capture(c.Connection);
            return (IUndoableCommand)new LengthMatchCommand(c, before,
                before with { TargetLengthMicrometers = null, ToleranceMicrometers = null }, "Release length lock");
        }).ToList();
        if (steps.Count == 0)
            return 0;
        var command = new CompositeCommand($"Release length lock ({steps.Count})", steps);
        if (_canvas.CommandManager is { } commands)
            commands.ExecuteCommand(command);
        else
            command.Execute();
        return steps.Count;
    }

    private static double? Key(WaveguideConnectionViewModel connection) =>
        connection.Connection.TargetLengthMicrometers is { } target ? Math.Round(target, TargetDecimals) : null;

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance.Translate(key), arguments);
}
