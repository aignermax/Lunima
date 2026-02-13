using System.Numerics;

namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Validates S-Matrix objects for physical plausibility and numerical stability.
    /// Checks: square matrix, no NaN/Infinity, magnitude &lt;= 1, energy conservation.
    /// </summary>
    public class BasicSMatrixValidator : ISMatrixValidator
    {
        /// <summary>
        /// Tolerance for energy conservation check.
        /// Sum of squared magnitudes per column may exceed 1.0 by this amount.
        /// </summary>
        public const double EnergyConservationTolerance = 1e-6;

        /// <inheritdoc />
        public SMatrixValidationResult Validate(SMatrix matrix)
        {
            var result = new SMatrixValidationResult();

            if (matrix?.SMat == null)
            {
                result.AddError("S-Matrix or its underlying matrix is null.");
                return result;
            }

            ValidateSquare(matrix, result);
            ValidateNoNaNOrInfinity(matrix, result);
            ValidateMagnitudes(matrix, result);
            ValidateEnergyConservation(matrix, result);

            return result;
        }

        private static void ValidateSquare(SMatrix matrix, SMatrixValidationResult result)
        {
            if (matrix.SMat.RowCount != matrix.SMat.ColumnCount)
            {
                result.AddError(
                    $"Matrix is not square: {matrix.SMat.RowCount}x{matrix.SMat.ColumnCount}.");
            }
        }

        private static void ValidateNoNaNOrInfinity(SMatrix matrix, SMatrixValidationResult result)
        {
            int rows = matrix.SMat.RowCount;
            int cols = matrix.SMat.ColumnCount;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Complex value = matrix.SMat[r, c];
                    if (double.IsNaN(value.Real) || double.IsNaN(value.Imaginary)
                        || double.IsInfinity(value.Real) || double.IsInfinity(value.Imaginary))
                    {
                        result.AddError($"Element [{r},{c}] contains NaN or Infinity.");
                    }
                }
            }
        }

        private static void ValidateMagnitudes(SMatrix matrix, SMatrixValidationResult result)
        {
            int rows = matrix.SMat.RowCount;
            int cols = matrix.SMat.ColumnCount;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    double magnitude = matrix.SMat[r, c].Magnitude;
                    if (magnitude > 1.0 + EnergyConservationTolerance)
                    {
                        result.AddError(
                            $"Element [{r},{c}] has magnitude {magnitude:F6} > 1.");
                    }
                }
            }
        }

        private static void ValidateEnergyConservation(
            SMatrix matrix, SMatrixValidationResult result)
        {
            int rows = matrix.SMat.RowCount;
            int cols = matrix.SMat.ColumnCount;

            for (int c = 0; c < cols; c++)
            {
                double columnPowerSum = 0;
                for (int r = 0; r < rows; r++)
                {
                    double mag = matrix.SMat[r, c].Magnitude;
                    columnPowerSum += mag * mag;
                }

                if (columnPowerSum > 1.0 + EnergyConservationTolerance)
                {
                    result.AddWarning(
                        $"Column {c}: sum of |S_ij|^2 = {columnPowerSum:F6} exceeds 1 " +
                        $"(energy conservation violation).");
                }
            }
        }
    }
}
