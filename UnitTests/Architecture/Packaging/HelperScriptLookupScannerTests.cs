using Shouldly;
using Xunit;

namespace UnitTests.Architecture.Packaging;

/// <summary>Pins what <see cref="HelperScriptLookupScanner"/> finds, so the shipping guard can't miss a lookup.</summary>
public sealed class HelperScriptLookupScannerTests : IDisposable
{
    private readonly ScratchRepository _repository = new();

    /// <inheritdoc />
    public void Dispose() => _repository.Dispose();

    [Fact]
    public void ScanSource_ResolvesEveryLookupStyle_AndSkipsComments()
    {
        const string Source = """
            /// PythonResolution.FindScript("documented_only.py")
            var a = PythonResolution.FindScript("fdtd", "Dockerfile");
            var b = Path.Combine(baseDir, "..", "scripts", "parse_pdk.py");
            const string C = "scripts/extract_gds_coords.py";
            var d = PythonResolution.FindScript(scriptName);
            """;

        HelperScriptLookupScanner.ScanSource(Source, "X.cs").ShouldBe(new[]
        {
            new HelperScriptLookup("scripts/fdtd/Dockerfile", "X.cs:2"),
            new HelperScriptLookup(null, "X.cs:5"),
            new HelperScriptLookup("scripts/parse_pdk.py", "X.cs:3"),
            new HelperScriptLookup("scripts/extract_gds_coords.py", "X.cs:4"),
        });
    }

    [Fact]
    public void Scan_ReadsProductionSources_ButNotBuildOutputOrOtherProjects()
    {
        const string Lookup = "\nvar s = PythonResolution.FindScript(\"{0}\");\n";
        _repository.Write(Path.Combine("CAP.Avalonia", "DI", "Feature.cs"), string.Format(Lookup, "shipped.py"));
        _repository.Write(Path.Combine("CAP.Avalonia", "obj", "Generated.cs"), string.Format(Lookup, "generated.py"));
        _repository.Write(Path.Combine("UnitTests", "Fixture.cs"), string.Format(Lookup, "test_only.py"));

        HelperScriptLookupScanner.Scan(_repository.Root).ShouldBe(new[]
        {
            new HelperScriptLookup("scripts/shipped.py", "CAP.Avalonia/DI/Feature.cs:2"),
        });
    }
}
