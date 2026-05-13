using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>
/// Extension methods that map a <see cref="MaterialDispersionDraft"/> DTO from a PDK JSON
/// into a domain-level <see cref="IDispersionModel"/> instance.
/// </summary>
public static class DispersionPdkExtensions
{
    /// <summary>
    /// Converts a <see cref="MaterialDispersionDraft"/> DTO to an <see cref="IDispersionModel"/>.
    /// Returns <c>null</c> if <paramref name="draft"/> is <c>null</c>.
    /// </summary>
    /// <param name="draft">The DTO parsed from PDK JSON, or null if absent.</param>
    /// <param name="fallbackLossDbPerCm">
    /// Scalar loss used when the draft has no loss sub-model (backwards compatibility).
    /// </param>
    public static IDispersionModel? ToDispersionModel(
        this MaterialDispersionDraft? draft,
        double fallbackLossDbPerCm = 0.5)
    {
        if (draft == null)
            return null;

        string? type = draft.Type?.ToLowerInvariant();
        if (type != null && type != "polynomial" && type != "tabulated")
            System.Diagnostics.Trace.TraceWarning(
                $"[DispersionPdkExtensions] Unknown materialDispersion type '{draft.Type}' — " +
                "falling back to polynomial. Check PDK JSON for typos.");

        return type switch
        {
            "tabulated" => BuildTabulated(draft, fallbackLossDbPerCm),
            _ => BuildPolynomial(draft, fallbackLossDbPerCm),   // "polynomial" or null → polynomial
        };
    }

    /// <summary>
    /// Returns a diagnostic warning when the PDK declares no <c>materialDispersion</c> block
    /// on any component or at the PDK root.  Returns <c>null</c> when at least one dispersion
    /// model is present.
    /// </summary>
    /// <remarks>
    /// Call this before starting a multi-wavelength operation (ONA sweep, time-domain IFFT) so
    /// that PDK authors are alerted rather than silently receiving a flat constant-loss curve.
    /// </remarks>
    /// <param name="pdk">The parsed PDK draft to inspect.</param>
    /// <returns>
    /// A human-readable warning string if no dispersion model is found; otherwise <c>null</c>.
    /// </returns>
    public static string? GetNoDispersionDiagnostic(PdkDraft pdk)
    {
        if (pdk.MaterialDispersion != null)
            return null;
        if (pdk.Components.Any(c => c.MaterialDispersion != null))
            return null;

        return $"PDK '{pdk.Name}' declares no materialDispersion block on any component or at the " +
               "PDK root. Multi-wavelength operations (ONA, time-domain) will use flat " +
               "constant-loss behaviour.";
    }

    // ---- private builders ----

    private static IDispersionModel BuildPolynomial(
        MaterialDispersionDraft draft,
        double fallbackLossDbPerCm)
    {
        var ei = draft.EffectiveIndex;
        double n0 = ei?.N0 ?? 2.45;
        double n1 = ei?.N1 ?? 0.0;
        double n2 = ei?.N2 ?? 0.0;
        double? ng0 = draft.GroupIndex?.Ng0;

        (double loss0, double lossSlope) = ExtractPolynomialLoss(
            draft.PropagationLossDbPerCm, draft.CenterWavelengthNm, fallbackLossDbPerCm);

        return new PolynomialDispersion(
            centerWavelengthNm: draft.CenterWavelengthNm,
            n0: n0,
            n1: n1,
            n2: n2,
            ng0: ng0,
            loss0: loss0,
            lossSlope: lossSlope);
    }

    private static (double Loss0, double LossSlope) ExtractPolynomialLoss(
        LossDraft? lossDraft,
        double centerWavelengthNm,
        double fallback)
    {
        if (lossDraft == null)
            return (fallback, 0.0);

        if (lossDraft.Type?.ToLowerInvariant() == "tabulated" && lossDraft.Points != null)
        {
            var pts = ParseLossPoints(lossDraft.Points);
            if (pts.Count >= 2)
            {
                // Interpolate the loss at the center wavelength so loss0 is correct
                double loss0 = InterpolateTabulated(pts, centerWavelengthNm);

                // Estimate local slope from the bracketing points around the center
                double slope = EstimateSlope(pts, centerWavelengthNm);
                return (loss0, slope);
            }
            if (pts.Count == 1)
                return (pts[0].Value, 0.0);
        }

        return (lossDraft.ConstantDbPerCm, 0.0);
    }

