namespace CAP_Core.Analysis.MeasuredSpectrum
{
    /// <summary>
    /// Finds the interference minima of a spectrum (e.g. the fringes of an MZI
    /// transmission) and extracts the free spectral range (FSR) and, for a given
    /// arm imbalance ΔL, the waveguide group index n_g = λ_c² / (FSR · ΔL).
    /// Robust to measurement noise via moving-average smoothing plus a prominence
    /// threshold; minima positions are refined by parabolic interpolation.
    /// </summary>
    public static class FringeAnalyzer
    {
        /// <summary>Fraction of the dynamic range a dip must reach to count as a fringe minimum.</summary>
        public const double ProminenceFraction = 0.05;

        /// <summary>Target smoothing window as a fraction of the point count.</summary>
        public const double SmoothingFraction = 0.02;

        /// <summary>Smallest allowed smoothing window (points).</summary>
        public const int MinSmoothingWindow = 3;

        /// <summary>Largest allowed smoothing window (points).</summary>
        public const int MaxSmoothingWindow = 51;

        /// <summary>
        /// Analyzes a spectrum. When <paramref name="armImbalanceUm"/> is positive,
        /// the group index is extracted as the median of the per-pair estimates
        /// λ_i² / (Δλ_i · ΔL) (λ in µm, ΔL in µm). A spectrum without detectable
        /// fringes yields <see cref="FringeAnalysisResult.NoFringes"/>.
        /// </summary>
        /// <param name="smoothingWindow">
        ///   Optional override for the moving-average window (points). Noise-free
        ///   simulated spectra pass <see cref="MinSmoothingWindow"/>: the default
        ///   noise smoothing smears narrow comb features that ride on a strong
        ///   bandpass envelope (e.g. a ring's through port).
        /// </param>
        public static FringeAnalysisResult Analyze(
            MeasuredSpectrum spectrum, double armImbalanceUm = 0.0, int? smoothingWindow = null)
        {
            if (spectrum == null) throw new ArgumentNullException(nameof(spectrum));
            int n = spectrum.WavelengthNm.Count;
            if (n < 5) return FringeAnalysisResult.NoFringes;

            int window = smoothingWindow is int w
                ? Math.Clamp(w, MinSmoothingWindow, MaxSmoothingWindow)
                : SmoothingWindow(n);
            if (window % 2 == 0) window++;
            double[] wl = spectrum.WavelengthNm.ToArray();
            double[] smoothed = MovingAverage(spectrum.PowerLinear, window);

            var minima = FindMinima(wl, smoothed);
            if (minima.Count < 2) return FringeAnalysisResult.NoFringes;

            var fsrValues = new List<double>();
            var ngEstimates = new List<double>();
            for (int i = 1; i < minima.Count; i++)
            {
                double delta = minima[i] - minima[i - 1];
                fsrValues.Add(delta);
                double pairCenterUm = (minima[i] + minima[i - 1]) / 2000.0;
                ngEstimates.Add(pairCenterUm * pairCenterUm / (delta / 1000.0 * armImbalanceUm));
            }

            double meanFsr = fsrValues.Average();
            double stdFsr = Math.Sqrt(fsrValues.Average(f => (f - meanFsr) * (f - meanFsr)));
            double center = minima.Average();
            double? groupIndex = armImbalanceUm > 0 ? Median(ngEstimates) : null;

            return new FringeAnalysisResult(minima, meanFsr, stdFsr, center, groupIndex);
        }

        /// <summary>Convenience overload for a simulated wavelength sweep.</summary>
        public static FringeAnalysisResult AnalyzeSweep(
            OnaAnalysis.WavelengthSweepResult sweep, Guid pinId, double armImbalanceUm = 0.0) =>
            Analyze(MeasuredSpectrum.FromSweepResult(sweep, pinId), armImbalanceUm);

        private static int SmoothingWindow(int pointCount)
        {
            int window = Math.Clamp((int)(pointCount * SmoothingFraction), MinSmoothingWindow, MaxSmoothingWindow);
            return window % 2 == 0 ? window + 1 : window;
        }

        private static double[] MovingAverage(IReadOnlyList<double> values, int window)
        {
            int n = values.Count;
            int half = window / 2;
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - half);
                int hi = Math.Min(n - 1, i + half);
                result[i] = WindowSum(values, lo, hi) / (hi - lo + 1);
            }
            return result;
        }

        private static double WindowSum(IReadOnlyList<double> values, int lo, int hi)
        {
            double sum = 0;
            for (int i = lo; i <= hi; i++) sum += values[i];
            return sum;
        }

        private static List<double> FindMinima(double[] wl, double[] smoothed)
        {
            var minima = new List<double>();
            double range = smoothed.Max() - smoothed.Min();
            if (range <= 1e-12) return minima;
            double minProminence = range * ProminenceFraction;
            int n = smoothed.Length;

            for (int i = 1; i < n - 1; i++)
            {
                if (smoothed[i] > smoothed[i - 1] || smoothed[i] > smoothed[i + 1])
                    continue;

                double leftMax = smoothed.Take(i + 1).Max();
                double rightMax = smoothed.Skip(i).Max();
                double prominence = Math.Min(leftMax, rightMax) - smoothed[i];
                if (prominence < minProminence) continue;

                minima.Add(RefineMinimum(wl, smoothed, i));
            }
            return minima;
        }

        /// <summary>Sub-sample minimum position via parabolic interpolation of the three-point neighborhood.</summary>
        private static double RefineMinimum(double[] wl, double[] y, int i)
        {
            double denominator = y[i - 1] - 2 * y[i] + y[i + 1];
            if (Math.Abs(denominator) < 1e-15) return wl[i];
            double offset = 0.5 * (y[i - 1] - y[i + 1]) / denominator;
            offset = Math.Clamp(offset, -1.0, 1.0);
            double step = (wl[i + 1] - wl[i - 1]) / 2.0;
            return wl[i] + offset * step;
        }

        private static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
    }
}
