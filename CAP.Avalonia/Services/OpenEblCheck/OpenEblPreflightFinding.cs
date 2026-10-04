namespace CAP.Avalonia.Services.OpenEblCheck;

/// <summary>
/// One Lunima pre-flight finding of the "Check for openEBL…" dialog (issue #1375): a
/// one-line message plus the severity. Errors hold the external check until "Check anyway";
/// warnings only annotate its result.
/// </summary>
/// <param name="Message">Human-readable description of the finding.</param>
/// <param name="IsError">True when the finding blocks the external check until "Check anyway".</param>
public sealed record OpenEblPreflightFinding(string Message, bool IsError);
