namespace CAP_Core.Components.Parametric
{
    /// <summary>
    /// Defines a single S-Matrix connection whose magnitude and phase
    /// are computed from formula expressions referencing named parameters.
    /// </summary>
    public class FormulaConnection
    {
        /// <summary>
        /// Source pin name (e.g., "in", "a0").
        /// </summary>
        public string FromPin { get; }

        /// <summary>
        /// Destination pin name (e.g., "out", "b0").
        /// </summary>
        public string ToPin { get; }

        /// <summary>
        /// Formula expression for magnitude (0-1).
        /// Can be a constant like "0.707" or a formula like "sqrt(coupling_ratio)".
        /// </summary>
        public string MagnitudeFormula { get; }

        /// <summary>
        /// Formula expression for phase in degrees.
        /// Can be a constant like "90" or a formula like "phase_shift * 180 / pi".
        /// </summary>
        public string PhaseDegFormula { get; }

        /// <summary>
        /// Creates a new formula-based connection definition.
        /// </summary>
        public FormulaConnection(
            string fromPin,
            string toPin,
            string magnitudeFormula,
            string phaseDegFormula)
        {
            if (string.IsNullOrWhiteSpace(fromPin))
                throw new ArgumentException("FromPin cannot be empty.", nameof(fromPin));
            if (string.IsNullOrWhiteSpace(toPin))
                throw new ArgumentException("ToPin cannot be empty.", nameof(toPin));
            if (string.IsNullOrWhiteSpace(magnitudeFormula))
                throw new ArgumentException("Magnitude formula cannot be empty.", nameof(magnitudeFormula));

            FromPin = fromPin;
            ToPin = toPin;
            MagnitudeFormula = magnitudeFormula;
            PhaseDegFormula = phaseDegFormula ?? "0";
        }
    }
}
