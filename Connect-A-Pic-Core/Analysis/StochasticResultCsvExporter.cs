using System.Globalization;
using System.Text;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Exports stochastic simulation results to CSV format.
    /// </summary>
    public static class StochasticResultCsvExporter
    {
        /// <summary>
        /// Exports the stochastic result summary to a CSV string.
        /// Includes per-pin mean, standard deviation, and all samples.
        /// </summary>
        /// <param name="result">The stochastic simulation result.</param>
        /// <returns>A CSV-formatted string.</returns>
        public static string ExportToCsv(StochasticResult result)
        {
            var sb = new StringBuilder();
            WriteSummarySection(sb, result);
            sb.AppendLine();
            WriteSamplesSection(sb, result);
            return sb.ToString();
        }

        /// <summary>
        /// Exports the stochastic result to a CSV file.
        /// </summary>
        /// <param name="result">The stochastic simulation result.</param>
        /// <param name="filePath">The output file path.</param>
        public static void ExportToFile(StochasticResult result, string filePath)
        {
            string csv = ExportToCsv(result);
            File.WriteAllText(filePath, csv);
        }

        private static void WriteSummarySection(
            StringBuilder sb, StochasticResult result)
        {
            sb.AppendLine("PinId,MeanPower,StdDeviation");
            foreach (var stats in result.PinStatistics)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:G10},{2:G10}",
                    stats.PinId,
                    stats.MeanPower,
                    stats.StdDeviation));
            }
        }

        private static void WriteSamplesSection(
            StringBuilder sb, StochasticResult result)
        {
            var allPins = result.PinStatistics;
            if (allPins.Count == 0) return;

            sb.Append("Iteration");
            foreach (var stats in allPins)
            {
                sb.Append(CultureInfo.InvariantCulture, $",{stats.PinId}");
            }
            sb.AppendLine();

            int sampleCount = allPins[0].Samples.Count;
            for (int i = 0; i < sampleCount; i++)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{i}");
                foreach (var stats in allPins)
                {
                    double value = i < stats.Samples.Count ? stats.Samples[i] : 0;
                    sb.Append(string.Format(
                        CultureInfo.InvariantCulture, ",{0:G10}", value));
                }
                sb.AppendLine();
            }
        }
    }
}
