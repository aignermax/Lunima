using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;

/// <summary>Only the component physics consumed by the common fabrication perturbation kernel.</summary>
internal abstract class FabricationParticipant
{
    internal abstract ComponentVarianceKind Kind { get; }
    internal abstract double LengthUm { get; }
    internal abstract IEnumerable<(Guid In, Guid Out)> Pins { get; }
    internal abstract IReadOnlyDictionary<int, SMatrix> Matrices { get; }
}

internal interface IFabricationVarianceState
{
    IReadOnlyList<FabricationParticipant> Participants { get; }
    IReadOnlyDictionary<FabricationParticipant, ComponentDeviation>? Deviations { get; }
}
