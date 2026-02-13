namespace CAP_Core.Analysis
{
    /// <summary>
    /// Specifies the type of S-Matrix parameter to apply stochastic variation to.
    /// </summary>
    public enum ParameterType
    {
        /// <summary>
        /// Coupling coefficients (off-diagonal S-Matrix elements).
        /// </summary>
        Coupling,

        /// <summary>
        /// Phase terms (argument of complex S-Matrix elements).
        /// </summary>
        Phase,

        /// <summary>
        /// Loss factors (magnitude of S-Matrix elements).
        /// </summary>
        Loss
    }

    /// <summary>
    /// Defines a stochastic variation to apply to a specific parameter type.
    /// </summary>
    public class ParameterVariation
    {
        /// <summary>
        /// The type of parameter to vary.
        /// </summary>
        public ParameterType Type { get; }

        /// <summary>
        /// Variation as a fraction (0.0 to 1.0). For example, 0.05 means +/-5%.
        /// </summary>
        public double VariationFraction { get; }

        /// <summary>
        /// Creates a new parameter variation definition.
        /// </summary>
        /// <param name="type">The parameter type to vary.</param>
        /// <param name="variationFraction">Variation fraction (0.0 to 1.0).</param>
        public ParameterVariation(ParameterType type, double variationFraction)
        {
            if (variationFraction < 0 || variationFraction > 1.0)
                throw new ArgumentOutOfRangeException(
                    nameof(variationFraction),
                    "Variation fraction must be between 0.0 and 1.0.");

            Type = type;
            VariationFraction = variationFraction;
        }
    }
}
