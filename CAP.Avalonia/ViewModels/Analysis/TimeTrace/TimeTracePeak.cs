using CAP_Core.LightCalculation.TimeDomainSimulation;

namespace CAP.Avalonia.ViewModels.Analysis.TimeTrace;

/// <summary>A trace's first maximum, shared by legend sorting and the peak table.</summary>
internal sealed record TimeTracePeak(Guid PinId, double Power, int Index)
{
    /// <summary>Scans each trace once, retaining result order and the first maximum's index.</summary>
    internal static IReadOnlyList<TimeTracePeak> Build(TimeDomainResult result)
    {
        var peaks = new List<TimeTracePeak>(result.PinTraces.Count);
        foreach (var (pinId, trace) in result.PinTraces)
        {
            int index = trace.Length == 0 ? -1 : 0;
            for (int i = 1; i < trace.Length; i++)
            {
                if (trace[i].CompareTo(trace[index]) > 0)
                    index = i;
            }
            peaks.Add(new TimeTracePeak(pinId, index < 0 ? double.NaN : trace[index], index));
        }
        return peaks;
    }
}
