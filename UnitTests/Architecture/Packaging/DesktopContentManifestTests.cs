using Shouldly;
using Xunit;

namespace UnitTests.Architecture.Packaging;

/// <summary>Pins how <see cref="DesktopContentManifest"/> evaluates Content items, so the guard can't invent a shipped file.</summary>
public sealed class DesktopContentManifestTests : IDisposable
{
    private readonly ScratchRepository _repository = new();

    /// <inheritdoc />
    public void Dispose() => _repository.Dispose();

    [Fact]
    public void Load_ExpandsLiteralsAndWildcards_AtTheirLink()
    {
        WriteFiles("scripts/a.py", "scripts/drc/x.lydrc", "scripts/drc/notes.txt", "examples/isa/p.asm", "examples/m.lun");
        var manifest = LoadProject("""
            <Content Include="..\scripts\a.py" Link="scripts\a.py" CopyToPublishDirectory="PreserveNewest" />
            <Content Include="..\scripts\drc\*.lydrc" Link="scripts\drc\%(Filename)%(Extension)" CopyToPublishDirectory="Always" />
            <Content Include="..\examples\**\*.*" Link="examples\%(RecursiveDir)%(Filename)%(Extension)" CopyToPublishDirectory="PreserveNewest" />
            """);

        manifest.ShippedFiles.Order(StringComparer.Ordinal).ShouldBe(new[]
        {
            "examples/isa/p.asm", "examples/m.lun", "scripts/a.py", "scripts/drc/x.lydrc",
        });
        manifest.Ships("scripts/drc").ShouldBeTrue();
        manifest.Ships("scripts/dr").ShouldBeFalse();
    }

    [Fact]
    public void Load_IgnoresItemsThatDoNotPublishOnEveryOs()
    {
        WriteFiles("scripts/output_only.py", "scripts/never.py", "scripts/windows.py");
        var manifest = LoadProject("""
            <Content Include="..\scripts\output_only.py" Link="scripts\output_only.py" CopyToOutputDirectory="PreserveNewest" />
            <Content Include="..\scripts\never.py" Link="scripts\never.py" CopyToPublishDirectory="Never" />
            <Content Include="..\scripts\windows.py" Link="scripts\windows.py" CopyToPublishDirectory="PreserveNewest"
                     Condition="$(RuntimeIdentifier.StartsWith('win'))" />
            <Content Include="..\scripts\absent.py" Link="scripts\absent.py" CopyToPublishDirectory="PreserveNewest" />
            """);

        manifest.ShippedFiles.ShouldBeEmpty();
    }

    [Fact]
    public void Load_RejectsGlobsItCannotModel()
    {
        WriteFiles("scripts/a/b/c.py");

        Should.Throw<NotSupportedException>(() => LoadProject("""
            <Content Include="..\scripts\*\b\*.py" CopyToPublishDirectory="PreserveNewest" />
            """));
    }

    private void WriteFiles(params string[] relativePaths)
    {
        foreach (var relative in relativePaths)
            _repository.Write(Path.Combine(relative.Split('/')));
    }

    private DesktopContentManifest LoadProject(string items)
    {
        var project = _repository.Write(Path.Combine("App", "App.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{items}</ItemGroup></Project>");
        return DesktopContentManifest.Load(project);
    }
}
