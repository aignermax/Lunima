using CAP.Avalonia.Commands;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// A new "Equalize" replaces the old length locks of the selected routes: their meanders, targets
/// and bend radii are dropped before the run, so the new radius and length apply to every route
/// instead of only to the ones that were not locked yet. The pre-run states are kept, so the whole
/// replacement records as one undo step — or is put back when the run does not happen.
/// </summary>
internal sealed class LengthLockReplacement
{
    private readonly Dictionary<WaveguideConnectionViewModel, LengthMatchState> _locked = new();

    /// <summary>True when at least one selected route had a lock that was dropped.</summary>
    public bool HasReleased => _locked.Count > 0;

    /// <summary>Drops the lock of every locked route in <paramref name="targets"/>, without recording undo.</summary>
    public static LengthLockReplacement Release(IEnumerable<WaveguideConnectionViewModel> targets)
    {
        var replacement = new LengthLockReplacement();
        foreach (var vm in targets.Where(t => t.Connection.TargetLengthMicrometers != null).Distinct())
        {
            var before = LengthMatchState.Capture(vm.Connection);
            replacement._locked[vm] = before;
            before.Cleared().ApplyTo(vm.Connection);
            vm.NotifyPathChanged();
        }
        return replacement;
    }

    /// <summary>The state an undo of the run must restore: the old lock when there was one.</summary>
    public LengthMatchState BeforeOf(WaveguideConnectionViewModel connection) =>
        _locked.TryGetValue(connection, out var locked) ? locked : LengthMatchState.Capture(connection.Connection);

    /// <summary>
    /// Undo steps for released routes the run did not record (failed or skipped), so undo still
    /// brings their old lock back.
    /// </summary>
    public IEnumerable<IUndoableCommand> UnrecordedSteps(IReadOnlySet<WaveguideConnectionViewModel> recorded) =>
        _locked.Where(entry => !recorded.Contains(entry.Key)).Select(entry => (IUndoableCommand)new LengthMatchCommand(
            entry.Key, entry.Value, LengthMatchState.Capture(entry.Key.Connection), "Release length lock"));

    /// <summary>Puts every old lock back unchanged (the run was rejected or failed before applying).</summary>
    public void Restore()
    {
        foreach (var (vm, locked) in _locked)
        {
            locked.ApplyTo(vm.Connection);
            vm.NotifyPathChanged();
        }
        _locked.Clear();
    }
}
