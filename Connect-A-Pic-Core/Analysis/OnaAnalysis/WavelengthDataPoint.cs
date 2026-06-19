using System.Numerics;

namespace CAP_Core.Analysis.OnaAnalysis
{
    /// <summary>
    /// A single data point from a wavelength sweep, containing output powers and
    /// insertion-loss values at each monitored pin for one wavelength step.
    /// </summary>
    public class WavelengthDataPoint
    {
        /// <summary>Insertion-loss floor applied when output power is zero or negative.</summary>
        public const double MinInsertionLossDb = -120.0;

        /// <summary>Wavelength (nm) at which this simulation step was run.</summary>
        public int WavelengthNm { get; }

        /// <summary>Output power (|field|²) per pin GUID.</summary>
        public IReadOnlyDictionary<Guid, double> OutputPowers { get; }

        /// <summary>
        /// Insertion loss in dB per pin GUID.
        /// Clamped to <see cref="MinInsertionLossDb"/> when output is zero or near-zero.
        /// </summary>
        public IReadOnlyDictionary<Guid, double> InsertionLossDb { get; }

        /// <summary>
        /// Creates a wavelength data point from raw field results.
        /// </summary>
        /// <param name="wavelengthNm">Wavelength of this step.</param>
        /// <param name="fieldResults">Complex field amplitudes keyed by pin GUID.</param>
        /// <param name="inputPower">Total input power (linear) for insertion-loss reference.</param>
        public WavelengthDataPoint(int wavelengthNm, Dictionary<Guid, Complex> fieldResults, double inputPower)
        {
            if (fieldResults == null)
                throw new ArgumentNullException(nameof(fieldResults));

            WavelengthNm = wavelengthNm;
            OutputPowers = ConvertToPowers(fieldResults);
            InsertionLossDb = ComputeInsertionLoss(OutputPowers, inputPower);
        }

        private static Dictionary<Guid, double> ConvertToPowers(Dictionary<Guid, Complex> fields)
        {
            var powers = new Dictionary<Guid, double>(fields.Count);
            foreach (var kvp in fields)
                powers[kvp.Key] = kvp.Value.Magnitude * kvp.Value.Magnitude;
            return powers;
        }

        private static Dictionary<Guid, double> ComputeInsertionLoss(
            IReadOnlyDictionary<Guid, double> outputPowers, double inputPower)
        {
            var result = new Dictionary<Guid, double>(outputPowers.Count);
            foreach (var kvp in outputPowers)
            {
                if (inputPower <= 0 || kvp.Value <= 0)
                    result[kvp.Key] = MinInsertionLossDb;
                else
                    result[kvp.Key] = Math.Max(10.0 * Math.Log10(kvp.Value / inputPower), MinInsertionLossDb);
            }
            return result;
        }
    }
}
