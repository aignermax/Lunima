using System.Globalization;
using System.Text;

namespace CAP_Core.Analysis
{
    /// <summary>Pure CSV formatting for parameter sweep results; performs no file access.</summary>
    public static class SweepCsvFormatter
    {
        private const string Separator = ",";

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
