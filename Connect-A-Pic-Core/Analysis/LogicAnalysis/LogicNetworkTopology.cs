using System.Collections.Immutable;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>Transient input read only during topology capture; never retained by the worker snapshot.</summary>
internal sealed record LogicGateTopologyInput(ComponentGroup Group, GateRoleAssignment Roles, string GateId);

/// <summary>Detached, ordered wiring and scalar timing/link physics shared by all model evaluations.</summary>
internal sealed class LogicNetworkTopology
{
    private readonly ImmutableArray<string> _inputs;
    private readonly ImmutableArray<KeyValuePair<LogicPinRef, LogicNetDriver>> _wiring;
    private readonly ImmutableArray<KeyValuePair<string, LogicPinRef>> _outputs;
    private readonly ImmutableArray<KeyValuePair<string, double>> _gateDelays;
    private readonly ImmutableArray<KeyValuePair<LogicWireEdge, double>> _wireDelays;
    private readonly ImmutableArray<string> _registers;
    private readonly ImmutableArray<KeyValuePair<LogicWireEdge, LogicWireLinkLoss>> _linkLosses;

    internal LogicNetworkTopology(IEnumerable<string> inputs, IDictionary<LogicPinRef, LogicNetDriver> wiring,
        IDictionary<string, LogicPinRef> outputs, IDictionary<string, double> gateDelays,
        IDictionary<LogicWireEdge, double> wireDelays, IEnumerable<string> registers,
        IDictionary<LogicWireEdge, LogicWireLinkLoss> linkLosses)
    {
        _inputs = inputs.ToImmutableArray();
        _wiring = wiring.ToImmutableArray();
        // Output tap order is presentation/CSV order; hash-map enumeration is not that contract.
        _outputs = outputs.ToImmutableArray();
        _gateDelays = gateDelays.ToImmutableArray();
        _wireDelays = wireDelays.ToImmutableArray();
        _registers = registers.ToImmutableArray();
        _linkLosses = linkLosses.ToImmutableArray();
    }

    internal LogicNetworkEvaluator Build(IReadOnlyDictionary<string, LogicGateModel> models) =>
        new(_inputs, models, _wiring.ToDictionary(pair => pair.Key, pair => pair.Value),
            _outputs.ToDictionary(pair => pair.Key, pair => pair.Value),
            _gateDelays.ToDictionary(pair => pair.Key, pair => pair.Value),
            _wireDelays.ToDictionary(pair => pair.Key, pair => pair.Value), _registers,
            _linkLosses.ToDictionary(pair => pair.Key, pair => pair.Value));
}
