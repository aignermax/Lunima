using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;

/// <summary>
/// One collapsible cell group of the Logic panel's Outputs list (issue #1399): the gate
/// outputs of the gates nested inside one top-level cell instance (hierarchical ids
/// <c>&lt;cell&gt;/&lt;gate&gt;.&lt;pin&gt;</c>, issue #1388) collapse under a single
/// header row — <c>CELL0 — 4 registers, 31 gates</c> — so a hierarchical design shows
/// its interface (named outputs and buses) first and the internals stay one click away,
/// NAND2TETRIS-style. Collapsed by default; expanding reveals the member rows with the
/// cell prefix stripped (<c>REG10.Y</c>), keeping the name column narrow. Flat designs
/// (no <c>/</c> in any tap name) get no cell groups and look exactly as before.
/// </summary>
public partial class LogicCellGroupViewModel : LogicOutputRowViewModel
{
    /// <summary>
    /// Groups the <paramref name="members"/> of one cell instance; their display names
    /// are stripped of the <paramref name="cellName"/> prefix. The header counts the
    /// non-register gates among the members; <paramref name="registerGateIds"/> carries
    /// the cell's register gate ids for the register count.
    /// </summary>
    public LogicCellGroupViewModel(
        string cellName,
        IReadOnlyList<LogicNetworkOutputViewModel> members,
        IReadOnlySet<string> registerGateIds)
    {
        CellName = cellName;
        Members = members;
        RegisterCount = registerGateIds.Count;
        GateCount = members
            .Select(m => GateIdOf(m.PinName))
            .Where(gateId => !registerGateIds.Contains(gateId))
            .Distinct()
            .Count();
        foreach (var member in members)
            member.DisplayName = member.PinName[(cellName.Length + 1)..];
        HeaderText = RegisterCount > 0
            ? string.Format(Translate("LogicPanel.CellGroup.HeaderGatesRegisters"), cellName, RegisterCount, GateCount)
            : string.Format(Translate("LogicPanel.CellGroup.HeaderGates"), cellName, GateCount);
    }

    /// <summary>The top-level cell instance name ("CELL0").</summary>
    public string CellName { get; }

    /// <summary>The member output rows, cell prefix stripped from their display names.</summary>
    public IReadOnlyList<LogicNetworkOutputViewModel> Members { get; }

    /// <summary>How many register gates the cell hides (shown in the header).</summary>
    public int RegisterCount { get; }

    /// <summary>How many non-register gates the cell hides (shown in the header).</summary>
    public int GateCount { get; }

    /// <summary>Header line, e.g. <c>CELL0 — 4 registers, 31 gates</c>.</summary>
    [ObservableProperty]
    private string _headerText = "";

    /// <summary>True while the cell's gate rows are visible — collapsed by default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronText))]
    private bool _isExpanded;

    /// <summary>The expand/collapse glyph of the header button.</summary>
    public string ChevronText => IsExpanded ? "▾" : "▸";

    /// <summary>Expands or collapses the cell's gate rows.</summary>
    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>The gate id of a hierarchical tap name: everything before the pin's dot.</summary>
    private static string GateIdOf(string tapName)
    {
        var dot = tapName.LastIndexOf('.');
        return dot < 0 ? tapName : tapName[..dot];
    }

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);
}
