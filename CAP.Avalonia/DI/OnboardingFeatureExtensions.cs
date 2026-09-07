using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the onboarding feature: the guided first-steps tour started from
/// the Home screen's "Learn Lunima" card (issue #1080, slice 1 of #769).
/// </summary>
internal static class OnboardingFeatureExtensions
{
    /// <summary>Adds both guided tours as singletons: the first-steps tour over the shared canvas, the 'Watch it compute' tour over the Logic panel.</summary>
    public static IServiceCollection AddOnboardingFeature(this IServiceCollection services)
    {
        services.AddSingleton<TutorialViewModel>();
        // LogicPanelViewModel is transient; the tour must observe the one panel the window shows.
        services.AddSingleton(sp => new WatchComputeTourViewModel(
            sp.GetRequiredService<ViewModels.Panels.RightPanelViewModel>().Logic));

        return services;
    }
}