    private static double InterpolateTabulated(
        List<(double WavelengthNm, double Value)> pts,
        double wavelengthNm)
    {
        if (wavelengthNm <= pts[0].WavelengthNm) return pts[0].Value;
        if (wavelengthNm >= pts[^1].WavelengthNm) return pts[^1].Value;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (pts[i + 1].WavelengthNm >= wavelengthNm)
            {
                double t = (wavelengthNm - pts[i].WavelengthNm) /
                           (pts[i + 1].WavelengthNm - pts[i].WavelengthNm);
                return pts[i].Value + t * (pts[i + 1].Value - pts[i].Value);
            }
        }

        return pts[^1].Value;
    }

    private static double EstimateSlope(
        List<(double WavelengthNm, double Value)> pts,
        double wavelengthNm)
    {
        // Find the bracketing interval
        int idx = 0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (pts[i + 1].WavelengthNm >= wavelengthNm) { idx = i; break; }
            idx = i;
        }

        double dWl = pts[idx + 1].WavelengthNm - pts[idx].WavelengthNm;
        return dWl == 0 ? 0.0 : (pts[idx + 1].Value - pts[idx].Value) / dWl;
    }

    private static IDispersionModel BuildTabulated(
        MaterialDispersionDraft draft,
        double fallbackLossDbPerCm)
    {
        // For a purely tabulated model we still need n_eff points.
        // If the draft is typed "tabulated" but has polynomial effective-index coefficients,
        // fall back to polynomial for index but use the tabulated loss.
        var ei = draft.EffectiveIndex;
        var lossPoints = ExtractTabulatedLoss(draft.PropagationLossDbPerCm, fallbackLossDbPerCm);

        if (ei != null)
        {
            // Hybrid: polynomial n_eff + tabulated loss
            // Build a TabulatedDispersion from synthetic n_eff points over [λ₀-100, λ₀+100]
            double lam0 = draft.CenterWavelengthNm;
            var nEffPoints = new List<(double WavelengthNm, double Value)>
            {
                (lam0 - 100, ei.N0 + ei.N1 * (-100) + ei.N2 * 10000),
                (lam0,       ei.N0),
                (lam0 + 100, ei.N0 + ei.N1 * (100) + ei.N2 * 10000),
            };

            double? ng0 = draft.GroupIndex?.Ng0;
            List<(double, double)>? ngPoints = ng0.HasValue
                ? new List<(double, double)> { (lam0, ng0.Value) }
                : null;

            return new TabulatedDispersion(nEffPoints, ngPoints, lossPoints);
        }

        // Pure tabulated: need at least n_eff — use generic Si defaults as placeholders
        var defaultNEff = new List<(double, double)> { (draft.CenterWavelengthNm, 2.45) };
        return new TabulatedDispersion(defaultNEff, null, lossPoints);
    }

    private static List<(double WavelengthNm, double Value)> ExtractTabulatedLoss(
        LossDraft? lossDraft,
        double fallback)
    {
        if (lossDraft?.Points != null && lossDraft.Type?.ToLowerInvariant() == "tabulated")
        {
            var pts = ParseLossPoints(lossDraft.Points);
            if (pts.Count > 0)
                return pts;
        }

        return new List<(double, double)> { (1550.0, fallback) };
    }

    private static List<(double WavelengthNm, double Value)> ParseLossPoints(List<List<double>> raw)
    {
        var result = new List<(double, double)>(raw.Count);
        foreach (var pair in raw)
        {
            if (pair.Count >= 2)
                result.Add((pair[0], pair[1]));
        }
        return result;
    }
}
