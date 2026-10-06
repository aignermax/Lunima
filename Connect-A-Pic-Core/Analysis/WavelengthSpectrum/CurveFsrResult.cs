namespace CAP_Core.Analysis.WavelengthSpectrum
{
    /// <summary>Which spectral features of a curve carry its fringe spacing.</summary>
    public enum SpectrumExtremumKind
    {
        /// <summary>Transmission minima (e.g. the through-port comb of a ring, MZI nulls).</summary>
        Dips,

        /// <summary>Transmission maxima (e.g. the drop-port resonances of a ring).</summary>
        Peaks,
    }

    /// <summary>
    /// Free-spectral-range readout of one simulated spectrum curve (#1382):
    /// the mean spacing between consecutive fringes, how many extrema carried it,
    /// and whether those extrema are dips or peaks.
    /// </summary>
    public class CurveFsrResult
    {
        /// <summary>Mean free spectral range (nm) between consecutive extrema.</summary>
        public double MeanFsrNm { get; }

        /// <summary>Number of extrema (dips or peaks) the FSR was extracted from.</summary>
        public int ExtremumCount { get; }

        /// <summary>Whether the fringes are dips or peaks in the plotted curve.</summary>
        public SpectrumExtremumKind ExtremumKind { get; }

        /// <summary>Creates an FSR readout.</summary>
        public CurveFsrResult(double meanFsrNm, int extremumCount, SpectrumExtremumKind extremumKind)
        {
            MeanFsrNm = meanFsrNm;
            ExtremumCount = extremumCount;
            ExtremumKind = extremumKind;
        }
    }
}
