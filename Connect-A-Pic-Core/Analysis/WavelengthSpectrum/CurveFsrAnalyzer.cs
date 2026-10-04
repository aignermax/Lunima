using CAP_Core.Analysis.MeasuredSpectrum;
using Spectrum = CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrum;

namespace CAP_Core.Analysis.WavelengthSpectrum
{
    /// <summary>
    /// Extracts the free spectral range (FSR) of a simulated transmission curve
    /// (#1382) by reusing the <see cref="FringeAnalyzer"/> that already serves
    /// measured spectra. Every curve is analyzed twice: directly (fringes = dips,
    /// the through-port / MZI case) and inverted (fringes = peaks, the ring
    /// drop-port case). When both find fringes the true fringe set is the
    /// uniformly spaced one — the other mixes real extrema with shallow
    /// flat-top or noise-floor artifacts — so the analysis with the lower
    /// relative FSR spread wins; ties go to dips. Simulated curves are
    /// noise-free, so the analyzer runs with its minimal smoothing window:
    /// the default noise smoothing smears narrow comb features that ride on
    /// a strong bandpass envelope (e.g. a ring's through port).
    /// </summary>
    public static class CurveFsrAnalyzer
    {
        /// <summary>
        /// Analyzes one simulated curve. Returns null when neither direction
        /// finds at least two fringes (flat, at the noise floor, single extremum).
        /// </summary>
        public static CurveFsrResult? Analyze(TransmissionCurve curve)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (curve.IsAtNoiseFloor) return null;

            var dips = AnalyzeDirect(curve);
            var peaks = AnalyzeInverted(curve);

            if (!dips.HasFringes)
                return peaks.HasFringes ? ToResult(peaks, SpectrumExtremumKind.Peaks) : null;
            if (!peaks.HasFringes)
                return ToResult(dips, SpectrumExtremumKind.Dips);
            return RelativeStd(dips) <= RelativeStd(peaks)
                ? ToResult(dips, SpectrumExtremumKind.Dips)
                : ToResult(peaks, SpectrumExtremumKind.Peaks);
        }

        private static CurveFsrResult ToResult(FringeAnalysisResult fringes, SpectrumExtremumKind kind) =>
            new(fringes.MeanFsrNm, fringes.MinimaWavelengthsNm.Count, kind);

        private static double RelativeStd(FringeAnalysisResult fringes) =>
            fringes.MeanFsrNm > 0
                ? fringes.FsrStdDevNm / fringes.MeanFsrNm
                : double.MaxValue;

        private static FringeAnalysisResult AnalyzeDirect(TransmissionCurve curve) =>
            FringeAnalyzer.Analyze(
                new Spectrum(curve.WavelengthsNm, curve.Transmission),
                smoothingWindow: FringeAnalyzer.MinSmoothingWindow);

        private static FringeAnalysisResult AnalyzeInverted(TransmissionCurve curve)
        {
            double max = curve.Transmission.Max();
            var inverted = curve.Transmission.Select(p => max - p).ToArray();
            return FringeAnalyzer.Analyze(
                new Spectrum(curve.WavelengthsNm, inverted),
                smoothingWindow: FringeAnalyzer.MinSmoothingWindow);
        }
    }
}
