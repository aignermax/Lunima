using Shouldly;

namespace UnitTests.Architecture;

/// <summary>
/// Guards that the shipped example designs (<c>examples/*.lun</c>) are bundled
/// into every release artifact. The app discovers them by walking up from its
/// base directory (<see cref="CAP.Avalonia.Services.ExampleDesignsService"/>),
/// so each packaging path must place an <c>examples/</c> folder beside the
/// executable — otherwise the Home screen's Examples section is silently empty
/// in installed builds — and every artifact is started before it is packaged.
/// </summary>
public class ExamplesPackagingTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void RepositoryExamplesDirectory_ContainsAtLeastOneDesign()
    {
        var examplesDir = Path.Combine(RepoRoot, "examples");

        Directory.Exists(examplesDir).ShouldBeTrue(
            "examples/ is missing — the release bundles would ship an empty Examples section.");
        Directory.GetFiles(examplesDir, "*.lun").ShouldNotBeEmpty(
            "examples/ contains no .lun designs — the release bundles would ship an empty Examples section.");
    }

    [Fact]
    public void PortableBuildJob_PublishesThroughTheSharedScript()
    {
        ReadWorkflow().ShouldContain("bash scripts/publish_desktop.sh portable ${{ matrix.rid }} publish", Case.Sensitive,
            "The portable win-x64/linux-x64 artifacts must be published (examples/ included) by " +
            "the script PackagedAppSmoke.yaml smoke-tests.");
    }

    [Fact]
    public void PublishScript_MergesExamplesBesideTheBinary()
    {
        ReadScript("publish_desktop.sh").ShouldContain(MergeExamplesInto("${OUTPUT_DIR}"), Case.Sensitive,
            "The publish already holds examples/*.lun; copying the folder itself nests it at " +
            "examples/examples/ and leaves examples/isa/ unreachable.");
    }

    [Fact]
    public void MsiBuildJob_PublishesWithExamples_BeforeHarvesting()
    {
        var workflow = ReadWorkflow();

        var publishIndex = workflow.IndexOf(
            "bash scripts/publish_desktop.sh msi win-x64 publish/win-x64-msi", StringComparison.Ordinal);
        publishIndex.ShouldBeGreaterThanOrEqualTo(0,
            "The MSI job must publish publish/win-x64-msi (examples/ included) through the shared script.");

        var harvestIndex = workflow.IndexOf("Generate-HarvestedFiles.ps1", StringComparison.Ordinal);
        harvestIndex.ShouldBeGreaterThan(publishIndex,
            "examples/ must be in the publish folder before Generate-HarvestedFiles.ps1 runs, otherwise the MSI misses them.");
    }

    [Fact]
    public void MacOsBundleScript_MergesExamplesBesideTheExecutable()
    {
        ReadScript("build_macos_bundle.sh").ShouldContain(MergeExamplesInto("${MACOS_DIR}"), Case.Sensitive,
            "The .app bundle must contain examples/ and examples/isa/ in Contents/MacOS (the app base directory).");
    }

    [Theory]
    [InlineData("smoke_published_app.py publish/${{ matrix.executable }}", "name: Upload artifact")]
    [InlineData("smoke_published_app.py publish/win-x64-msi/CAP.Desktop.exe", "Generate-HarvestedFiles.ps1")]
    [InlineData("smoke_published_app.py dist/Lunima.app/Contents/MacOS/CAP.Desktop", "name: Package unsigned .dmg")]
    public void EveryReleaseArtifact_IsStartedBeforeItIsPackaged(string smokeCommand, string packagingStep)
    {
        var workflow = ReadWorkflow();

        var smokeIndex = workflow.IndexOf(smokeCommand, StringComparison.Ordinal);
        smokeIndex.ShouldBeGreaterThanOrEqualTo(0, $"Build_Exe.yaml must run `{smokeCommand}`.");
        workflow.IndexOf(packagingStep, smokeIndex, StringComparison.Ordinal).ShouldBeGreaterThan(smokeIndex,
            $"`{smokeCommand}` must run before `{packagingStep}`, so a broken artifact fails the release.");
    }

    private static string MergeExamplesInto(string destination) =>
        $"cp -R \"${{REPO_ROOT}}/examples/.\" \"{destination}/examples/\"";

    private static string ReadScript(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "scripts", fileName));

    private static string ReadWorkflow()
    {
        return File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "Build_Exe.yaml"));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            // In a normal clone .git is a directory; in a git worktree it is a file.
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (.git directory or file).");
    }
}
