using System.Numerics;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance
{
    /// <summary>
    /// Decorates an <see cref="ISystemMatrixBuilder"/> with per-component fabrication
    /// perturbations. After the inner builder assembles the system S-matrix,
    /// every intra-component transfer is multiplied by the component's sampled loss/phase
    /// factor; couplers are additionally evaluated at a shifted wavelength and MMIs receive
    /// a port imbalance. Magnitudes are clamped to max(1, |nominal|) per the passivity
    /// policy. Component state is never mutated — with no active sample the inner matrix
    /// passes through unchanged.
    /// </summary>
    internal sealed class FabricationMatrixPerturbation : ISystemMatrixBuilder
    {
        private readonly ISystemMatrixBuilder _inner;
        private readonly IFabricationVarianceState _source;
        private readonly Dictionary<Guid, FabricationParticipant> _pinOwners = new();
        private readonly Dictionary<FabricationParticipant, List<Guid>> _orderedOutFlowIds = new();

        /// <summary>
        /// Wraps <paramref name="inner"/> so that the variance sample active on
        /// <paramref name="source"/> perturbs every built system matrix.
        /// </summary>
        internal FabricationMatrixPerturbation(ISystemMatrixBuilder inner, IFabricationVarianceState source)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            IndexComponentPins();
        }

        /// <inheritdoc />
        public SMatrix GetSystemSMatrix(int LaserWaveLengthInNm)
            => GetSystemSMatrix((double)LaserWaveLengthInNm);

        /// <inheritdoc />
        public SMatrix GetSystemSMatrix(double wavelengthNm)
        {
            var matrix = _inner.GetSystemSMatrix(wavelengthNm);
            var deviations = _source.Deviations;
            if (deviations == null) return matrix;

            var perturbations = ComputePerturbations(deviations, wavelengthNm);
            PerturbLinearEntries(matrix, perturbations, wavelengthNm);
            PerturbNonLinearEntries(matrix, perturbations);
            return matrix;
        }

        private void IndexComponentPins()
        {
            foreach (var component in _source.Participants)
            {
                var outFlowIds = new List<Guid>();
                foreach (var pin in component.Pins)
                {
                    _pinOwners[pin.In] = component;
                    _pinOwners[pin.Out] = component;
                    outFlowIds.Add(pin.Out);
                }
                _orderedOutFlowIds[component] = outFlowIds;
            }
        }

        private static Dictionary<FabricationParticipant, SMatrixPerturbation> ComputePerturbations(
            IReadOnlyDictionary<FabricationParticipant, ComponentDeviation> deviations, double wavelengthNm)
        {
            var perturbations = new Dictionary<FabricationParticipant, SMatrixPerturbation>();
            foreach (var (component, deviation) in deviations)
            {
                perturbations[component] = VarianceSensitivityModel.Compute(
                    component.Kind,
                    deviation,
                    wavelengthNm,
                    component.LengthUm);
            }
            return perturbations;
        }

        private void PerturbLinearEntries(
            SMatrix matrix,
            Dictionary<FabricationParticipant, SMatrixPerturbation> perturbations,
            double wavelengthNm)
        {
            var updates = new Dictionary<(Guid, Guid), Complex>();
            var shiftedBases = new Dictionary<FabricationParticipant, Dictionary<(Guid, Guid), Complex>>();

            foreach (var entry in matrix.GetNonNullValues())
            {
                var component = OwnerOfBoth(entry.Key);
                if (component == null || !perturbations.TryGetValue(component, out var perturbation))
                    continue;

                Complex baseValue = ResolveBaseValue(component, perturbation, entry, wavelengthNm, shiftedBases);
                updates[entry.Key] = Perturb(baseValue, perturbation, entry.Key.PinIdEnd, component);
            }
            matrix.SetValues(updates);
        }

        /// <summary>
        /// The nominal transfer the perturbation multiplies: for spectrally shifted kinds
        /// (couplers) the component's own S-matrix evaluated at wavelength − shift, so the
        /// whole coupling spectrum moves; otherwise the entry as built.
        /// </summary>
        private static Complex ResolveBaseValue(
            FabricationParticipant component,
            SMatrixPerturbation perturbation,
            KeyValuePair<(Guid PinIdStart, Guid PinIdEnd), Complex> entry,
            double wavelengthNm,
            Dictionary<FabricationParticipant, Dictionary<(Guid, Guid), Complex>> shiftedBases)
        {
            if (perturbation.WavelengthShiftNm == 0 || component.Matrices.Count < 2)
                return entry.Value;

            if (!shiftedBases.TryGetValue(component, out var shifted))
            {
                double shiftedTargetNm = wavelengthNm - Math.Round(perturbation.WavelengthShiftNm);
                shifted = WavelengthInterpolator
                    .GetMatrix(component.Matrices, shiftedTargetNm, out _)
                    .GetNonNullValues();
                shiftedBases[component] = shifted;
            }
            return shifted.TryGetValue(entry.Key, out var shiftedValue) ? shiftedValue : entry.Value;
        }

        private Complex Perturb(
            Complex baseValue, SMatrixPerturbation perturbation, Guid outFlowId, FabricationParticipant component)
        {
            double amplitude = perturbation.AmplitudeFactor
                * (1.0 + ImbalanceSign(component, outFlowId) * perturbation.ImbalanceFraction);
            var value = baseValue * Complex.FromPolarCoordinates(
                Math.Max(amplitude, 0), perturbation.PhaseRadians);

            double passivityCap = Math.Max(1.0, baseValue.Magnitude);
            return value.Magnitude > passivityCap
                ? value * (passivityCap / value.Magnitude)
                : value;
        }

        /// <summary>
        /// Deterministic ± sign of the MMI imbalance per output pin (alternating in pin
        /// order), so one port gains what the other loses across the whole run.
        /// </summary>
        private double ImbalanceSign(FabricationParticipant component, Guid outFlowId)
        {
            int index = _orderedOutFlowIds[component].IndexOf(outFlowId);
            return index >= 0 && index % 2 == 1 ? -1.0 : 1.0;
        }

        /// <summary>
        /// Formula-driven (parametric) entries are recomputed during field iteration, which
        /// would overwrite a static perturbation — so their functions are wrapped to apply
        /// the loss/phase factor after evaluation (|factor| ≤ 1 keeps them passive).
        /// </summary>
        private void PerturbNonLinearEntries(
            SMatrix matrix, Dictionary<FabricationParticipant, SMatrixPerturbation> perturbations)
        {
            foreach (var key in matrix.NonLinearConnections.Keys.ToList())
            {
                var component = OwnerOfBoth(key);
                if (component == null || !perturbations.TryGetValue(component, out var perturbation))
                    continue;

                var original = matrix.NonLinearConnections[key];
                var factor = Complex.FromPolarCoordinates(
                    perturbation.AmplitudeFactor, perturbation.PhaseRadians);
                var innerFunc = original.CalcConnectionWeightAsync;
                matrix.NonLinearConnections[key] = original with
                {
                    CalcConnectionWeightAsync = parameters => innerFunc(parameters) * factor,
                };
            }
        }

        /// <summary>The component owning BOTH pins, or null (inter-component connections).</summary>
        private FabricationParticipant? OwnerOfBoth((Guid PinIdStart, Guid PinIdEnd) key) =>
            _pinOwners.TryGetValue(key.PinIdStart, out var start)
            && _pinOwners.TryGetValue(key.PinIdEnd, out var end)
            && ReferenceEquals(start, end)
                ? start
                : null;
    }
}
