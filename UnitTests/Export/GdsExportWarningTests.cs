using System.Text.Json;
using CAP_Core.Export.Contracts;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

/// <summary>Exercises warning capture through real, successful Python subprocesses.</summary>
public sealed class GdsExportWarningTests
{
    private const string PlaceholderWarning = "[Lunima] WARN: SiEPIC real-geometry upgrade skipped (No module named 'klayout.db'); stub boxes kept.";
    private const string DevRecWarning = "[Lunima] WARN: DevRec emission skipped (No module named 'klayout.db').";

    [SkippableTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task ExitZero_PreservesWarningsAndReportsDegradedExport(string newline)
    {
        var stderr = "harmless diagnostic" + newline + PlaceholderWarning + newline + DevRecWarning + newline;
        var result = await ExportAsync(stderr);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.GdsPath.ShouldNotBeNull();
        result.ErrorMessage.ShouldBeNull();
        result.Warnings.ShouldBe(new[] { PlaceholderWarning, DevRecWarning });
        result.Status.ShouldContain("exported with warnings");
        result.Status.ShouldContain(PlaceholderWarning);
        result.Status.ShouldContain(DevRecWarning);
        result.Status.ShouldNotContain("exported successfully");
    }

    [SkippableTheory]
    [InlineData("")]
    [InlineData("A harmless library diagnostic\n")]
    public async Task ExitZero_WithoutLunimaWarnings_PreservesCleanSuccess(string stderr)
    {
        var result = await ExportAsync(stderr);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldBeEmpty();
        result.Status.ShouldBe("Script and GDS exported successfully");
    }

    [SkippableFact]
    public async Task NonzeroExit_PreservesWarningsAlongsideFailure()
    {
        var result = await ExportAsync(PlaceholderWarning, exitCode: 7);
        result.Success.ShouldBeFalse();
        result.GdsPath.ShouldBeNull();
        result.Warnings.ShouldBe(new[] { PlaceholderWarning });
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage!.ShouldContain("exit code 7");
    }

    [SkippableFact]
    public async Task ExitZero_WithoutGds_PreservesWarningsAlongsideFailure()
    {
        var result = await ExportAsync(PlaceholderWarning, writeGds: false);
        result.Success.ShouldBeFalse();
        result.GdsPath.ShouldBeNull();
        result.Warnings.ShouldBe(new[] { PlaceholderWarning });
        result.Status.ShouldBe("GDS generation failed");
    }

    private static async Task<GdsExportResult> ExportAsync(string stderr, int exitCode = 0, bool writeGds = true)
    {
        await using var child = await ControlledSolverProcess.CreateAsync($$"""
            import pathlib
            if {{(writeGds ? "True" : "False")}}:
                pathlib.Path(sys.argv[0]).with_suffix('.gds').write_bytes(b'controlled-gds')
            sys.stderr.write({{JsonSerializer.Serialize(stderr)}})
            sys.stderr.flush()
            os._exit({{exitCode}})
            """);
        var service = new InstantEnvironmentGdsExportService();
        service.SetCustomPythonPath(child.PythonExecutable);
        var result = await service.ExportToGdsAsync(child.ScriptPath, true)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        File.Exists(Path.ChangeExtension(child.ScriptPath, ".gds")).ShouldBe(writeGds);
        child.ShouldHaveExited();
        return result;
    }
}
