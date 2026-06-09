using System.Globalization;
using System.Text;

namespace CAP_Core.Analysis.OnaAnalysis
{
    /// <summary>
    /// Complete result of a wavelength (ONA) sweep, including per-step insertion-loss data
    /// and any diagnostic warnings collected during the sweep.
    /// </summary>
    public class WavelengthSweepResult
    {
        /// <summary>The configuration that produced this result.</summary>
        public WavelengthSweepConfiguration Configuration { get; }

        /// <summary>Ordered collection of per-wavelength data points.</summary>
        public IReadOnlyList<WavelengthDataPoint> DataPoints { get; }

        /// <summary>Pin GUIDs that were monitored, in consistent order.</summary>
        public IReadOnlyList<Guid> MonitoredPinIds { get; }

        /// <summary>
        /// Diagnostic warnings emitted before or during the sweep
        /// (e.g. components with only one defined wavelength stop).
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Creates a new wavelength sweep result.</summary>
        public WavelengthSweepResult(
            WavelengthSweepConfiguration configuration,
            List<WavelengthDataPoint> dataPoints,
            List<Guid> monitoredPinIds,
            List<string>? warnings = null)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            DataPoints = dataPoints ?? throw new ArgumentNullException(nameof(dataPoints));
            MonitoredPinIds = monitoredPinIds ?? throw new ArgumentNullException(nameof(monitoredPinIds));
            Warnings = warnings ?? new List<string>();
        }

        /// <summary>Returns the wavelength values (nm) across all data points.</summary>
        public int[] GetWavelengthValues()
        {
            var values = new int[DataPoints.Count];
            for (int i = 0; i < DataPoints.Count; i++)
                values[i] = DataPoints[i].WavelengthNm;
            return values;
        }

        /// <summary>
        /// Returns the insertion-loss series (dB) for a specific pin across all sweep steps.
        /// Returns zeros for pins that were not monitored.
        /// </summary>
        public double[] GetInsertionLossSeriesForPin(Guid pinId)
        {
            var series = new double[DataPoints.Count];
            for (int i = 0; i < DataPoints.Count; i++)
                DataPoints[i].InsertionLossDb.TryGetValue(pinId, out series[i]);
            return series;
        }

        /// <summary>
        /// Generates CSV content with wavelength (nm) and insertion loss (dB) per pin.
        /// When <paramref name="pinNameResolver"/> is supplied and returns a non-null
        /// human-readable name for a pin GUID, that name is used as the column header
        /// instead of the GUID-prefix fallback.
        /// </summary>
        public string GenerateCsvContent(Func<Guid, string?>? pinNameResolver = null)
        {
            var sb = new StringBuilder();
            sb.Append("Wavelength_nm");
            foreach (var pinId in MonitoredPinIds)
            {
                var name = pinNameResolver?.Invoke(pinId);
                var header = !string.IsNullOrEmpty(name)
                    ? name
                    : $"Pin_{pinId.ToString("N")[..8]}";
                sb.Append($",{header}_dB");
            }
            sb.AppendLine();

            foreach (var dp in DataPoints)
            {
                sb.Append(dp.WavelengthNm);
                foreach (var pinId in MonitoredPinIds)
                {
                    dp.InsertionLossDb.TryGetValue(pinId, out double loss);
                    sb.Append(',');
                    sb.Append(loss.ToString("G", CultureInfo.InvariantCulture));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
