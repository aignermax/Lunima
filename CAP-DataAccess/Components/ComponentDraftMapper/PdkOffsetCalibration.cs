using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>Result of <see cref="PdkOffsetCalibration.ApplyAutoCalibrate"/>.</summary>
public enum AutoCalibrateOutcome
{
    /// <summary>Width / Height / Origin / pin offsets were updated.</summary>
    Success,
    /// <summary>Nazca render failed or no result was supplied — nothing to apply.</summary>
    NoPreview,
    /// <summary>Nazca bbox is zero-area; calibration would produce a degenerate box.</summary>
    DegenerateBbox,
    /// <summary>Lunima vs Nazca pin counts differ — auto-fix would have to invent or drop pins.</summary>
    PinCountMismatch,
}

/// <summary>Per-component verdict from <see cref="PdkOffsetCalibration.Evaluate"/>.</summary>
public enum ComponentCheckStatus
{
    /// <summary>Every Lunima pin is within the alignment tolerance of its matched Nazca pin.</summary>
    Aligned,
    /// <summary>Bbox / pin counts match but at least one pin is outside tolerance — fixable by Auto-Calibrate.</summary>
    Misaligned,
    /// <summary>Lunima and Nazca disagree on the pin count — manual edit required.</summary>
    PinCountMismatch,
    /// <summary>Nazca render returned no pins; alignment cannot be assessed.</summary>
    NoNazcaPins,
    /// <summary>The Python preview helper returned an error for this component.</summary>
    RenderFailed,
}

/// <summary>One row of the Check-All / Try-Fix-All report.</summary>
public record ComponentCheckResult(
    string ComponentName,
    ComponentCheckStatus Status,
    int LunimaPinCount,
    int NazcaPinCount,
    double WorstDeltaMicrometers,
    string Message)
{
    /// <summary>Coloured single-character glyph for table rows.</summary>
    public string StatusBadge => Status switch
    {
        ComponentCheckStatus.Aligned          => "✓",
        ComponentCheckStatus.Misaligned       => "⚠",
        ComponentCheckStatus.PinCountMismatch => "✗",
        ComponentCheckStatus.NoNazcaPins      => "?",
        ComponentCheckStatus.RenderFailed     => "✗",
        _                                     => "·",
    };

    /// <summary>True when Auto-Calibrate would resolve the issue without manual edits.</summary>
    public bool IsAutoFixable =>
        Status == ComponentCheckStatus.Misaligned ||
        Status == ComponentCheckStatus.Aligned;
}

