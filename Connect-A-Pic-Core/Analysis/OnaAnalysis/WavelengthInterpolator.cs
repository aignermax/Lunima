using CAP_Core.LightCalculation;
using System.Numerics;

namespace CAP_Core.Analysis.OnaAnalysis
{
    /// <summary>
    /// Provides linear interpolation of S-matrices between defined wavelength stops.
    /// Used by <see cref="SystemMatrixBuilder"/> to produce smooth ONA spectra rather
    /// than the step-shaped curves that nearest-neighbour fallback would give.
    /// </summary>
    public static class WavelengthInterpolator
    {
        /// <summary>
        /// Returns the best-available S-matrix for <paramref name="targetNm"/> from
        /// <paramref name="wavelengthMap"/>.
        /// <list type="bullet">
        ///   <item>Exact match → returns it directly (no interpolation).</item>
        ///   <item>Two adjacent stops bracket the target → linear interpolation.</item>
        ///   <item>Target outside the defined range → nearest-neighbour fallback.</item>
        /// </list>
        /// </summary>
        /// <param name="wavelengthMap">Component's wavelength-to-SMatrix dictionary.</param>
        /// <param name="targetNm">Requested wavelength in nm.</param>
        /// <param name="wasInterpolated">
        ///   <see langword="true"/> when the returned matrix was linearly interpolated;
        ///   <see langword="false"/> for exact or nearest-neighbour results.
        /// </param>
        public static SMatrix GetMatrix(
            IReadOnlyDictionary<int, SMatrix> wavelengthMap,
            int targetNm,
            out bool wasInterpolated)
        {
            if (wavelengthMap.TryGetValue(targetNm, out var exact))
            {
                wasInterpolated = false;
                return exact;
            }

            var sortedKeys = wavelengthMap.Keys.OrderBy(k => k).ToList();

            int lowerNm = sortedKeys.LastOrDefault(k => k < targetNm);
            int upperNm = sortedKeys.FirstOrDefault(k => k > targetNm);

            bool hasLower = lowerNm > 0 && wavelengthMap.ContainsKey(lowerNm);
            bool hasUpper = upperNm > 0 && wavelengthMap.ContainsKey(upperNm);

            if (!hasLower || !hasUpper)
            {
                // Extrapolation: nearest-neighbour only
                var nearest = sortedKeys.OrderBy(k => Math.Abs(k - targetNm)).First();
                wasInterpolated = false;
                return wavelengthMap[nearest];
            }

            wasInterpolated = true;
            return Interpolate(wavelengthMap[lowerNm], wavelengthMap[upperNm], lowerNm, upperNm, targetNm);
        }

        /// <summary>
        /// Creates a new SMatrix by linearly interpolating real and imaginary parts
        /// of every coefficient between two adjacent wavelength stops.
        /// </summary>
        private static SMatrix Interpolate(SMatrix lower, SMatrix upper, int lowerNm, int upperNm, int targetNm)
        {
            double t = (double)(targetNm - lowerNm) / (upperNm - lowerNm);
            var pins = lower.PinReference.Keys.ToList();
            var sliders = lower.SliderReference
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();

            var result = new SMatrix(pins, sliders);
            int size = lower.SMat.RowCount;

            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                {
                    var lo = lower.SMat[row, col];
                    var hi = upper.SMat[row, col];
                    result.SMat[row, col] = LerpComplex(lo, hi, t);
                }
            }

            // Carry over non-linear connections from the lower stop (conservative approach)
            foreach (var kvp in lower.NonLinearConnections)
                result.NonLinearConnections[kvp.Key] = kvp.Value;

            return result;
        }

        private static Complex LerpComplex(Complex a, Complex b, double t)
            => new(a.Real + t * (b.Real - a.Real), a.Imaginary + t * (b.Imaginary - a.Imaginary));
    }
}
