using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Notifications;
using CAP.Avalonia.Services.Solvers;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Export.OpenEbl;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.PdkResolution;
using CAP.Avalonia.ViewModels.Properties.Editors;
using CAP.Avalonia.ViewModels.Settings;
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Shell;
using CAP_Core;
using CAP_Core.Export;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Persistence.PIR;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>Resolves window feature factories at the composition root, before the view is shown.</summary>
public static class MainWindowComposition
{
    public static MainWindow Create(IServiceProvider services, MainViewModel model, Func<Task>? retire = null)
    {
        var editors = new ComponentEditorHostFactory(owner => new ComponentSettingsDialogFactory(owner,
            services.GetService<ErrorConsoleService>(), services.GetService<IPortMappingDialogService>(),
            services.GetService<IFdtdSMatrixService>(), services.GetService<NazcaComponentPreviewService>(),
            services.GetService<GdsFactoryComponentPreviewService>(), services.GetService<IDockerSetupDialogService>(),
            services.GetService<INotificationService>(), services.GetService<FdtdBackendRegistry>(), services.GetService<IUrlLauncher>(),
            services.GetService<Services.Lifetime.ApplicationWorkCoordinator>()),
            services.GetService<IUserPdkLibrary>(), services.GetService<ITemplateSMatrixStore>(),
            services.GetService<ProcessEditorFactory>());
        var tools = new ShellToolWindowFactory(
            () => services.GetRequiredService<SettingsWindowViewModel>(),
            owner => ModeSolverWindowComposition.ShowAsync(services, owner), () => services.GetService<IsaPlaygroundViewModel>(),
            () => services.GetService<RunProgramTourViewModel>(), () => services.GetService<PdkResolutionCheckViewModel>(),
            () => services.GetService<OpenEblCheckViewModel>());
        var panels = new ShellPanelDialogBindings(services.GetService<PdkImportService>(),
            services.GetService<OnaAnalyzerEditorProvider>(), services.GetService<NotificationService>());
        return new MainWindow(editors, tools, panels) { DataContext = model, RetireApplicationAsync = retire };
    }
}
