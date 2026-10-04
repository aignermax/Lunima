using CAP.Avalonia.Services.OpenEblCheck;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Packaging tests for the vendored openEBL check scripts. The scripts must ship
/// under <c>scripts/openebl/</c> next to the executable in every release bundle;
/// the test output directory mirrors that layout, so it serves as a proxy for the
/// published build output. The second test pins the runtime resolution:
/// <see cref="OpenEblSubmissionChecker"/> must find the scripts relative to
/// <see cref="AppContext.BaseDirectory"/> without any override — exactly the
/// situation in an installed build.
/// </summary>
public class OpenEblScriptPackagingTests
{
    [Theory]
    [InlineData("openebl_submission_check.py")]
    [InlineData("openebl_verification.py")]
    public void BuildOutput_ShipsOpenEblScript_UnderScriptsOpenEblDirectory(string scriptFileName)
    {
        var scriptPath = Path.Combine(
            AppContext.BaseDirectory, "scripts", "openebl", scriptFileName);

        File.Exists(scriptPath).ShouldBeTrue(
            $"the build output must ship '{scriptFileName}' at {scriptPath}");
    }

    [Fact]
    public async Task CheckAsync_ScriptsNextToExecutable_ResolvesThemFromAppBaseDirectory()
    {
        var gdsPath = Path.Combine(
            Path.GetTempPath(), "lunima-openebl-packaging-" + Guid.NewGuid().ToString("N") + ".gds");
        await File.WriteAllTextAsync(gdsPath, "not a real gds");
        try
        {
            var checker = new OpenEblSubmissionChecker(
                ProcessLaunchFactory.CreateDefault(),
                pythonPath: Path.Combine(Path.GetTempPath(), "lunima-no-such-python.exe"));

            var report = await checker.CheckAsync(gdsPath);

            // The nonexistent interpreter yields ToolchainMissing only after the
            // scripts were found; ScriptsMissing would mean the resolver did not
            // locate scripts/openebl relative to AppContext.BaseDirectory.
            report.Status.ShouldBe(OpenEblCheckStatus.ToolchainMissing);
        }
        finally
        {
            File.Delete(gdsPath);
        }
    }
}
