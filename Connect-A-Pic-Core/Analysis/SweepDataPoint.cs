using System.Numerics;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// A single data point in a parameter sweep, containing the parameter value
    /// and the resulting output powers at each monitored pin.
    /// </summary>
    public class SweepDataPoint
    {
        /// <summary>
        /// The parameter value used for this simulation step.
        /// </summary>
        public double ParameterValue { get; }

        /// <summary>
        /// Output powers (magnitude squared) keyed by pin GUID.
        /// </summary>
        public Dictionary<Guid, double> OutputPowers { get; }

        /// <summary>
        /// Creates a new sweep data point.
        /// </summary>
        public SweepDataPoint(double parameterValue, Dictionary<Guid, Complex> fieldResults)
        {
            if (fieldResults == null)
                throw new ArgumentNullException(nameof(fieldResults));

            ParameterValue = parameterValue;
            OutputPowers = ConvertToPowers(fieldResults);
        }

        private static Dictionary<Guid, double> ConvertToPowers(Dictionary<Guid, Complex> fields)
        {
            var powers = new Dictionary<Guid, double>();
            foreach (var kvp in fields)
            {
                powers[kvp.Key] = kvp.Value.Magnitude * kvp.Value.Magnitude;
            }
            return powers;
        }
    }
}
