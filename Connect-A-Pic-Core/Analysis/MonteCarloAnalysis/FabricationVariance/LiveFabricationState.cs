using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;

/// <summary>Preserves the legacy builder's live reads while sharing the numerical kernel.</summary>
internal sealed class LiveFabricationState : IFabricationVarianceState
{
    private readonly FabricationVarianceSource _source;
    private readonly Dictionary<Component, FabricationParticipant> _owners = new();
    public IReadOnlyList<FabricationParticipant> Participants { get; }
    public IReadOnlyDictionary<FabricationParticipant, ComponentDeviation>? Deviations =>
        _source.CurrentDeviations?.ToDictionary(pair => Owner(pair.Key), pair => pair.Value);

    internal LiveFabricationState(FabricationVarianceSource source)
    {
        _source = source;
        foreach (var component in source.Components)
            _owners.TryAdd(component, new LiveParticipant(component));
        Participants = source.Components.Select(component => _owners[component]).ToArray();
    }

    private FabricationParticipant Owner(Component component)
    {
        if (!_owners.TryGetValue(component, out var owner)) _owners.Add(component, owner = new LiveParticipant(component));
        return owner;
    }

    private sealed class LiveParticipant(Component component) : FabricationParticipant
    {
        internal override ComponentVarianceKind Kind => ComponentVarianceClassifier.Classify(component);
        internal override double LengthUm => Math.Max(component.Parts.GetLength(0), component.Parts.GetLength(1))
            * PerturbedSystemMatrixBuilder.NominalTileLengthUm;
        internal override IEnumerable<(Guid In, Guid Out)> Pins =>
            component.GetAllPins().Select(pin => (pin.IDInFlow, pin.IDOutFlow));
        internal override IReadOnlyDictionary<int, SMatrix> Matrices => component.WaveLengthToSMatrixMap;
    }
}
