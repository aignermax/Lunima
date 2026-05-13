namespace CAP_Core.Analysis.OnaAnalysis
{
    /// <summary>
    /// Defines the wavelength range and resolution for an ONA (Optical Network Analyzer) sweep.
    /// </summary>
    public class WavelengthSweepConfiguration
    {
        /// <summary>Maximum allowed step count to prevent runaway sweeps.</summary>
        public const int MaxStepCount = 500;

        /// <summary>Start wavelength in nanometres (must be positive).</summary>
        public int StartNm { get; }

        /// <summary>End wavelength in nanometres (must be greater than <see cref="StartNm"/>).</summary>
        public int EndNm { get; }

        /// <summary>Number of evenly-spaced wavelength steps (2 – <see cref="MaxStepCount"/>).</summary>
        public int StepCount { get; }

        /// <summary>
        /// Creates a wavelength sweep configuration.
        /// </summary>
        /// <param name="startNm">Start wavelength in nm.</param>
        /// <param name="endNm">End wavelength in nm, must exceed <paramref name="startNm"/>.</param>
        /// <param name="stepCount">Number of steps (2 – <see cref="MaxStepCount"/>).</param>
        public WavelengthSweepConfiguration(int startNm, int endNm, int stepCount)
        {
            if (startNm <= 0)
                throw new ArgumentOutOfRangeException(nameof(startNm), "Start wavelength must be positive.");
            if (endNm <= startNm)
                throw new ArgumentException("End wavelength must be greater than start wavelength.", nameof(endNm));
            if (stepCount < 2)
                throw new ArgumentOutOfRangeException(nameof(stepCount), "Step count must be at least 2.");
            if (stepCount > MaxStepCount)
                throw new ArgumentOutOfRangeException(nameof(stepCount), $"Step count must not exceed {MaxStepCount}.");

            StartNm = startNm;
            EndNm = endNm;
            StepCount = stepCount;
        }

        /// <summary>
        /// Generates evenly-spaced integer wavelength values for this configuration.
        /// </summary>
        public int[] GenerateWavelengthValues()
        {
            var values = new int[StepCount];
            double step = (double)(EndNm - StartNm) / (StepCount - 1);
            for (int i = 0; i < StepCount; i++)
                values[i] = (int)Math.Round(StartNm + i * step);
            return values;
        }
    }
}
