using CAP.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CAP.Avalonia.DI;

/// <summary>Shared example discovery for independently composed Home and ISA features.</summary>
internal static class ExampleCatalogFeatureExtensions
{
    internal static IServiceCollection AddExampleCatalog(this IServiceCollection services)
    {
        services.TryAddSingleton<FileExampleCatalog>();
        services.TryAddSingleton<IExampleFileCatalog>(sp => sp.GetRequiredService<FileExampleCatalog>());
        services.TryAddSingleton<ExampleDesignsService>();
        return services;
    }
}
