using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Microsoft.Extensions.DependencyInjection;
using UnitTests.Helpers;
using Moq;
using Shouldly;

namespace UnitTests.Architecture;

public sealed class BundledPdkCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lunima-catalog-{Guid.NewGuid():N}");
    private string Base => Path.Combine(_root, "app", "bin");
    private string Shipped => Path.Combine(Base, "PDKs");
    private string Repo => Path.Combine(_root, "CAP-DataAccess", "PDKs");

    [Fact]
    public void Inventory_PrefersNonemptyRepoSource_AndContainsOnlyJsonFiles()
    {
        Directory.CreateDirectory(Repo);
        Directory.CreateDirectory(Shipped);
        File.WriteAllText(Path.Combine(Repo, "foundry.json"), "{}");
        File.WriteAllText(Path.Combine(Repo, "notes.txt"), "notes");
        File.WriteAllText(Path.Combine(Shipped, "stale.json"), "{}");
        var inventory = new BundledPdkCatalog(Base).ReadInventory();
        inventory.ShouldNotBeNull();
        inventory.Directory.ShouldBe(Repo);
        inventory.Files.ShouldBe(new[] { Path.Combine(Repo, "foundry.json") });
    }

    [Fact]
    public void Inventory_DistinguishesMissingAndEmptyShippedDirectory_AndIgnoresEmptyRepo()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(Repo);
        var catalog = new BundledPdkCatalog(Base);
        catalog.ReadInventory().ShouldBeNull();
        Directory.CreateDirectory(Shipped);
        var inventory = catalog.ReadInventory();
        inventory.ShouldNotBeNull();
        inventory.Directory.ShouldBe(Shipped);
        inventory.Files.ShouldBeEmpty();
    }

    [Fact]
    public void WriteBoundary_IncludesShippedAndEmptyRepoCandidates_ButNotUserForks()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(Repo);
        var catalog = new BundledPdkCatalog(Base);
        catalog.IsBundledFile(Path.Combine(Shipped, "foundry.json")).ShouldBeTrue();
        catalog.IsBundledFile(Path.Combine(Repo, "foundry.json")).ShouldBeTrue();
        catalog.IsBundledFile(Path.Combine(_root, "user-pdks", "fork.json")).ShouldBeFalse();
    }

    [Fact]
    public void Saver_UsesConfiguredCatalogGuard_BeforeCreatingAnyFile()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "virtual-foundry.json");
        var catalog = new Mock<IBundledPdkCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.IsBundledFile(target)).Returns(true);
        var saver = new PdkJsonSaver(catalog.Object);
        Should.Throw<UnauthorizedAccessException>(() => saver.SaveToFile(new PdkDraft { Name = "Protected" }, target));
        File.Exists(target).ShouldBeFalse();
        File.Exists(target + ".tmp").ShouldBeFalse();
        catalog.VerifyAll();
    }

    [Fact]
    public async Task ProductionCatalog_UsesOneConfiguredOwner_AndSaverUsesItsContract()
    {
        await using (var production = ProductionContainerTestHelper.BuildWithTempPreferences(Path.Combine(_root, "preferences.json")))
            production.GetRequiredService<IBundledPdkCatalog>().ShouldBeSameAs(production.GetRequiredService<BundledPdkCatalog>());
        var services = new ServiceCollection();
        CAP.Avalonia.App.ConfigureServices(services);
        var target = Path.Combine(_root, "configured-guard.json");
        var guard = new Mock<IBundledPdkCatalog>(MockBehavior.Strict);
        guard.Setup(value => value.IsBundledFile(target)).Returns(true);
        services.AddSingleton(guard.Object);
        await using var provider = services.BuildServiceProvider();
        Should.Throw<UnauthorizedAccessException>(() => provider.GetRequiredService<IPdkWriter>()
            .SaveToFile(new PdkDraft { Name = "Protected" }, target));
        File.Exists(target).ShouldBeFalse();
        guard.VerifyAll();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
