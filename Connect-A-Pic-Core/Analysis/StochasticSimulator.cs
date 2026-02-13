using System.Collections.Concurrent;
using System.Numerics;
using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Runs Monte Carlo simulations with random parameter variations
    /// to estimate design robustness against fabrication tolerances.
    /// </summary>
    public class StochasticSimulator
    {
        private readonly IParameterPerturber _perturber;

        /// <summary>
        /// Creates a new stochastic simulator.
        /// </summary>
        /// <param name="perturber">The parameter perturber to use.</param>
        public StochasticSimulator(IParameterPerturber perturber)
        {
            _perturber = perturber;
        }

        /// <summary>
        /// Runs N Monte Carlo iterations in parallel, perturbing the system
        /// S-Matrix each time and collecting output power statistics.
        /// </summary>
        /// <param name="systemMatrix">The nominal system S-Matrix.</param>
        /// <param name="inputVector">The input light vector.</param>
        /// <param name="variations">Parameter variations to apply.</param>
        /// <param name="iterationCount">Number of Monte Carlo iterations.</param>
        /// <param name="seed">Optional random seed for reproducibility.</param>
        /// <returns>Aggregated stochastic simulation results.</returns>
        public async Task<StochasticResult> RunAsync(
            SMatrix systemMatrix,
            Vector<Complex> inputVector,
            IReadOnlyList<ParameterVariation> variations,
            int iterationCount,
            int? seed = null)
        {
            if (iterationCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(iterationCount), "Must run at least one iteration.");

            var pinIds = systemMatrix.PinReference.Keys.ToList();
            var allResults = new ConcurrentBag<Dictionary<Guid, Complex>>();

            await Task.Run(() =>
            {
                Parallel.For(0, iterationCount, i =>
                {
                    int iterSeed = seed.HasValue ? seed.Value + i : Environment.TickCount + i;
                    var random = new Random(iterSeed);
                    var perturbed = _perturber.Perturb(
                        systemMatrix, variations, random);
                    var result = RunSingleIteration(perturbed, inputVector);
                    allResults.Add(result);
                });
            });

            return BuildResult(pinIds, variations, allResults, iterationCount);
        }

        private static Dictionary<Guid, Complex> RunSingleIteration(
            SMatrix perturbedMatrix,
            Vector<Complex> inputVector)
        {
            int stepCount = perturbedMatrix.PinReference.Count * 2;
            var cts = new CancellationTokenSource();
            return perturbedMatrix
                .CalcFieldAtPinsAfterStepsAsync(inputVector, stepCount, cts)
                .GetAwaiter().GetResult();
        }

        private static StochasticResult BuildResult(
            List<Guid> pinIds,
            IReadOnlyList<ParameterVariation> variations,
            ConcurrentBag<Dictionary<Guid, Complex>> allResults,
            int iterationCount)
        {
            var resultList = allResults.ToList();
            var pinStatistics = new List<OutputPowerStatistics>();

            foreach (var pinId in pinIds)
            {
                var samples = CollectPowerSamples(resultList, pinId);
                pinStatistics.Add(new OutputPowerStatistics(pinId, samples));
            }

            return new StochasticResult(iterationCount, variations, pinStatistics);
        }

        private static List<double> CollectPowerSamples(
            List<Dictionary<Guid, Complex>> results, Guid pinId)
        {
            var samples = new List<double>(results.Count);
            foreach (var result in results)
            {
                if (result.TryGetValue(pinId, out var field))
                {
                    samples.Add(field.Magnitude * field.Magnitude);
                }
                else
                {
                    samples.Add(0.0);
                }
            }
            return samples;
        }
    }
}
