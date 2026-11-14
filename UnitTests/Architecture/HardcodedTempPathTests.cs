using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>
/// Test code must not write files under a literal <c>/tmp/</c> path (CLAUDE.md §1.2: no
/// hard-coded separators or locations). Such a write only works on a Unix machine where that
/// directory already exists: it throws on Windows and on a fresh CI runner, so a local scratch
/// harness that slips into a commit fails the xUnit check instead of the author's machine.
/// Literal <c>/tmp/…</c> strings used as deliberately non-existent inputs are not writes and
/// stay allowed.
/// </summary>
public class HardcodedTempPathTests
{
    private const string TestRoot = "UnitTests";

    private static readonly Regex LiteralTmpWrite = new(
        @"(\b(File|Directory)\.(Write|Append|Create|Copy|Move)\w*|\bnew\s+(StreamWriter|FileStream))\(\s*[@$]*""/tmp/",
        RegexOptions.Compiled);

    /// <summary>Every file write in the test project goes through a portable temp path.</summary>
    [Fact]
    public void TestSources_DoNotWriteToALiteralTmpPath()
    {
        var root = SourceSizePolicy.RepositoryRoot();
        var violations = Directory
            .GetFiles(Path.Combine(root, TestRoot), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => LiteralTmpWrite.IsMatch(entry.line))
                .Select(entry => $"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{entry.number}"))
            .ToList();

        violations.ShouldBeEmpty(
            "These test lines write to a hard-coded /tmp/ path, which fails on Windows and on CI " +
            "runners where the directory does not exist. Use Path.GetTempPath() / Path.Combine:\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>The pattern catches the scratch-harness shape it exists for and ignores read-only fixtures.</summary>
    [Theory]
    [InlineData("File.AppendAllText(\"/tmp/calib/results.txt\", text);", true)]
    [InlineData("File.WriteAllText( $\"/tmp/{name}.json\", json);", true)]
    [InlineData("Directory.CreateDirectory(\"/tmp/scratch\");", true)]
    [InlineData("using var w = new StreamWriter(\"/tmp/log.txt\");", true)]
    [InlineData("File.AppendAllText(Path.Combine(Path.GetTempPath(), \"x.txt\"), text);", false)]
    [InlineData("vm.PdkManager.RegisterPdk(\"Ghost Pdk\", \"/tmp/ghost.json\", isBundled: false);", false)]
    [InlineData("File.Exists(\"/tmp/does_not_exist.py\").ShouldBeFalse();", false)]
    public void LiteralTmpWritePattern_MatchesOnlyWrites(string line, bool expected) =>
        LiteralTmpWrite.IsMatch(line).ShouldBe(expected);

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}bin{separator}");
    }
}
