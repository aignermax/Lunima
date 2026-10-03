using System.Diagnostics;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.OpenEblCheck;

/// <summary>
/// Headless openEBL submission checker: runs the two vendored klayout-only check
/// scripts (<c>scripts/openebl/openebl_submission_check.py</c> and
/// <c>scripts/openebl/openebl_verification.py</c>) against an exported GDS file and
/// returns a typed <see cref="OpenEblCheckReport"/>. Async and cancellable; nothing
/// touches the UI thread. A missing toolchain (klayout / siepic_ebeam_pdk /
/// SiEPIC-Tools not importable) yields <see cref="OpenEblCheckStatus.ToolchainMissing"/>
/// with an actionable pip hint — never a crash and never a false "passed".
/// </summary>
public sealed class OpenEblSubmissionChecker
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(5);

    private const string SubmissionScriptFileName = "openebl_submission_check.py";
    private const string VerificationScriptFileName = "openebl_verification.py";

    private readonly ProcessLaunchFactory _factory;
    private readonly OpenEblPythonLocator _pythonLocator;
    private readonly string? _pythonPathOverride;
    private readonly string? _scriptsDirectoryOverride;

    /// <summary>
    /// Initializes the checker. Production resolves the shared
    /// <see cref="ProcessLaunchFactory"/> singleton from DI; the optional overrides exist
    /// for tests and for a user-configured interpreter.
    /// </summary>
    /// <param name="factory">Process launch factory (defaults to <see cref="ProcessLaunchFactory.CreateDefault"/>).</param>
    /// <param name="pythonPath">Explicit interpreter to use instead of auto-discovery.</param>
    /// <param name="scriptsDirectory">
    /// Explicit directory containing the vendored scripts, instead of locating
    /// <c>scripts/openebl</c> by walking up from the app base directory.
    /// </param>
    public OpenEblSubmissionChecker(
        ProcessLaunchFactory? factory = null,
        string? pythonPath = null,
        string? scriptsDirectory = null)
    {
        _factory = factory ?? ProcessLaunchFactory.CreateDefault();
        _pythonLocator = new OpenEblPythonLocator(_factory);
        _pythonPathOverride = pythonPath;
        _scriptsDirectoryOverride = scriptsDirectory;
    }

    /// <summary>
    /// Runs both openEBL checks against <paramref name="gdsPath"/>. The returned report
    /// always reflects reality: only zero parsed errors on both scripts is a pass.
    /// </summary>
    /// <param name="gdsPath">Path to the exported GDS file.</param>
    /// <param name="cancellationToken">Cancels the run (the Python process tree is killed).</param>
    public async Task<OpenEblCheckReport> CheckAsync(
        string gdsPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gdsPath) || !File.Exists(gdsPath))
            throw new FileNotFoundException("GDS file to check was not found.", gdsPath);

        var scriptsDirectory = ResolveScriptsDirectory();
        if (scriptsDirectory == null)
            return new OpenEblCheckReport
            {
                Status = OpenEblCheckStatus.ScriptsMissing,
                SubmissionChecksPassed = false,
                VerificationPassed = false,
            };

        var python = await ResolvePythonAsync(cancellationToken).ConfigureAwait(false);
        if (python == null)
            return new OpenEblCheckReport
            {
                Status = OpenEblCheckStatus.ToolchainMissing,
                SubmissionChecksPassed = false,
                VerificationPassed = false,
                ToolchainMessage =
                    "The openEBL check needs a Python toolchain with klayout, the SiEPIC EBeam " +
                    "PDK and SiEPIC-Tools. Install them with: " + OpenEblPythonLocator.PipInstallHint,
            };

        var submission = await RunScriptAsync(
            python, Path.Combine(scriptsDirectory, SubmissionScriptFileName), gdsPath, cancellationToken)
            .ConfigureAwait(false);
        var verification = await RunScriptAsync(
            python, Path.Combine(scriptsDirectory, VerificationScriptFileName), gdsPath, cancellationToken)
            .ConfigureAwait(false);

        return BuildReport(submission, verification);
    }

    private static OpenEblCheckReport BuildReport(
        ScriptRun? submission, ScriptRun? verification)
    {
        if (submission == null || verification == null)
        {
            return new OpenEblCheckReport
            {
                Status = OpenEblCheckStatus.CheckCrashed,
                SubmissionChecksPassed = false,
                VerificationPassed = false,
                SubmissionOutput = submission?.StdOut ?? "",
                VerificationOutput = verification?.StdOut ?? "",
            };
        }

        var (submissionErrors, submissionEntries, boundingBox) =
            OpenEblReportParser.ParseSubmissionOutput(submission.StdOut);
        var (verificationErrors, verificationEntries) =
            OpenEblReportParser.ParseVerificationOutput(verification.StdOut);

        var crashed = submission.ExitCode != 0 || verification.ExitCode != 0
            || submissionErrors < 0 || verificationErrors < 0;
        var errors = submissionEntries.Concat(verificationEntries).ToList();
        var passed = !crashed && submissionErrors == 0 && verificationErrors == 0;

        return new OpenEblCheckReport
        {
            Status = crashed
                ? OpenEblCheckStatus.CheckCrashed
                : passed ? OpenEblCheckStatus.Passed : OpenEblCheckStatus.Failed,
            SubmissionChecksPassed = submissionErrors == 0 && submission.ExitCode == 0,
            VerificationPassed = verificationErrors == 0 && verification.ExitCode == 0,
            SubmissionErrorCount = submissionErrors,
            VerificationErrorCount = verificationErrors,
            Errors = errors,
            DieBoundingBox = boundingBox,
            SubmissionOutput = submission.StdOut,
            VerificationOutput = verification.StdOut,
        };
    }

    private async Task<string?> ResolvePythonAsync(CancellationToken cancellationToken)
    {
        if (_pythonPathOverride != null)
            return await _pythonLocator.ProbeAsync(_pythonPathOverride, cancellationToken)
                .ConfigureAwait(false)
                ? _pythonPathOverride
                : null;
        return await _pythonLocator.FindPythonAsync(cancellationToken).ConfigureAwait(false);
    }

    private string? ResolveScriptsDirectory()
    {
        if (_scriptsDirectoryOverride != null)
            return Directory.Exists(_scriptsDirectoryOverride) ? _scriptsDirectoryOverride : null;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "openebl");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
        return null;
    }

    private sealed record ScriptRun(int ExitCode, string StdOut, string StdErr);

    private async Task<ScriptRun?> RunScriptAsync(
        string python, string scriptPath, string gdsPath, CancellationToken cancellationToken)
    {
        if (!_factory.TryBuild(
                python, new[] { scriptPath, gdsPath }, Path.GetDirectoryName(gdsPath), null,
                out var startInfo, out _))
            return null;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        Process process;
        try
        {
            process = Process.Start(startInfo)!;
        }
        catch (Exception)
        {
            return null;   // interpreter vanished between probe and launch
        }

        using (process)
        {
            var stdOut = process.StandardOutput.ReadToEndAsync();
            var stdErr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ScriptTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                cancellationToken.ThrowIfCancellationRequested();
                return null;   // timed out → CheckCrashed, never a silent pass
            }
            return new ScriptRun(process.ExitCode, await stdOut, await stdErr);
        }
    }
}
