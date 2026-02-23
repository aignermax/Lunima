namespace CAP_Core.Analysis
{
    /// <summary>
    /// Defines the range and resolution for a parameter sweep.
    /// </summary>
    public class SweepConfiguration
    {
        /// <summary>
        /// The parameter to sweep.
        /// </summary>
        public SweepParameter Parameter { get; }

        /// <summary>
        /// Start value for the sweep range.
        /// </summary>
        public double StartValue { get; }

        /// <summary>
        /// End value for the sweep range.
        /// </summary>
        public double EndValue { get; }

        /// <summary>
        /// Number of evenly-spaced steps (must be at least 2).
        /// </summary>
        public int StepCount { get; }

        /// <summary>
        /// Laser wavelength in nm to use for each simulation.
        /// </summary>
        public int WavelengthNm { get; }

        /// <summary>
        /// Creates a sweep configuration with the specified range and resolution.
        /// </summary>
        public SweepConfiguration(
            SweepParameter parameter,
            double startValue,
            double endValue,
            int stepCount,
            int wavelengthNm)
        {
            Parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));

            if (stepCount < 2)
                throw new ArgumentOutOfRangeException(nameof(stepCount), "Step count must be at least 2.");

            if (wavelengthNm <= 0)
                throw new ArgumentOutOfRangeException(nameof(wavelengthNm), "Wavelength must be positive.");

            StartValue = startValue;
            EndValue = endValue;
            StepCount = stepCount;
            WavelengthNm = wavelengthNm;
        }

        /// <summary>
        /// Generates the evenly-spaced parameter values for this sweep.
        /// </summary>
        public double[] GenerateSweepValues()
        {
            var values = new double[StepCount];
            double step = (EndValue - StartValue) / (StepCount - 1);
            for (int i = 0; i < StepCount; i++)
            {
                values[i] = StartValue + i * step;
            }
            return values;
        }
    }
}
