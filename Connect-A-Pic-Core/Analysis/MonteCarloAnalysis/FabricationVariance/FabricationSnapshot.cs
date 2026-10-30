using System.Collections.Immutable;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;

namespace CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;

/// <summary>Ordered fabrication inputs, separate from mutable per-run Gaussian samples.</summary>
internal sealed class FabricationSnapshot
{
    private readonly ImmutableArray<Definition> _participants;
    private readonly ProcessTolerances _tolerances;

    internal FabricationSnapshot(IReadOnlyList<Component> participants, ProcessTolerances tolerances)
    {
        var captured = new Dictionary<Component, Definition>();
        var ordered = ImmutableArray.CreateBuilder<Definition>(participants.Count);
        foreach (var component in participants)
        {
            if (!captured.TryGetValue(component, out var definition))
            {
                definition = new(ComponentVarianceClassifier.Classify(component),
                    Math.Max(component.Parts.GetLength(0), component.Parts.GetLength(1)) * PerturbedSystemMatrixBuilder.NominalTileLengthUm,
                    component.GetAllPins().Select(pin => (pin.IDInFlow, pin.IDOutFlow)).ToImmutableArray(),
                    ComponentMatrixSnapshot.Capture(component));
                captured.Add(component, definition);
            }
            ordered.Add(definition);
        }
        _participants = ordered.ToImmutable();
        _tolerances = new(tolerances.WidthSigmaNm, tolerances.ThicknessSigmaNm);
    }

    internal RunState CreateRun(CancellationToken token) => new(_participants, _tolerances, token);

    // Class identity deliberately represents original component identity, including repeated
    // participants and shared/group flow owners. Equal numerical definitions are not deduplicated.
    internal sealed class Definition(ComponentVarianceKind kind, double length,
        ImmutableArray<(Guid In, Guid Out)> pins, ComponentMatrixSnapshot matrices)
    {
        internal readonly ComponentVarianceKind Kind = kind;
        internal readonly double Length = length;
        internal readonly ImmutableArray<(Guid In, Guid Out)> Pins = pins;
        internal readonly ComponentMatrixSnapshot Matrices = matrices;
    }

    internal sealed class RunState : IVarianceSource, IFabricationVarianceState
    {
        private readonly ProcessTolerances _tolerances;
        public IReadOnlyList<FabricationParticipant> Participants { get; }
        public IReadOnlyDictionary<FabricationParticipant, ComponentDeviation>? Deviations { get; private set; }

        internal RunState(ImmutableArray<Definition> definitions, ProcessTolerances tolerances, CancellationToken token)
        {
            _tolerances = tolerances;
            var owners = new Dictionary<Definition, FabricationParticipant>();
            var participants = new List<FabricationParticipant>(definitions.Length);
            foreach (var definition in definitions)
            {
                token.ThrowIfCancellationRequested();
                if (!owners.TryGetValue(definition, out var participant))
                    owners.Add(definition, participant = new CapturedParticipant(definition));
                participants.Add(participant);
            }
            Participants = participants;
        }

        public void ApplyVariance(GaussianSampler sampler) =>
            Deviations = FabricationVarianceSampling.Draw(Participants, _tolerances, sampler);
        public void RestoreNominal() => Deviations = null;
        internal ISystemMatrixBuilder Decorate(ISystemMatrixBuilder inner) => new FabricationMatrixPerturbation(inner, this);
    }

    private sealed class CapturedParticipant(Definition definition) : FabricationParticipant
    {
        private readonly Dictionary<int, SMatrix> _matrices = definition.Matrices.Rebuild();
        internal override ComponentVarianceKind Kind => definition.Kind;
        internal override double LengthUm => definition.Length;
        internal override IEnumerable<(Guid In, Guid Out)> Pins => definition.Pins;
        internal override IReadOnlyDictionary<int, SMatrix> Matrices => _matrices;
    }
}
