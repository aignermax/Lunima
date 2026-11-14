using System.Globalization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>
/// Renders everything a Logic build publishes as stable text lines: the panel's status, inputs,
/// outputs, bus rows, fan-out and timing views, one line per gate with its re-extracted truth
/// table, delay and canvas badges, the outputs of one fixed input vector, and one clock step.
/// </summary>
internal static class LogicBuildDescription
{
    private const string NumberFormat = "0.####";

    /// <summary>The fixed operand vector of the 4-bit adder: A = 5, B = 3, Cin = 1.</summary>
    internal static readonly IReadOnlyDictionary<string, bool> AdderVector = new Dictionary<string, bool>
    {
        ["A0"] = true, ["A2"] = true, ["B0"] = true, ["B1"] = true, ["Cin"] = true,
    };

    /// <summary>Describes the built panel and network, then drives <see cref="AdderVector"/> and one step.</summary>
    internal static List<string> Describe(MainViewModel vm, LogicNetworkEvaluator network)
    {
        var panel = vm.RightPanel.Logic;
        var lines = new List<string>
        {
            "status|" + panel.StatusText,
            "wavelength|" + panel.WavelengthText,
            "inputs|" + string.Join(",", panel.Inputs.Select(input => input.PinName)),
            "input-rows|" + string.Join(",", panel.InputRows.Select(DescribeInputRow)),
            "output-rows|" + string.Join(",", panel.OutputRows.Select(DescribeOutputRow)),
            $"gates|{network.Gates.Count}",
            $"critical-path|{Number(network.CriticalPathDelayPicoseconds)}|{string.Join(",", network.CriticalPathGateIds)}",
            "critical-path-text|" + panel.CriticalPathText,
            $"wire-delays|{network.WireDelaysPicoseconds.Count}|{Number(network.WireDelaysPicoseconds.Values.Sum())}",
            $"fan-out|{panel.HasFanOutWarnings}|{panel.HasSplitFanOutWarnings}|{panel.FanOutWarnings.Count}",
        };
        lines.AddRange(panel.FanOutWarnings.Select(warning =>
            $"fan-out|{warning.WarningText}|{warning.SplitLine}|{string.Join(" / ", warning.VerdictLines)}"));
        lines.AddRange(panel.Outputs.Select(output =>
            $"output|{output.PinName}|{output.RawPinName}|{output.DelayText}|{output.BitText}"));
        var badges = vm.Canvas.LogicGateStates.Badges;
        lines.Add($"badges|{badges.Count}");
        lines.AddRange(network.Gates.Select(gate => DescribeGate(gate.Key, gate.Value, network) + "|badges " +
            string.Join(" ", badges.Where(badge => badge.GroupName == gate.Key).Select(DescribeBadge))));
        lines.AddRange(DriveVector(vm));
        lines.Add(Step(panel, network));
        return lines;
    }

    /// <summary>The network on its own: inputs, outputs, every gate's truth table and delay, and the critical path.</summary>
    internal static List<string> DescribeNetwork(LogicNetworkEvaluator network)
    {
        var lines = new List<string>
        {
            "inputs|" + string.Join(",", network.InputPinNames),
            "outputs|" + string.Join(",", network.OutputPinNames),
            $"critical-path|{Number(network.CriticalPathDelayPicoseconds)}|{string.Join(",", network.CriticalPathGateIds)}",
        };
        lines.AddRange(network.Gates.Select(gate => DescribeGate(gate.Key, gate.Value, network)));
        return lines;
    }

    private static IEnumerable<string> DriveVector(MainViewModel vm)
    {
        var panel = vm.RightPanel.Logic;
        foreach (var input in panel.Inputs)
            input.IsOn = AdderVector.GetValueOrDefault(input.PinName);
        yield return "vector-outputs|" + string.Concat(panel.Outputs.Select(output => output.BitText));
        yield return "vector-named|" + string.Join(",", panel.Outputs
            .Where(output => output.PinName != output.RawPinName)
            .Select(output => $"{output.PinName}={output.BitText}"));
        yield return "vector-badges|" + string.Concat(vm.Canvas.LogicGateStates.Badges.Select(badge => badge.BitText));
        yield return $"vector-timeline|{panel.TimelineEvents.Count}|{panel.HasTimelineEvents}";
    }

    private static string Step(LogicPanelViewModel panel, LogicNetworkEvaluator network)
    {
        var stepEnabled = panel.StepClockCommand.CanExecute(null);
        var events = network.Step();
        var registers = network.RegisterState
            .OrderBy(pair => pair.Key.GateId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.PinName, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.GateId}.{pair.Key.PinName}={(pair.Value ? 1 : 0)}");
        return $"step|enabled={stepEnabled}|registers={panel.RegisterStates.Count}|events={events.Count}|{string.Join(",", registers)}";
    }

    private static string DescribeBadge(LogicGateBadgeViewModel badge) =>
        badge.HasSignalName ? $"{badge.PinName}:{badge.LabelText}" : $"{badge.PinName}={badge.BitText}";

    private static string DescribeGate(string gateId, LogicGateModel gate, LogicNetworkEvaluator network)
    {
        var rows = gate.TruthTable.Rows.Select(row =>
            string.Concat(gate.InputPinNames.Select(pin => row.InputBits[pin] ? '1' : '0')) + ">" +
            string.Join("/", gate.OutputPinNames.Select(pin =>
                (row.Outputs[pin].IsOne ? "1:" : "0:") + Number(row.Outputs[pin].Power))));
        return $"gate|{gateId}|{Number(network.GateDelaysPicoseconds[gateId])}|" +
            $"{string.Join(",", gate.InputPinNames)}>{string.Join(",", gate.OutputPinNames)}|{string.Join(" ", rows)}";
    }

    private static string DescribeInputRow(LogicInputRowViewModel row) => row switch
    {
        LogicSignalBusInputViewModel bus => $"bus:{bus.Prefix}[{string.Join(" ", bus.Members.Select(m => m.PinName))}]",
        LogicNetworkInputViewModel input => input.PinName,
        _ => row.GetType().Name,
    };

    private static string DescribeOutputRow(LogicOutputRowViewModel row) => row switch
    {
        LogicSignalBusOutputViewModel bus => $"bus:{bus.Prefix}[{string.Join(" ", bus.Members.Select(m => m.PinName))}]",
        LogicCellGroupViewModel cell => $"cell:{cell.CellName}[{cell.Members.Count}]",
        LogicNetworkOutputViewModel output => output.PinName,
        _ => row.GetType().Name,
    };

    private static string Number(double value) => value.ToString(NumberFormat, CultureInfo.InvariantCulture);
}
