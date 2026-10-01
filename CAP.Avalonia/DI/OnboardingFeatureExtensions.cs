using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the onboarding feature: the guided first-steps tour started from
/// the Home screen's "Learn Lunima" card (issue #1080, slice 1 of #769).
/// </summary>
internal static class OnboardingFeatureExtensions
{
    /// <summary>Adds the guided tours as singletons: the first-steps tour over the shared canvas, the 'Watch it compute' tour over the Logic panel, and the 'Run a program on your chip' tour over both plus the ISA playground.</summary>
    public static IServiceCollection AddOnboardingFeature(this IServiceCollection services)
    {
        services.AddSingleton<TutorialViewModel>();
        // LogicPanelViewModel is transient; the tour must observe the one panel the window shows.
        services.AddSingleton(sp => new WatchComputeTourViewModel(
            sp.GetRequiredService<ViewModels.Panels.RightPanelViewModel>().Logic,
            sp.GetRequiredService<ViewModels.Panels.BottomPanelViewModel>().Analysis));
        // Same for the ISA playground: the tour observes the singleton the playground window shows.
        services.AddSingleton(sp => new RunProgramTourViewModel(
            sp.GetRequiredService<ViewModels.Panels.RightPanelViewModel>().Logic,
            sp.GetRequiredService<ViewModels.Panels.BottomPanelViewModel>().Analysis,
            sp.GetRequiredService<ViewModels.Logic.IsaPlayground.IsaPlaygroundViewModel>()));
        // Same for the chiplet tour: it observes the canvas, the Design Checks
        // the window shows and the shared undo history (its helper move is undoable).
        services.AddSingleton(sp => new ConnectChipletsTourViewModel(
            sp.GetRequiredService<ViewModels.Canvas.DesignCanvasViewModel>(),
            sp.GetRequiredService<ViewModels.Panels.RightPanelViewModel>().DesignValidation,
            sp.GetRequiredService<ViewModels.Panels.BottomPanelViewModel>().Analysis,
            sp.GetRequiredService<Commands.CommandManager>()));

        return services;
    }
}