/// <summary>
/// Pure calibration math shared by the single-component Auto-Calibrate command
/// and the Check-All / Try-Fix-All batch commands. No MVVM, no async, no UI —
/// keeps the ViewModel lean and lets the unit tests pin every formula.
/// </summary>
public static class PdkOffsetCalibration
{
    /// <summary>
    /// Greedy bipartite pin matcher: repeatedly takes the closest Lunima/Nazca
    /// pair (in current Lunima→Nazca-space projection) and removes both from
    /// the candidate sets. Returns one pair per Lunima pin assuming counts
    /// match, otherwise pairs up to <c>min(lunima, nazca)</c> pins.
    /// </summary>
    public static List<(PhysicalPinDraft Lunima, NazcaPreviewPin Nazca)>
        MatchPinsByGreedyNearest(PdkComponentDraft draft, NazcaPreviewResult result)
    {
        var pairs = new List<(PhysicalPinDraft, NazcaPreviewPin)>();
        var projections = draft.Pins
            .Select(lp => (
                lp,
                x: lp.OffsetXMicrometers - (draft.NazcaOriginOffsetX ?? 0),
                y: (draft.HeightMicrometers - lp.OffsetYMicrometers) - (draft.NazcaOriginOffsetY ?? 0)))
            .ToList();
        var availableNazca = result.Pins.ToList();

        while (projections.Count > 0 && availableNazca.Count > 0)
        {
            var best = (lpIdx: -1, npIdx: -1, dist: double.MaxValue);
            for (var i = 0; i < projections.Count; i++)
            {
                for (var j = 0; j < availableNazca.Count; j++)
                {
                    var dx = availableNazca[j].X - projections[i].x;
                    var dy = availableNazca[j].Y - projections[i].y;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < best.dist) best = (i, j, d);
                }
            }
            pairs.Add((projections[best.lpIdx].lp, availableNazca[best.npIdx]));
            projections.RemoveAt(best.lpIdx);
            availableNazca.RemoveAt(best.npIdx);
        }
        return pairs;
    }

    /// <summary>
    /// Mutates <paramref name="draft"/> so that Width / Height / NazcaOriginOffset
    /// match the Nazca bbox and every pin sits at its matched Nazca pin position.
    /// Returns the outcome enum so callers can decide how to surface failures.
    /// </summary>
    public static AutoCalibrateOutcome ApplyAutoCalibrate(
        PdkComponentDraft draft, NazcaPreviewResult result)
    {
        if (result is not { Success: true }) return AutoCalibrateOutcome.NoPreview;
        if (result.XMax <= result.XMin || result.YMax <= result.YMin)
            return AutoCalibrateOutcome.DegenerateBbox;
        if (result.Pins.Count != draft.Pins.Count)
            return AutoCalibrateOutcome.PinCountMismatch;

        var pairs = MatchPinsByGreedyNearest(draft, result);
        draft.WidthMicrometers   = result.XMax - result.XMin;
        draft.HeightMicrometers  = result.YMax - result.YMin;
        draft.NazcaOriginOffsetX = -result.XMin;
        draft.NazcaOriginOffsetY = -result.YMin;
        foreach (var (lp, np) in pairs)
        {
            lp.OffsetXMicrometers = np.X - result.XMin;
            lp.OffsetYMicrometers = result.YMax - np.Y;
        }
        return AutoCalibrateOutcome.Success;
    }

    /// <summary>
    /// Inspects the alignment of <paramref name="draft"/>'s pins against
    /// <paramref name="result"/>'s Nazca pins under the current calibration
    /// and returns a verdict suitable for Check-All reports. Does NOT mutate
    /// the draft.
    /// </summary>
    public static ComponentCheckResult Evaluate(
        PdkComponentDraft draft, NazcaPreviewResult result, double toleranceMicrometers)
    {
        var name = draft.Name ?? "(unnamed)";
        if (result is not { Success: true })
            return new ComponentCheckResult(name, ComponentCheckStatus.RenderFailed,
                draft.Pins.Count, 0, double.NaN,
                result?.Error ?? "Render returned no result.");

        if (result.Pins.Count == 0)
            return new ComponentCheckResult(name, ComponentCheckStatus.NoNazcaPins,
                draft.Pins.Count, 0, double.NaN,
                "Nazca cell exposes no pins — alignment cannot be assessed.");

        if (result.Pins.Count != draft.Pins.Count)
            return new ComponentCheckResult(name, ComponentCheckStatus.PinCountMismatch,
                draft.Pins.Count, result.Pins.Count, double.NaN,
                $"Lunima declares {draft.Pins.Count} pins, Nazca exposes {result.Pins.Count}.");

        var pairs = MatchPinsByGreedyNearest(draft, result);
        double worst = 0;
        foreach (var (lp, np) in pairs)
        {
            // Same projection as MatchPinsByGreedyNearest: Lunima→Nazca-space.
            var x = lp.OffsetXMicrometers - (draft.NazcaOriginOffsetX ?? 0);
            var y = (draft.HeightMicrometers - lp.OffsetYMicrometers) - (draft.NazcaOriginOffsetY ?? 0);
            var d = Math.Sqrt((np.X - x) * (np.X - x) + (np.Y - y) * (np.Y - y));
            if (d > worst) worst = d;
        }

        if (worst <= toleranceMicrometers)
            return new ComponentCheckResult(name, ComponentCheckStatus.Aligned,
                draft.Pins.Count, result.Pins.Count, worst,
                $"All {draft.Pins.Count} pins within {toleranceMicrometers:F1} µm.");

        return new ComponentCheckResult(name, ComponentCheckStatus.Misaligned,
            draft.Pins.Count, result.Pins.Count, worst,
            $"Worst pin delta {worst:F2} µm — Auto-Calibrate will fix.");
    }
}
