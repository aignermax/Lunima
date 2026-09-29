using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the ISA playground tool window's ViewModel (issue #1194).
/// </summary>
internal static class IsaPlaygroundFeatureExtensions
{
    /// <summary>
    /// Adds the <see cref="IsaPlaygroundViewModel"/> as a singleton: the playground
    /// window is single-instance, so its state survives re-opening via the Tools flyout.
    /// The constructor only probes for <c>examples/isa</c> on disk and never throws,
    /// so building the production container headlessly is safe.
    /// </summary>
    public static IServiceCollection AddIsaPlaygroundFeature(this IServiceCollection services)
    {
        services.AddSingleton<IsaPlaygroundViewModel>();
        return services;
    }
}
