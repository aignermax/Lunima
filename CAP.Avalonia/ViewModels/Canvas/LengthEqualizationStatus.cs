using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Routing.LengthEqualization;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>Localized status lines of the "Equalize lengths" section.</summary>
internal static class LengthEqualizationStatus
{
    /// <summary>
    /// The outcome of a run: how many routes reached which length (re-laid as a bundle or
    /// stretched), the ones outside the bundle, and a width change when the widths were unified.
    /// </summary>
    public static string Describe(LengthEqualizationResult result, double? unifiedWidth)
    {
        string message = Headline(result);
        if (result.NotInBundleCount > 0)
            message += Format("Routing.LengthEqualize.NotInBundle", result.NotInBundleCount);
        if (unifiedWidth is { } width)
            message += Format("Routing.LengthEqualize.WidthUnified", width);
        return message;
    }

    /// <summary>Formats a localized string in the current culture.</summary>
    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance.Translate(key), arguments);

    private static string Headline(LengthEqualizationResult result)
    {
        double seconds = result.Elapsed.TotalSeconds;
        if (result.FailedCount > 0)
            return Format("Routing.LengthEqualize.Partial", result.MatchedCount, result.MatchedCount + result.FailedCount,
                result.TargetLengthMicrometers, seconds, result.FailedCount);
        if (result.ReroutedAsBundle)
            return Format("Routing.LengthEqualize.SuccessBundle", result.MatchedCount, result.TargetLengthMicrometers,
                result.Outcomes.Where(o => o.IsSuccess).Select(o => o.RadiusMicrometers).DefaultIfEmpty().Max(), seconds);
        return Format("Routing.LengthEqualize.Success", result.MatchedCount, result.TargetLengthMicrometers, seconds);
    }
}
