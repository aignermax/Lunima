using System.Globalization;
using System.Text;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Exports parameter sweep results to CSV format.
    /// </summary>
    public static class SweepCsvExporter
    {
        private const string Separator = ",";

        /// <summary>
        /// Exports sweep results to a CSV file at the specified path.
        /// </summary>
        public static void ExportToFile(SweepResult result, string filePath)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path must not be empty.", nameof(filePath));

            var csv = GenerateCsvContent(result);
            File.WriteAllText(filePath, csv);
        }

        /// <summary>
        /// Generates CSV content as a string from sweep results.
        /// </summary>
        public static string GenerateCsvContent(SweepResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();
            AppendHeader(sb, result);
            AppendDataRows(sb, result);
            return sb.ToString();
        }

        private static void AppendHeader(StringBuilder sb, SweepResult result)
        {
            sb.Append(result.Configuration.Parameter.DisplayName);

            foreach (var pinId in result.MonitoredPinIds)
            {
                sb.Append(Separator);
                sb.Append(FormatPinId(pinId));
            }

            sb.AppendLine();
        }

        private static void AppendDataRows(StringBuilder sb, SweepResult result)
        {
            foreach (var dataPoint in result.DataPoints)
            {
                sb.Append(FormatDouble(dataPoint.ParameterValue));

                foreach (var pinId in result.MonitoredPinIds)
                {
                    sb.Append(Separator);
                    dataPoint.OutputPowers.TryGetValue(pinId, out double power);
                    sb.Append(FormatDouble(power));
                }

                sb.AppendLine();
            }
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        private static string FormatPinId(Guid pinId)
        {
            return $"Pin_{pinId.ToString("N")[..8]}";
        }
    }
}
