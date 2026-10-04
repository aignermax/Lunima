using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis;

namespace CAP.Avalonia.Services;

/// <summary>
/// Renders a <see cref="DesignIssue"/> for display in the user's active UI language.
/// Single shared entry point so every consumer (Design Checks panel, pre-flight
/// reports, export warning lists, …) formats issues identically.
/// </summary>
public static class DesignIssueFormatter
{
    /// <summary>
    /// Returns the issue's message in the active UI language. Falls back to the
    /// English <see cref="DesignIssue.Description"/> when the issue carries no
    /// localization key or when the key is missing from every string table.
    /// Numeric args are formatted with the current UI culture (display strings only;
    /// machine output uses <see cref="CultureInfo.InvariantCulture"/> at the source).
    /// </summary>
    public static string Format(DesignIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return Format(issue, LocalizationService.Instance);
    }

    /// <summary>Test-friendly overload with an explicit localization service.</summary>
    public static string Format(DesignIssue issue, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(localization);

        if (issue.LocalizationKey is null)
            return issue.Description;

        var template = localization.Translate(issue.LocalizationKey);
        if (ReferenceEquals(template, issue.LocalizationKey))
            return issue.Description; // key missing in every table — never leak the key

        if (issue.LocalizationArgs is not { Count: > 0 } args)
            return template;

        // Use the *display language's* culture for numbers, not the OS culture — a user
        // running a de-DE machine with the UI forced to English expects "5.0", not "5,0".
        var culture = CultureInfo.GetCultureInfo(localization.ActiveLanguageCode);
        return string.Format(culture, template, args as object[] ?? args.ToArray());
    }
}
