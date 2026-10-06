namespace CAP_Core.Analysis.MeasuredSpectrum
{
    /// <summary>
    /// A measured (or externally produced) optical spectrum: wavelength in nm
    /// paired with linear power values. Used for measurement-vs-simulation
    /// comparison and fringe analysis (FSR / group-index extraction).
    /// </summary>
    public class MeasuredSpectrum
    {
        /// <summary>Wavelengths in nanometers, ascending.</summary>
        public IReadOnlyList<double> WavelengthNm { get; }

        /// <summary>Linear power values (e.g. transmission 0..1), same length as <see cref="WavelengthNm"/>.</summary>
        public IReadOnlyList<double> PowerLinear { get; }

        /// <summary>Human-readable origin (file name, port name, …).</summary>
        public string Label { get; }

        /// <summary>Creates a spectrum; arrays must match in length and be non-empty.</summary>
        public MeasuredSpectrum(IReadOnlyList<double> wavelengthNm, IReadOnlyList<double> powerLinear, string label = "")
        {
            if (wavelengthNm == null) throw new ArgumentNullException(nameof(wavelengthNm));
            if (powerLinear == null) throw new ArgumentNullException(nameof(powerLinear));
            if (wavelengthNm.Count == 0) throw new ArgumentException("Spectrum must contain at least one point.", nameof(wavelengthNm));
            if (wavelengthNm.Count != powerLinear.Count)
                throw new ArgumentException("Wavelength and power arrays must have equal length.", nameof(powerLinear));

            WavelengthNm = wavelengthNm;
            PowerLinear = powerLinear;
            Label = label ?? string.Empty;
        }

        /// <summary>Centre wavelength (mean of first/last) in nm.</summary>
        public double CenterWavelengthNm => (WavelengthNm[0] + WavelengthNm[WavelengthNm.Count - 1]) / 2.0;

        /// <summary>
        /// Adapts a simulated <see cref="OnaAnalysis.WavelengthSweepResult"/> to a spectrum
        /// by taking the insertion-loss series (converted from dB to linear) of one pin.
        /// </summary>
        public static MeasuredSpectrum FromSweepResult(OnaAnalysis.WavelengthSweepResult sweep, Guid pinId)
        {
            if (sweep == null) throw new ArgumentNullException(nameof(sweep));
            var lossDb = sweep.GetInsertionLossSeriesForPin(pinId);
            var wavelengths = sweep.GetWavelengthValues();
            var linear = lossDb.Select(MeasuredSpectrumCsvReader.DecibelToLinear).ToArray();
            return new MeasuredSpectrum(wavelengths, linear, $"Sweep pin {pinId.ToString("N")[..8]}");
        }
    }
}
