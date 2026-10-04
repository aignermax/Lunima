using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Services.OpenEblCheck;

/// <summary>
/// Lunima-side pre-flight of the "Check for openEBL…" dialog (issue #1375): runs
/// <see cref="DesignValidator"/> on the canvas connections and flags unrouted connections
/// before the ~20 s external check would fail with cryptic klayout messages. Pure
/// computation — safe to invoke from a worker thread.
/// </summary>
public static class OpenEblPreflightChecker
{
    /// <summary>
    /// Gathers the pre-flight findings: every connection without a valid routed path that the
    /// router did not already mark as a blocked fallback, plus every per-connection finding of
    /// <see cref="DesignValidator"/> mapped to error/warning severity.
    /// </summary>
    public static List<OpenEblPreflightFinding> Collect(IReadOnlyList<WaveguideConnection> connections)
    {
        var findings = new List<OpenEblPreflightFinding>();

        foreach (var connection in connections)
        {
            if (!connection.IsPathValid && !connection.IsBlockedFallback)
                findings.Add(new OpenEblPreflightFinding(FormatUnrouted(connection), IsError: true));
        }

        foreach (var issue in new DesignValidator().Validate(connections))
            findings.Add(new OpenEblPreflightFinding(DesignIssueFormatter.Format(issue), IsErrorType(issue.Type)));

        return findings;
    }

    /// <summary>Severity mapping: findings that export broken or flat geometry are errors.</summary>
    private static bool IsErrorType(DesignIssueType type) => type switch
    {
        DesignIssueType.InvalidGeometry
            or DesignIssueType.BlockedPath
            or DesignIssueType.OverlappingPaths
            or DesignIssueType.PinMismatch
            or DesignIssueType.StyledRouteThroughComponent
            or DesignIssueType.ComponentFootprintOverlap
            or DesignIssueType.WaveguideCrossing => true,
        _ => false,
    };

    private static string FormatUnrouted(WaveguideConnection connection) =>
        string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Instance.Translate("OpenEblCheck.Preflight.UnroutedConnection"),
            FormatPinName(connection.StartPin),
            FormatPinName(connection.EndPin));

    private static string FormatPinName(CAP_Core.Components.Core.PhysicalPin pin) =>
        $"{pin.ParentComponent.Identifier}.{pin.Name}";
}
