namespace CAP_Core.Analysis
{
    /// <summary>
    /// Contains the complete results of a parameter sweep, including metadata
    /// and all data points collected during the sweep.
    /// </summary>
    public class SweepResult
    {
        /// <summary>
        /// The configuration that produced this result.
        /// </summary>
        public SweepConfiguration Configuration { get; }

        /// <summary>
        /// Ordered collection of data points from the sweep.
        /// </summary>
        public IReadOnlyList<SweepDataPoint> DataPoints { get; }

        /// <summary>
        /// Pin GUIDs that were monitored, in consistent order.
        /// </summary>
        public IReadOnlyList<Guid> MonitoredPinIds { get; }

        /// <summary>
        /// Creates a new sweep result.
        /// </summary>
        public SweepResult(
            SweepConfiguration configuration,
            List<SweepDataPoint> dataPoints,
            List<Guid> monitoredPinIds)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            DataPoints = dataPoints ?? throw new ArgumentNullException(nameof(dataPoints));
            MonitoredPinIds = monitoredPinIds ?? throw new ArgumentNullException(nameof(monitoredPinIds));
        }

        /// <summary>
        /// Extracts the power values at a specific pin across all sweep points.
        /// Returns empty array if the pin was not monitored.
        /// </summary>
        public double[] GetPowerSeriesForPin(Guid pinId)
        {
            var series = new double[DataPoints.Count];
            for (int i = 0; i < DataPoints.Count; i++)
            {
                DataPoints[i].OutputPowers.TryGetValue(pinId, out double power);
                series[i] = power;
            }
            return series;
        }

        /// <summary>
        /// Gets the parameter values used across all sweep points.
        /// </summary>
        public double[] GetParameterValues()
        {
            var values = new double[DataPoints.Count];
            for (int i = 0; i < DataPoints.Count; i++)
            {
                values[i] = DataPoints[i].ParameterValue;
            }
            return values;
        }
    }
}
