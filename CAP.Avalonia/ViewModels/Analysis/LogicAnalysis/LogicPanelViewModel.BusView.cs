using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>
/// Bus-view half of <see cref="LogicPanelViewModel"/> (issue #1068, NAND game rung 5):
/// turns the flat <see cref="Inputs"/>/<see cref="Outputs"/> lists into grouped rows —
/// indexed signal families (<c>A0</c>–<c>A3</c>) collapse into bus header rows showing
/// the decimal value, everything else stays a plain row. Purely display-level: the
/// network, the timeline, and the canvas badges keep reading the flat lists.
/// </summary>
public partial class LogicPanelViewModel
{
    /// <summary>The Inputs list as grouped display rows: bus headers and plain toggles.</summary>
    public ObservableCollection<LogicInputRowViewModel> InputRows { get; } = new();

    /// <summary>The Outputs list as grouped display rows: bus headers and plain indicators.</summary>
    public ObservableCollection<LogicOutputRowViewModel> OutputRows { get; } = new();

    /// <summary>
    /// True when the assembled network contains at least one cell-instance group
    /// (#1411) — controls the (?) flyout that explains what a cell instance is.
    /// </summary>
    [ObservableProperty]
    private bool _hasCellGroups;

    /// <summary>
    /// Rebuilds both row collections from the current flat lists — call after
    /// <see cref="Inputs"/> and <see cref="Outputs"/> were refilled.
    /// </summary>
    private void RebuildBusRows()
    {
        DetachBusRows();
        InputRows.Clear();
        OutputRows.Clear();
        foreach (var row in SignalBusGrouping.GroupInputs(Inputs))
            InputRows.Add(row);
        // Cell collapse (#1399): gates nested inside a cell instance fold under one
        // collapsed header per cell; named outputs, buses and top-level gates stay flat.
        foreach (var row in CellGrouping.GroupByCell(
                     SignalBusGrouping.GroupOutputs(Outputs), RegisterGatesByCell()))
            OutputRows.Add(row);
        HasCellGroups = OutputRows.OfType<LogicCellGroupViewModel>().Any();
    }

    /// <summary>The register gate ids per top-level cell instance, for the cell-group headers.</summary>
    private IReadOnlyDictionary<string, IReadOnlySet<string>> RegisterGatesByCell()
    {
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        if (_network == null)
            return result;
        foreach (var gateId in _network.RegisterState.Keys.Select(pin => pin.GateId).Distinct())
        {
            if (!CellGrouping.TryCellPrefix(gateId, out var prefix))
                continue;
            if (!result.TryGetValue(prefix, out var gates))
            {
                gates = new HashSet<string>(StringComparer.Ordinal);
                result[prefix] = gates;
            }
            ((HashSet<string>)gates).Add(gateId);
        }
        return result;
    }

    /// <summary>Unsubscribes every bus row from its members before the rows go away.</summary>
    private void DetachBusRows()
    {
        foreach (var bus in InputRows.OfType<LogicSignalBusInputViewModel>())
            bus.Detach();
        foreach (var bus in OutputRows.OfType<LogicSignalBusOutputViewModel>())
            bus.Detach();
    }
}
