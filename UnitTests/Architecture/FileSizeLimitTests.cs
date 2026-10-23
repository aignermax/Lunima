using System.Text;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>All authored source follows the shared 999-line ceiling, without basename exemptions.</summary>
public sealed class FileSizeLimitTests
{
    private static SourceSizePolicy Policy => SourceSizePolicy.Load(SourceSizePolicy.RepositoryRoot());

    [Fact]
    public async Task NoAuthoredSourceExceedsHardLimit()
    {
        var root = SourceSizePolicy.RepositoryRoot();
        var policy = SourceSizePolicy.Load(root);
        policy.MaxLines.ShouldBe(999);
        var violations = new List<string>();
        foreach (var relativePath in await SourceSizePolicy.TrackedFilesAsync(root))
        {
            var path = Path.Combine(root, relativePath);
            if (!policy.IsSource(relativePath) || !File.Exists(path)) continue; // Working-tree deletions match CI's checker.
            if (policy.Violation(relativePath, await File.ReadAllBytesAsync(path)) is { } violation)
                violations.Add(violation);
        }
        violations.ShouldBeEmpty("Extract focused responsibilities; tests, views, scripts and CI are source too.\n" +
            string.Join("\n", violations));
    }

    [Theory]
    [InlineData(999, true, false)]
    [InlineData(999, false, false)]
    [InlineData(1000, true, true)]
    [InlineData(1000, false, true)]
    public void Ceiling_CountsPhysicalLinesWithOrWithoutFinalNewline(int lines, bool finalNewline, bool rejected)
    {
        var text = string.Join("\n", Enumerable.Repeat("// source", lines)) + (finalNewline ? "\n" : "");
        SourceSizePolicy.PhysicalLines(Encoding.UTF8.GetBytes(text)).ShouldBe(lines);
        (Policy.Violation("Feature.cs", Encoding.UTF8.GetBytes(text)) != null).ShouldBe(rejected);
    }

    [Theory]
    [InlineData("UnitTests/FeatureTests.cs")]
    [InlineData("Views/MainWindow.axaml")]
    [InlineData("Other/MainWindow.axaml.cs")]
    [InlineData("Services/SimpleNazcaExporter.cs")]
    [InlineData("scripts/task.py")]
    [InlineData("scripts/task.ps1")]
    [InlineData("docs/index.html")]
    [InlineData(".github/workflows/test.yml")]
    [InlineData(".githooks/pre-commit")]
    public void EveryAuthoredSourceArea_IsCheckedWithoutLegacyBasenameExemptions(string path)
    {
        Policy.Violation(path, Encoding.UTF8.GetBytes(new string('\n', 1000)))
            .ShouldBe($"{path}: 1000 lines (maximum 999)");
    }

    [Fact]
    public void UnicodeSeparators_DoNotInventPhysicalLines()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("// a\u2028b\n", 999)));
        SourceSizePolicy.PhysicalLines(bytes).ShouldBe(999);
        Policy.Violation("Feature.cs", bytes).ShouldBeNull();
    }

    [Fact]
    public void BinaryContentCannotHideInAnAuthoredSourceExtension() =>
        Policy.Violation("Feature.cs", [(byte)'a', 0, (byte)'b']).ShouldBe("Feature.cs: binary content in a source file");

    [Fact]
    public void DataFilesAreOutsideTheAuthoredSourcePolicy() =>
        Policy.Violation("fixtures/data.json", new byte[1000]).ShouldBeNull();
}
