using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the ISA playground tool window's ViewModel (issue #1194) and the
/// shared <see cref="BuiltLogicNetworkProvider"/> (issue #1215) it consumes.
/// </summary>
internal static class IsaPlaygroundFeatureExtensions
{
    /// <summary>
    /// Adds the <see cref="IsaPlaygroundViewModel"/> as a singleton: the playground
    /// window is single-instance, so its state survives re-opening via the Tools flyout.
    /// The constructor only probes for <c>examples/isa</c> on disk and never throws,
    /// so building the production container headlessly is safe. The provider is the
    /// hand-off the Logic panel (registered in AddCanvasAndPanels) publishes its
    /// assembled network to; both sides must share the one singleton instance.
    /// </summary>
    public static IServiceCollection AddIsaPlaygroundFeature(this IServiceCollection services)
    {
        services.AddSingleton<BuiltLogicNetworkProvider>();
        services.AddSingleton(sp => new IsaPlaygroundViewModel(sp.GetRequiredService<BuiltLogicNetworkProvider>()));
        return services;
    }
}
