using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;

namespace UnitTests.Architecture;

public sealed class BundledPdkDirectoryAliasTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lunima-pdk-alias-{Guid.NewGuid():N} fixture");
    private readonly List<string> _links = new();
    private string Base => Path.Combine(_root, "app", "bin");
    private string Shipped => Path.Combine(Base, "PDKs");
    private string Repo => Path.Combine(_root, "CAP-DataAccess", "PDKs");

    [Theory]
    [InlineData("shipped")]
    [InlineData("repository")]
    [InlineData("ancestor")]
    [InlineData("linked-candidate")]
    public async Task SaveThroughDirectoryAlias_RejectsBundledOverwriteWithoutChangingFoundry(string kind)
    {
        var (physical, alias) = await CreateProtectedAliasAsync(kind);
        var source = Path.Combine(physical, "foundry.json");
        const string original = "{\"Name\":\"Original foundry\"}";
        File.WriteAllText(source, original);
        var destination = Path.Combine(alias, "foundry.json");
        var catalog = new BundledPdkCatalog(Base);

        var error = Record.Exception(() => new PdkJsonSaver(catalog)
            .SaveToFile(new PdkDraft { Name = "Must remain a user fork" }, destination));

        error.ShouldBeOfType<UnauthorizedAccessException>();
        File.ReadAllText(source).ShouldBe(original);
        File.Exists(source + ".tmp").ShouldBeFalse();
        catalog.IsBundledFile(destination).ShouldBeTrue();
    }

    [Fact]
    public async Task NewFileThroughAncestorAlias_IsRejectedBeforeAnyFileIsCreated()
    {
        var (physical, alias) = await CreateProtectedAliasAsync("ancestor");
        var target = Path.Combine(alias, "new-foundry.json");

        Should.Throw<UnauthorizedAccessException>(() => new PdkJsonSaver(new BundledPdkCatalog(Base))
            .SaveToFile(new PdkDraft { Name = "New" }, target));

        Directory.GetFiles(physical).ShouldBeEmpty();
    }

    [Fact]
    public async Task UserForkThroughDirectoryAlias_RemainsWritableAndDoesNotChangeBundledSource()
    {
        Directory.CreateDirectory(Shipped);
        var source = Path.Combine(Shipped, "foundry.json");
        File.WriteAllText(source, "bundled source");
        var userDirectory = Path.Combine(_root, "user-pdks");
        Directory.CreateDirectory(userDirectory);
        var alias = await CreateAliasAsync("user-alias", userDirectory);
        var catalog = new BundledPdkCatalog(Base);
        var destination = Path.Combine(alias, "fork.json");

        catalog.IsBundledFile(destination).ShouldBeFalse();
        new PdkJsonSaver(catalog).SaveToFile(new PdkDraft { Name = "Writable fork" }, destination);

        new PdkLoader().LoadFromFileForEditing(Path.Combine(userDirectory, "fork.json"))
            .Name.ShouldBe("Writable fork");
        File.ReadAllText(source).ShouldBe("bundled source");
        File.Exists(destination + ".tmp").ShouldBeFalse();
    }

    private async Task<(string Physical, string Alias)> CreateProtectedAliasAsync(string kind)
    {
        Directory.CreateDirectory(Base);
        if (kind == "repository")
        {
            Directory.CreateDirectory(Repo);
            return (Repo, await CreateAliasAsync("repo-alias", Repo));
        }
        if (kind == "linked-candidate")
        {
            var physical = Path.Combine(_root, "foundry-storage");
            Directory.CreateDirectory(physical);
            await CreateDirectoryLinkAsync(Shipped, physical);
            // The candidate is an alias, but the write uses its physical directory.
            return (physical, physical);
        }
        Directory.CreateDirectory(Shipped);
        if (kind == "ancestor")
        {
            var alias = await CreateAliasAsync("app-alias", Path.Combine(_root, "app"));
            return (Shipped, Path.Combine(alias, "bin", "PDKs"));
        }
        return (Shipped, await CreateAliasAsync("shipped-alias", Shipped));
    }

    private async Task<string> CreateAliasAsync(string name, string target)
    {
        var alias = Path.Combine(_root, name);
        await CreateDirectoryLinkAsync(alias, target);
        return alias;
    }

    private async Task CreateDirectoryLinkAsync(string alias, string target)
    {
        try
        {
            // Relative targets also require resolution relative to the link's parent.
            Directory.CreateSymbolicLink(alias, Path.GetRelativePath(Path.GetDirectoryName(alias)!, target));
        }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
        {
            await TemporaryDirectoryJunction.CreateAsync(_root, alias, target);
        }
        _links.Add(alias);
    }

    public void Dispose()
    {
        // Remove aliases themselves before recursively removing the owned fixture directory.
        foreach (var link in _links.AsEnumerable().Reverse()) Directory.Delete(link);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
