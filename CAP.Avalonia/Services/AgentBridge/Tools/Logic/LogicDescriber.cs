using CAP_Core.Analysis.LogicAnalysis;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Logic;

/// <summary>Compact, agent-readable forms of truth tables and logic networks.</summary>
public static class LogicDescriber
{
    /// <summary>One entry per input combination: input bits, output bits and output powers.</summary>
    public static IReadOnlyList<object> Rows(TruthTable table) =>
        table.Rows.Select(row => (object)new
        {
            Inputs = Bits(table.InputPinNames, row.InputBits),
            Outputs = table.OutputPinNames.ToDictionary(o => o, o => row.Outputs[o].IsOne ? 1 : 0),
            Powers = table.OutputPinNames.ToDictionary(o => o, o => Math.Round(row.Outputs[o].Power, 4)),
        }).ToList();

    /// <summary>The structure of a built network: signals, gates, registers and timing.</summary>
    public static object Network(LogicNetworkEvaluator network) => new
    {
        Inputs = network.InputPinNames,
        Outputs = network.OutputPinNames,
        Gates = network.Gates.Select(g => new
        {
            Id = g.Key,
            Inputs = g.Value.InputPinNames,
            Outputs = g.Value.OutputPinNames,
        }).ToList(),
        Registers = Registers(network),
        CriticalPathPs = Math.Round(network.CriticalPathDelayPicoseconds, 2),
        CriticalPath = network.CriticalPathGateIds,
        FanOutWarnings = network.FanOutWarnings.Select(w => w.ToString()).ToList(),
    };

    /// <summary>Committed register outputs as <c>gate.pin</c> → bit.</summary>
    public static IReadOnlyDictionary<string, int> Registers(LogicNetworkEvaluator network) =>
        network.RegisterState.ToDictionary(r => $"{r.Key.GateId}.{r.Key.PinName}", r => r.Value ? 1 : 0);

    /// <summary>Bits as 0/1 in the given order.</summary>
    public static IReadOnlyDictionary<string, int> Bits(IEnumerable<string> names, IReadOnlyDictionary<string, bool> bits) =>
        names.ToDictionary(n => n, n => bits.TryGetValue(n, out var b) && b ? 1 : 0);
}
