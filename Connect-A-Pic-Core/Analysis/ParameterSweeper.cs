using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Runs a parameter sweep by varying a component slider across a defined range
    /// and collecting simulation results at each step.
    /// </summary>
    public class ParameterSweeper
    {
        private readonly ILightCalculator _calculator;

        /// <summary>
        /// Creates a parameter sweeper that uses the given light calculator.
        /// </summary>
        public ParameterSweeper(ILightCalculator calculator)
        {
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        /// <summary>
        /// Executes a parameter sweep according to the given configuration.
        /// The slider value is restored to its original value after the sweep.
        /// </summary>
        public async Task<SweepResult> RunSweepAsync(
            SweepConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var slider = configuration.Parameter.GetSlider();
            double originalValue = slider.Value;
            var sweepValues = configuration.GenerateSweepValues();
            var dataPoints = new List<SweepDataPoint>(sweepValues.Length);
            List<Guid>? monitoredPinIds = null;

            try
            {
                foreach (double value in sweepValues)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    slider.Value = value;

                    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var fieldResults = await _calculator.CalculateFieldPropagationAsync(
                        cts, configuration.WavelengthNm);

                    monitoredPinIds ??= fieldResults.Keys.ToList();

                    dataPoints.Add(new SweepDataPoint(value, fieldResults));
                }
            }
            finally
            {
                slider.Value = originalValue;
            }

            return new SweepResult(
                configuration,
                dataPoints,
                monitoredPinIds ?? new List<Guid>());
        }
    }
}
