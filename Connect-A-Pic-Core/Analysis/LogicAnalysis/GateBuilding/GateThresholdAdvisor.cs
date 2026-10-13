namespace CAP_Core.Analysis.LogicAnalysis.GateBuilding;

/// <summary>One simulated output power plus the bit the intended function expects there (null = no intent).</summary>
/// <param name="Power">Normalized output power of one truth-table row.</param>
/// <param name="ExpectedBit">Bit the intended function expects; null for a custom gate.</param>
public readonly record struct GatePowerSample(double Power, bool? ExpectedBit);

/// <summary>
/// A suggested analog→digital threshold and how safely it separates the two levels.
/// </summary>
/// <param name="Threshold">Suggested threshold in (0, 1).</param>
/// <param name="BrightestZero">Highest power that must read 0.</param>
/// <param name="DarkestOne">Lowest power that must read 1.</param>
/// <param name="MarginDb">
/// Power ratio between the darkest 1 and the brightest 0 in dB, capped at
/// <see cref="GateThresholdAdvisor.MaxMarginDb"/>; zero or negative when the levels overlap.
/// </param>
/// <param name="LevelsOverlap">True when no threshold can separate the levels.</param>
public sealed record ThresholdSuggestion(
    double Threshold, double BrightestZero, double DarkestOne, double MarginDb, bool LevelsOverlap);

/// <summary>
/// Suggests the threshold of a gate automatically. With a declared function the
/// threshold sits midway between the brightest row expected to read 0 and the darkest
/// row expected to read 1; without one (custom) it sits midway across the largest gap
/// between the sorted measured powers. The margin is the ratio of the two levels in dB —
/// the headroom loss and noise may eat before a bit flips.
/// </summary>
public sealed class GateThresholdAdvisor
{
    /// <summary>Lowest threshold the advisor suggests (the extractor needs the open interval (0, 1)).</summary>
    public const double MinThreshold = 0.001;

    /// <summary>Highest threshold the advisor suggests.</summary>
    public const double MaxThreshold = 0.999;

    /// <summary>Margin reported when the 0-level is perfectly dark (an infinite ratio).</summary>
    public const double MaxMarginDb = 60.0;

    /// <summary>Fallback threshold when the powers carry no level structure at all.</summary>
    public const double NeutralThreshold = 0.5;

    private const int DisplayDecimals = 3;
    private const double DecibelsPerDecade = 10.0;

    /// <summary>Suggests a threshold for the given rows.</summary>
    /// <param name="samples">Every output power of the extracted table with its expected bit.</param>
    public ThresholdSuggestion Suggest(IReadOnlyList<GatePowerSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var zeros = samples.Where(s => s.ExpectedBit == false).Select(s => s.Power).ToList();
        var ones = samples.Where(s => s.ExpectedBit == true).Select(s => s.Power).ToList();
        if (zeros.Count > 0 && ones.Count > 0)
            return Between(zeros.Max(), ones.Min());
        return AcrossLargestGap(samples.Select(s => s.Power).ToList());
    }

    /// <summary>
    /// Custom gate: the largest gap between neighbouring sorted powers splits 0 from 1.
    /// The gap is measured as a power ratio (the dB scale of the margin), not as a
    /// difference: an ideal NAND rests at 0.5 / 0.25 / 0, two equal differences, but
    /// only the step down to the dark row is the logic level change.
    /// </summary>
    private static ThresholdSuggestion AcrossLargestGap(List<double> powers)
    {
        var sorted = powers.Distinct().OrderBy(p => p).ToList();
        if (sorted.Count < 2)
        {
            var level = sorted.Count == 1 ? sorted[0] : 0.0;
            return new ThresholdSuggestion(NeutralThreshold, level, level, 0.0, LevelsOverlap: true);
        }

        var gapIndex = Enumerable.Range(0, sorted.Count - 1)
            .OrderByDescending(i => RatioGap(sorted[i], sorted[i + 1]))
            .ThenBy(i => i)
            .First();
        return Between(sorted[gapIndex], sorted[gapIndex + 1]);
    }

    /// <summary>Decades between two powers, with a dark floor so a zero power stays finite.</summary>
    private static double RatioGap(double lower, double upper) =>
        Math.Log10(Math.Max(upper, DarkFloorPower) / Math.Max(lower, DarkFloorPower));

    /// <summary>Power treated as "dark" when comparing ratios (−60 dB of a unit input).</summary>
    private const double DarkFloorPower = 1e-6;

    /// <summary>Midpoint threshold and margin between the two levels.</summary>
    private static ThresholdSuggestion Between(double brightestZero, double darkestOne)
    {
        var overlap = darkestOne <= brightestZero;
        var midpoint = Math.Clamp((brightestZero + darkestOne) / 2.0, MinThreshold, MaxThreshold);
        var threshold = overlap ? midpoint : Readable(midpoint, brightestZero, darkestOne);
        return new ThresholdSuggestion(threshold, brightestZero, darkestOne, MarginDb(brightestZero, darkestOne), overlap);
    }

    /// <summary>Rounds to three decimals when the rounded value still separates the levels.</summary>
    private static double Readable(double threshold, double brightestZero, double darkestOne)
    {
        var rounded = Math.Round(threshold, DisplayDecimals, MidpointRounding.AwayFromZero);
        return rounded > brightestZero && rounded < darkestOne && rounded is >= MinThreshold and <= MaxThreshold
            ? rounded
            : threshold;
    }

    /// <summary>10·log10(darkest 1 / brightest 0), capped for a perfectly dark 0-level.</summary>
    private static double MarginDb(double brightestZero, double darkestOne)
    {
        if (darkestOne <= 0)
            return -MaxMarginDb;
        if (brightestZero <= 0)
            return MaxMarginDb;
        return Math.Min(MaxMarginDb, DecibelsPerDecade * Math.Log10(darkestOne / brightestZero));
    }
}
