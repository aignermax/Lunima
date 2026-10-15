namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>
/// The step series the repair candidates use to widen a narrow corridor in front of a pin:
/// fine increments (a grid cell is a few µm, and a corridor is often short by less than one
/// routable pitch) up to <see cref="MaxStepMicrometers"/>. The widening direction comes first,
/// smallest step first, then the opposite direction — deterministic and budget-friendly, since
/// a short time budget tries the most promising moves.
/// </summary>
public static class CorridorSteps
{
    /// <summary>The finest step tried, in µm.</summary>
    public const double FineStepMicrometers = 4;

    /// <summary>The largest step tried, in µm.</summary>
    public const double MaxStepMicrometers = 30;

    /// <summary>Steps in the widening direction only: +4, +8, … +28, +30 (times <paramref name="sign"/>).</summary>
    public static IReadOnlyList<double> Widening(int sign) => Magnitudes().Select(m => m * Math.Sign(sign)).ToList();

    /// <summary>All steps: the widening ones first, then the same magnitudes the other way.</summary>
    public static IReadOnlyList<double> WideningFirst(int sign)
    {
        int s = sign >= 0 ? 1 : -1;
        return Magnitudes().Select(m => m * s).Concat(Magnitudes().Select(m => -m * s)).ToList();
    }

    /// <summary>Steps alternating in sign for moves without a preferred direction: +4, −4, +8, −8, …</summary>
    public static IReadOnlyList<double> Alternating() =>
        Magnitudes().SelectMany(m => new[] { m, -m }).ToList();

    private static IEnumerable<double> Magnitudes()
    {
        for (double m = FineStepMicrometers; m < MaxStepMicrometers; m += FineStepMicrometers)
            yield return m;
        yield return MaxStepMicrometers;
    }
}
