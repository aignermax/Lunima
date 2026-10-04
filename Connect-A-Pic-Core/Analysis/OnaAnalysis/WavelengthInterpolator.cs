using CAP_Core.LightCalculation;
using System.Numerics;

namespace CAP_Core.Analysis.OnaAnalysis
{
    /// <summary>
    /// Provides interpolation of S-matrices between defined wavelength stops.
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
        ///   <item>Two adjacent stops bracket the target → polar interpolation (magnitude linear, phase unwrapped).</item>
        ///   <item>Target outside the defined range → nearest-neighbour fallback.</item>
        /// </list>
        /// </summary>
        /// <param name="wavelengthMap">Component's wavelength-to-SMatrix dictionary.</param>
        /// <param name="targetNm">Requested wavelength in nm.</param>
        /// <param name="wasInterpolated">
        ///   <see langword="true"/> when the returned matrix was interpolated;
        ///   <see langword="false"/> for exact or nearest-neighbour results.
        /// </param>
        public static SMatrix GetMatrix(
            IReadOnlyDictionary<int, SMatrix> wavelengthMap,
            int targetNm,
            out bool wasInterpolated) =>
            GetMatrix(wavelengthMap, (double)targetNm, out wasInterpolated);

        /// <summary>
        /// Sub-nm overload of <see cref="GetMatrix(IReadOnlyDictionary{int, SMatrix}, int, out bool)"/>
        /// for the wavelength sweep: an integral target still hits the exact stop, a
        /// fractional target is interpolated between the bracketing integer-nm stops.
        /// </summary>
        /// <param name="wavelengthMap">Component's wavelength-to-SMatrix dictionary.</param>
        /// <param name="targetNm">Requested wavelength in nm (may be fractional).</param>
        /// <param name="wasInterpolated">
        ///   <see langword="true"/> when the returned matrix was interpolated;
        ///   <see langword="false"/> for exact or nearest-neighbour results.
        /// </param>
        public static SMatrix GetMatrix(
            IReadOnlyDictionary<int, SMatrix> wavelengthMap,
            double targetNm,
            out bool wasInterpolated)
        {
            int integralNm = (int)targetNm;
            if (targetNm == integralNm && wavelengthMap.TryGetValue(integralNm, out var exact))
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
        /// Creates a new SMatrix by interpolating every coefficient between two
        /// adjacent wavelength stops in polar form: magnitude linearly, phase on the
        /// unwrapped circle. Component-wise (cartesian) lerp would cut the chord
        /// between two phasors, attenuating fast-rotating entries by up to
        /// 1−cos(Δφ/2) mid-interval — e.g. ~13 % for the 58°/10 nm halfring arc of
        /// the SiEPIC EBeam PDK, which destroys ring-resonator loop gain.
        /// </summary>
        private static SMatrix Interpolate(SMatrix lower, SMatrix upper, int lowerNm, int upperNm, double targetNm)
        {
            double t = (targetNm - lowerNm) / (upperNm - lowerNm);
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

            EnforcePassivity(result);
            return result;
        }

        /// <summary>
        /// Polar lerp is not a convex combination of the endpoint matrices, so the
        /// interpolated matrix's largest singular value can exceed both stops'
        /// (phases rotating into constructive alignment) by up to ~1 % for the
        /// bundled PDKs. Scale the whole matrix back to the passivity boundary:
        /// phases are untouched, and a uniform attenuation never fabricates energy.
        /// </summary>
        private static void EnforcePassivity(SMatrix matrix)
        {
            double sigmaMax = matrix.SMat.L2Norm();
            if (sigmaMax <= 1.0)
                return;
            double scale = 1.0 / sigmaMax;
            for (int row = 0; row < matrix.SMat.RowCount; row++)
            {
                for (int col = 0; col < matrix.SMat.ColumnCount; col++)
                {
                    if (matrix.SMat[row, col] != Complex.Zero)
                        matrix.SMat[row, col] *= scale;
                }
            }
        }

        /// <summary>
        /// Polar lerp: magnitude linear, phase along the shorter unwrapped arc.
        /// Identical to component-wise lerp when both values share a phase (e.g. all
        /// real-valued fixtures); for a zero endpoint (undefined phase) the other
        /// endpoint's phase is used from t=0 on.
        /// </summary>
        private static Complex LerpComplex(Complex a, Complex b, double t)
        {
            double magnitude = a.Magnitude + t * (b.Magnitude - a.Magnitude);
            if (a.Magnitude == 0)
                return Complex.FromPolarCoordinates(magnitude, b.Phase);
            if (b.Magnitude == 0)
                return Complex.FromPolarCoordinates(magnitude, a.Phase);
            double phaseDelta = b.Phase - a.Phase;
            if (phaseDelta > Math.PI) phaseDelta -= 2 * Math.PI;
            if (phaseDelta < -Math.PI) phaseDelta += 2 * Math.PI;
            return Complex.FromPolarCoordinates(magnitude, a.Phase + t * phaseDelta);
        }
    }
}
