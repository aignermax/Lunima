namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Locates the vendored openEBL check scripts under <c>scripts/openebl/</c> — the same
/// files <see cref="CAP.Avalonia.Services.OpenEblCheck.OpenEblSubmissionChecker"/> runs —
/// so the tests exercise exactly what the product ships and no script text is embedded
/// in the test suite.
/// </summary>
internal static class OpenEblScriptFiles
{
    /// <summary>Absolute path of the vendored openebl_submission_check.py.</summary>
    internal static string SubmissionCheckScriptPath =>
        Path.Combine(ScriptsDirectory(), "openebl_submission_check.py");

    /// <summary>Absolute path of the vendored openebl_verification.py.</summary>
    internal static string VerificationScriptPath =>
        Path.Combine(ScriptsDirectory(), "openebl_verification.py");

    private static string ScriptsDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "openebl");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Repo scripts/openebl/ directory not found walking up from " + AppContext.BaseDirectory);
    }
}
