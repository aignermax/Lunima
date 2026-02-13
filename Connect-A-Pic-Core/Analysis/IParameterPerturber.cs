using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Applies stochastic perturbations to S-Matrix parameters.
    /// </summary>
    public interface IParameterPerturber
    {
        /// <summary>
        /// Creates a perturbed copy of the given S-Matrix values.
        /// The original matrix is not modified.
        /// </summary>
        /// <param name="original">The original S-Matrix to perturb.</param>
        /// <param name="variations">The variations to apply.</param>
        /// <param name="random">Random number generator for this run.</param>
        /// <returns>A new S-Matrix with perturbed values.</returns>
        SMatrix Perturb(
            SMatrix original,
            IReadOnlyList<ParameterVariation> variations,
            Random random);
    }
}
