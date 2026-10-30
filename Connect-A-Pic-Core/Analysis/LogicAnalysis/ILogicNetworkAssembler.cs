using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>Builds an evaluable logic network from the selected design scope.</summary>
public interface ILogicNetworkAssembler
{
    Task<LogicNetworkEvaluator> AssembleAsync(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections, int wavelengthNm,
        CancellationToken cancellationToken = default);
}
