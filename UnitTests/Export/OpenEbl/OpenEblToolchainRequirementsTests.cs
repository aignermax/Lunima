using System.Text.RegularExpressions;
using CAP_Core.Export.Contracts;
using CAP_Core.Export.OpenEbl;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Pins the pip packages Lunima tells a user to install for the openEBL check. One
/// unresolvable name makes pip abort and install nothing, so the hint must name real
/// PyPI distributions — exactly the ones the shipped scripts import. "SiEPIC-Tools" is
/// the project's name, not a PyPI package; <c>import SiEPIC</c> comes from <c>SiEPIC</c>.
/// </summary>
public class OpenEblToolchainRequirementsTests
{
    private static readonly string[] ExpectedPackages = { "klayout", "siepic_ebeam_pdk", "SiEPIC" };

    private static readonly Dictionary<string, string> DistributionByModule =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["klayout"] = "klayout",
            ["siepic_ebeam_pdk"] = "siepic_ebeam_pdk",
            ["SiEPIC"] = "SiEPIC",
        };

    private static readonly HashSet<string> StandardLibraryModules = new(StringComparer.Ordinal)
    {
        "argparse", "collections", "itertools", "json", "math", "os", "pathlib", "re",
        "shutil", "subprocess", "sys", "tempfile", "xml",
    };

    private static readonly Regex RequiresCommentPattern =
        new(@"^#\s*Requires \(pip\):\s*(?<packages>.+?)\s*$", RegexOptions.Multiline);

    private static readonly string CiToolSetupRelativePath =
        Path.Combine(".github", "actions", "setup-test-tools", "action.yml");

    [Fact]
    public void PipInstallCommand_InstallsExactlyKlayoutSiepicEbeamPdkAndSiepic()
    {
        PackagesOf(OpenEblToolchainRequirements.PipInstallCommand).ShouldBe(ExpectedPackages);
        OpenEblToolchainRequirements.PipPackages.ShouldBe(ExpectedPackages);
        OpenEblPythonLocator.PipInstallHint.ShouldBe(OpenEblToolchainRequirements.PipInstallCommand);
    }

    [Fact]
    public void ToolchainHint_NeverNamesTheNonexistentSiepicToolsDistribution()
    {
        OpenEblToolchainRequirements.PipInstallCommand.ShouldNotContain("SiEPIC-Tools", Case.Insensitive);
        OpenEblToolchainRequirements.ToolchainMissingMessage.ShouldNotContain("SiEPIC-Tools", Case.Insensitive);
        OpenEblToolchainRequirements.ToolchainMissingMessage
            .ShouldEndWith(OpenEblToolchainRequirements.PipInstallCommand);
    }

    [Fact]
    public void PipPackages_AreExactlyTheDistributionsTheShippedScriptsImport()
    {
        var imported = ShippedScripts().SelectMany(ThirdPartyDistributionsOf).Distinct();

        Sorted(imported).ShouldBe(Sorted(OpenEblToolchainRequirements.PipPackages),
            "the install hint must name every PyPI distribution scripts/openebl/*.py imports, and nothing else");
    }

    [Theory]
    [InlineData("openebl_submission_check.py")]
    [InlineData("openebl_verification.py")]
    public void ScriptRequiresComment_NamesTheDistributionsTheScriptImports(string scriptFileName)
    {
        var script = ScriptPath(scriptFileName);
        var matches = RequiresCommentPattern.Matches(File.ReadAllText(script));

        matches.Count.ShouldBe(1, $"{scriptFileName} must carry exactly one '# Requires (pip): …' line");
        var declared = matches[0].Groups["packages"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Sorted(declared).ShouldBe(Sorted(ThirdPartyDistributionsOf(script)));
    }

    [Fact]
    public void PythonLocatorProbe_ImportsAModuleOfEveryHintPackage()
    {
        var probed = ModulesImportedBy(OpenEblPythonLocator.RequiredImports)
            .Select(module => DistributionByModule[module]);

        Sorted(probed).ShouldBe(Sorted(OpenEblToolchainRequirements.PipPackages));
    }

    [Fact]
    public void CiToolSetup_InstallsEveryHintPackageFromPyPi()
    {
        var installedByCi = File.ReadAllLines(FindRepoFile(CiToolSetupRelativePath))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("pip install ", StringComparison.Ordinal))
            .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var package in OpenEblToolchainRequirements.PipPackages)
            installedByCi.ShouldContain(package,
                $"CI must pip-install '{package}' so a name PyPI cannot resolve fails the build");
    }

    private static string[] PackagesOf(string pipInstallCommand)
    {
        const string prefix = "pip install ";
        pipInstallCommand.ShouldStartWith(prefix);
        return pipInstallCommand[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> ShippedScripts() => new[]
    {
        OpenEblScriptFiles.SubmissionCheckScriptPath,
        OpenEblScriptFiles.VerificationScriptPath,
    };

    private static string ScriptPath(string scriptFileName) =>
        Path.Combine(Path.GetDirectoryName(OpenEblScriptFiles.SubmissionCheckScriptPath)!, scriptFileName);

    private static IEnumerable<string> ThirdPartyDistributionsOf(string scriptPath)
    {
        var modules = File.ReadAllLines(scriptPath)
            .SelectMany(ModulesImportedBy)
            .Where(module => !StandardLibraryModules.Contains(module))
            .Distinct()
            .ToList();
        foreach (var module in modules)
            DistributionByModule.ShouldContainKey(module,
                $"{Path.GetFileName(scriptPath)} imports '{module}': add its PyPI distribution to " +
                "OpenEblToolchainRequirements and this test (or to StandardLibraryModules if it is stdlib)");
        return modules.Select(module => DistributionByModule[module]).Distinct();
    }

    private static IEnumerable<string> ModulesImportedBy(string pythonLine)
    {
        var code = pythonLine.Split('#')[0].Trim();
        if (code.StartsWith("from ", StringComparison.Ordinal))
            return new[] { TopLevelModule(code["from ".Length..]) };
        if (!code.StartsWith("import ", StringComparison.Ordinal))
            return Array.Empty<string>();
        return code["import ".Length..].Split(',').Select(TopLevelModule);
    }

    private static string TopLevelModule(string dottedImport) =>
        dottedImport.Trim().Split(' ')[0].Split('.')[0];

    private static IEnumerable<string> Sorted(IEnumerable<string> packages) =>
        packages.OrderBy(package => package, StringComparer.Ordinal).ToList();

    private static string FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException(
            $"{relativePath} not found walking up from {AppContext.BaseDirectory}");
    }
}
