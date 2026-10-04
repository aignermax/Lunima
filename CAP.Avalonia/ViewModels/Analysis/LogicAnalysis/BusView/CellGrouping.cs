namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;

/// <summary>
/// Groups the Logic panel's output rows per top-level cell instance (issue #1399):
/// every plain output row whose tap name is hierarchical (<c>&lt;cell&gt;/&lt;gate&gt;.
/// &lt;pin&gt;</c>, issue #1388) folds into one <see cref="LogicCellGroupViewModel"/>
/// per cell, inserted where the cell's first member stood. Named outputs (signal names
/// carry no <c>/</c>) and bus rows stay top-level rows, so the memory's interface reads
/// first and the internals collapse behind one header per cell. Rows of a flat design —
/// no hierarchical tap — pass through unchanged.
/// </summary>
public static class CellGrouping
{
    /// <summary>
    /// Replaces the hierarchical plain rows of <paramref name="rows"/> by one collapsed
    /// cell group per top-level cell. <paramref name="registerGatesByCell"/> carries the
    /// register gate ids per cell name for the header's register/gate counts.
    /// </summary>
    public static IReadOnlyList<LogicOutputRowViewModel> GroupByCell(
        IReadOnlyList<LogicOutputRowViewModel> rows,
        IReadOnlyDictionary<string, IReadOnlySet<string>> registerGatesByCell)
    {
        var membersByCell = new Dictionary<string, List<LogicNetworkOutputViewModel>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row is not LogicNetworkOutputViewModel output || !TryCellPrefix(output.PinName, out var prefix))
                continue;
            if (!membersByCell.TryGetValue(prefix, out var members))
            {
                members = new List<LogicNetworkOutputViewModel>();
                membersByCell[prefix] = members;
            }
            members.Add(output);
        }
        if (membersByCell.Count == 0)
            return rows;

        var pending = membersByCell.ToDictionary(
            pair => pair.Key,
            pair => new LogicCellGroupViewModel(
                pair.Key,
                pair.Value,
                registerGatesByCell.TryGetValue(pair.Key, out var gates)
                    ? gates
                    : new HashSet<string>(StringComparer.Ordinal)),
            StringComparer.Ordinal);
        var grouped = new List<LogicOutputRowViewModel>();
        foreach (var row in rows)
        {
            if (row is LogicNetworkOutputViewModel output && TryCellPrefix(output.PinName, out var prefix))
            {
                if (pending.Remove(prefix, out var group))
                    grouped.Add(group);
                continue;
            }
            grouped.Add(row);
        }
        return grouped;
    }

    /// <summary>
    /// Splits the top-level cell prefix off a hierarchical tap name (<c>CELL0</c> of
    /// <c>CELL0/REG10.Y</c>). Returns false for flat names — they stay top-level rows.
    /// </summary>
    public static bool TryCellPrefix(string tapName, out string prefix)
    {
        prefix = "";
        var slash = tapName.IndexOf('/');
        if (slash <= 0)
            return false;
        prefix = tapName[..slash];
        return true;
    }
}
