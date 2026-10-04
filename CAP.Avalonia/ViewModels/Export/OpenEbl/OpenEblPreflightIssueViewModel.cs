using CAP.Avalonia.Services.Localization;

namespace CAP.Avalonia.ViewModels.Export.OpenEbl;

/// <summary>
/// One row in the Lunima pre-flight list of the "Check for openEBL…" dialog: a finding the
/// local design checks surface before the external check runs (unrouted/blocked connections,
/// DRC-lite violations). Errors hold the run until "Check anyway"; warnings let it through.
/// </summary>
public sealed class OpenEblPreflightIssueViewModel
{
    /// <summary>Initializes a new instance of <see cref="OpenEblPreflightIssueViewModel"/>.</summary>
    public OpenEblPreflightIssueViewModel(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }

    /// <summary>Human-readable description of the finding (one line).</summary>
    public string Message { get; }

    /// <summary>True when the finding blocks the external check until "Check anyway".</summary>
    public bool IsError { get; }

    /// <summary>Localized severity word for the chip next to the message.</summary>
    public string SeverityLabel =>
        LocalizationService.Instance.Translate(IsError ? "OpenEblCheck.Preflight.Error" : "OpenEblCheck.Preflight.Warning");
}
