using CAP.Avalonia.Services.ComponentRegistry;
using CAP_Core.ComponentRegistry.RegistryClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Architecture;

public sealed class RegistryContractContainerTests
{
    [Fact]
    public async Task CatalogAndAdoptionPorts_ReuseConfiguredOwners()
    {
        await using var services = ProductionContainerTestHelper.BuildWithTempPreferences(
            Path.Combine(Path.GetTempPath(), $"lunima-registry-di-{Guid.NewGuid():N}.json"));
        services.GetRequiredService<IRegistryCatalog>().ShouldBeSameAs(services.GetRequiredService<RegistryClient>());
        services.GetRequiredService<IRegistryAdoption>().ShouldBeSameAs(services.GetRequiredService<RegistryDownloadService>());
    }
}
