namespace CAP_Core.Analysis.MeasuredSpectrum
{
    /// <summary>
    /// Result of a fringe (interference-minima) analysis of a spectrum:
    /// minima wavelengths, free spectral range (FSR) statistics, and the
    /// extracted waveguide group index n_g = λ_c² / (FSR · ΔL) when an arm
    /// imbalance ΔL was supplied.
    /// </summary>
    public class FringeAnalysisResult
    {
        /// <summary>Wavelengths (nm) of the detected interference minima.</summary>
        public IReadOnlyList<double> MinimaWavelengthsNm { get; }

        /// <summary>Mean free spectral range (nm) between consecutive minima.</summary>
        public double MeanFsrNm { get; }

        /// <summary>Standard deviation of the FSR across consecutive minima (nm).</summary>
        public double FsrStdDevNm { get; }

        /// <summary>Centre wavelength (mean of the minima) in nm.</summary>
        public double CenterWavelengthNm { get; }

        /// <summary>
        /// Extracted group index, or null when no arm imbalance was supplied
        /// or no fringes were found.
        /// </summary>
        public double? GroupIndex { get; }

        /// <summary>True when fewer than two minima were detected (no usable fringes).</summary>
        public bool HasFringes => MinimaWavelengthsNm.Count >= 2;

        /// <summary>Creates a fringe-analysis result.</summary>
        public FringeAnalysisResult(
            IReadOnlyList<double> minimaWavelengthsNm,
            double meanFsrNm,
            double fsrStdDevNm,
            double centerWavelengthNm,
            double? groupIndex)
        {
            MinimaWavelengthsNm = minimaWavelengthsNm ?? throw new ArgumentNullException(nameof(minimaWavelengthsNm));
            MeanFsrNm = meanFsrNm;
            FsrStdDevNm = fsrStdDevNm;
            CenterWavelengthNm = centerWavelengthNm;
            GroupIndex = groupIndex;
        }

        /// <summary>An empty result signalling "no fringes found".</summary>
        public static FringeAnalysisResult NoFringes { get; } =
            new(Array.Empty<double>(), 0.0, 0.0, 0.0, null);
    }
}
