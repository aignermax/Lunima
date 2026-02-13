using System.Numerics;
using CAP_Core.LightCalculation;
using MathNet.Numerics.LinearAlgebra;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Perturbs S-Matrix elements by applying Gaussian noise to magnitude and phase.
    /// Preserves physical plausibility by clamping magnitudes to [0, 1].
    /// </summary>
    public class SMatrixParameterPerturber : IParameterPerturber
    {
        /// <inheritdoc />
        public SMatrix Perturb(
            SMatrix original,
            IReadOnlyList<ParameterVariation> variations,
            Random random)
        {
            var pinIds = original.PinReference.Keys.ToList();
            var sliders = original.SliderReference
                .Select(kv => (kv.Key, kv.Value)).ToList();
            var perturbed = new SMatrix(pinIds, sliders);

            var transfers = original.GetNonNullValues();
            var perturbedTransfers = PerturbTransfers(
                transfers, variations, random);
            perturbed.SetValues(perturbedTransfers);

            CopyNonLinearConnections(original, perturbed);
            return perturbed;
        }

        private static Dictionary<(Guid, Guid), Complex> PerturbTransfers(
            Dictionary<(Guid, Guid), Complex> transfers,
            IReadOnlyList<ParameterVariation> variations,
            Random random)
        {
            var result = new Dictionary<(Guid, Guid), Complex>();

            foreach (var kvp in transfers)
            {
                Complex value = kvp.Value;
                double magnitude = value.Magnitude;
                double phase = value.Phase;

                foreach (var variation in variations)
                {
                    switch (variation.Type)
                    {
                        case ParameterType.Coupling:
                        case ParameterType.Loss:
                            magnitude = PerturbValue(
                                magnitude, variation.VariationFraction, random);
                            break;
                        case ParameterType.Phase:
                            phase = PerturbValue(
                                phase, variation.VariationFraction, random);
                            break;
                    }
                }

                magnitude = Math.Clamp(magnitude, 0.0, 1.0);
                result[kvp.Key] = Complex.FromPolarCoordinates(magnitude, phase);
            }

            return result;
        }

        private static double PerturbValue(
            double value, double fraction, Random random)
        {
            double sigma = Math.Abs(value) * fraction;
            double noise = GaussianSample(random) * sigma;
            return value + noise;
        }

        /// <summary>
        /// Generates a standard normal sample using Box-Muller transform.
        /// </summary>
        internal static double GaussianSample(Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static void CopyNonLinearConnections(
            SMatrix original, SMatrix perturbed)
        {
            foreach (var kvp in original.NonLinearConnections)
            {
                perturbed.NonLinearConnections[kvp.Key] = kvp.Value;
            }
        }
    }
}
