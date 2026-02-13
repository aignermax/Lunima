namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Validates S-Matrix objects for physical plausibility and numerical stability.
    /// </summary>
    public interface ISMatrixValidator
    {
        /// <summary>
        /// Validates the given S-Matrix and returns structured results.
        /// </summary>
        /// <param name="matrix">The S-Matrix to validate.</param>
        /// <returns>A validation result containing warnings and errors.</returns>
        SMatrixValidationResult Validate(SMatrix matrix);
    }
}
