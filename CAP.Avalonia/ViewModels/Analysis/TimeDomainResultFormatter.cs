using System.Text;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.TimeTrace;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// Static helpers that format <see cref="TimeDomainResult"/> for display and CSV export.
/// Extracted from <see cref="TimeDomainViewModel"/> to keep that file within the 250-line cap.
/// </summary>
internal static class TimeDomainResultFormatter
{
    /// <summary>
    /// Formats a result as a human-readable ASCII table (peak power + peak time per output pin).
    /// </summary>
    internal static string FormatResult(TimeDomainResult result, IReadOnlyList<TimeTracePeak>? peaks = null)
    {
        if (result.PinTraces.Count == 0)
            return LocalizationService.Instance.Translate("Analysis.TimeDomain.NoSignalOutput");

        var sb = new StringBuilder();
        sb.AppendLine($"{"Pin (short)",-20} {"Peak Power",12} {"Peak Time (ps)",14}");
        sb.AppendLine(new string('-', 48));

        foreach (var peak in peaks ?? TimeTracePeak.Build(result))
        {
            if (peak.Index < 0) throw new InvalidOperationException("Sequence contains no elements");
            if (peak.Power < 1e-12) continue;
            double peakPs = result.TimeAxis[peak.Index] * 1e12;
            string shortId = peak.PinId.ToString()[..6];
            sb.AppendLine($"{shortId,-20} {peak.Power,12:F6} {peakPs,14:F2}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds CSV content with one time column (ps) and one intensity column per output pin.
    /// </summary>
    internal static string BuildCsvContent(TimeDomainResult result)
    {
        var sb = new StringBuilder();

        sb.Append("time_ps");
        var pinIds = result.PinTraces.Keys.ToList();
        foreach (var pid in pinIds)
            sb.Append($",pin_{pid.ToString()[..8]}");
        sb.AppendLine();

        for (int n = 0; n < result.TimeAxis.Length; n++)
        {
            sb.Append($"{result.TimeAxis[n] * 1e12:F4}");
            foreach (var pid in pinIds)
            {
                var trace = result.PinTraces[pid];
                double val = n < trace.Length ? trace[n] : 0;
                sb.Append($",{val:F8}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
