namespace CAP.Avalonia.Services.OpenEblCheck;

/// <summary>Overall outcome of an <see cref="OpenEblSubmissionChecker"/> run.</summary>
public enum OpenEblCheckStatus
{
    /// <summary>Both checks ran and reported zero errors.</summary>
    Passed,

    /// <summary>The checks ran; at least one reported errors (see the report's errors).</summary>
    Failed,

    /// <summary>
    /// No Python interpreter with klayout / siepic_ebeam_pdk / SiEPIC-Tools could be found
    /// or probed. The report carries an actionable message naming the pip packages.
    /// </summary>
    ToolchainMissing,

    /// <summary>The vendored check scripts (scripts/openebl/) were not found — broken install.</summary>
    ScriptsMissing,

    /// <summary>
    /// A check script exited abnormally, timed out, or produced unparseable output.
    /// Never reported as "passed".
    /// </summary>
    CheckCrashed,
}

/// <summary>Stable category names used for the typed error entries of a report.</summary>
public static class OpenEblCheckCategories
{
    /// <summary>Layout does not have exactly one top cell.</summary>
    public const string TopCell = "TopCell";

    /// <summary>Die bounding box exceeds the openEBL floorplan of 605 x 410 µm.</summary>
    public const string DieSize = "DieSize";

    /// <summary>No shapes on the openEBL floorplan layers (1,0)+(4,0).</summary>
    public const string Floorplan = "Floorplan";

    /// <summary>Unidentified black-box cells (leftover 998/0 shapes).</summary>
    public const string BlackBoxCells = "BlackBoxCells";

    /// <summary>A design layer is not defined in the SiEPIC EBeam PDK.</summary>
    public const string LayerConformity = "LayerConformity";

    /// <summary>Any other submission-check error line.</summary>
    public const string SubmissionCheck = "SubmissionCheck";

    /// <summary>SiEPIC layout_check aborted ("Unknown error occurred").</summary>
    public const string VerificationCrash = "VerificationCrash";
}

/// <summary>One typed error entry of an openEBL check run.</summary>
/// <param name="Category">Stable category (see <see cref="OpenEblCheckCategories"/>) or the
/// layout_check rule name for verification errors.</param>
/// <param name="Message">Human-readable description (verbatim script output where possible).</param>
/// <param name="XMicrometers">Optional error location in µm (not reported by the current scripts).</param>
/// <param name="YMicrometers">Optional error location in µm (not reported by the current scripts).</param>
public sealed record OpenEblCheckError(
    string Category,
    string Message,
    double? XMicrometers = null,
    double? YMicrometers = null);

/// <summary>Die extent in µm over the openEBL floorplan layers (1,0)+(4,0).</summary>
public sealed record OpenEblDieBoundingBox(double WidthMicrometers, double HeightMicrometers);

/// <summary>Typed result of <see cref="OpenEblSubmissionChecker.CheckAsync"/>.</summary>
public sealed record OpenEblCheckReport
{
    /// <summary>Overall outcome; <see cref="OpenEblCheckStatus.Passed"/> means zero errors on both checks.</summary>
    public required OpenEblCheckStatus Status { get; init; }

    /// <summary>True when the submission checks ran and reported zero errors.</summary>
    public required bool SubmissionChecksPassed { get; init; }

    /// <summary>True when the SiEPIC layout_check verification ran and reported zero errors.</summary>
    public required bool VerificationPassed { get; init; }

    /// <summary>Error count printed by the submission-check script (-1 when it did not run to completion).</summary>
    public int SubmissionErrorCount { get; init; } = -1;

    /// <summary>Error count printed by the verification script (-1 when it did not run to completion).</summary>
    public int VerificationErrorCount { get; init; } = -1;

    /// <summary>Typed error entries across both checks.</summary>
    public IReadOnlyList<OpenEblCheckError> Errors { get; init; } = Array.Empty<OpenEblCheckError>();

    /// <summary>Die extent on the floorplan layers, when the submission check reported one.</summary>
    public OpenEblDieBoundingBox? DieBoundingBox { get; init; }

    /// <summary>
    /// Actionable message naming the pip packages to install, set when (and only when)
    /// <see cref="Status"/> is <see cref="OpenEblCheckStatus.ToolchainMissing"/>.
    /// </summary>
    public string? ToolchainMessage { get; init; }

    /// <summary>Raw stdout of the submission-check script (diagnostics).</summary>
    public string SubmissionOutput { get; init; } = "";

    /// <summary>Raw stdout of the verification script (diagnostics).</summary>
    public string VerificationOutput { get; init; } = "";
}
